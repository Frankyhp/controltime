# Phase 14 — Enforcement

## Goal

Apply restrictions when Core determines usage is not allowed: lock the Windows session while access is `Blocked`.

```text
RuleEngine → Blocked → EnforcementService → ISessionLock → LockWorkStation
```

Core decides; Infrastructure performs the Windows action; the App only composes them.

## Scope

In scope:

- `ISessionLock` abstraction in Core; `WindowsSessionLock` (`LockWorkStation`) in Infrastructure.
- `EnforcementService` in Core: pure decision logic fed by the 1 s `MonitorState` stream.
- Lock policy: lock only with enabled rules while `Blocked`, never while already `Locked` or while admin mode is active; edge-triggered once per `Blocked` episode plus re-lock on each new `Active` episode while `Blocked` persists.
- App wiring through `MonitoringHost.StateChanged` with failures logged, never thrown.
- Test-only kill switch: environment variable `CONTROLTIME_ENFORCEMENT=off` evaluated exclusively in App composition (Core/Infrastructure unaffected).

Out of scope:

- System tray and window-to-tray hiding (Phase 15).
- User-configurable enforcement settings (Settings, Phase 16).
- Windows Service / separation of UI and enforcement (later phases).
- Any protection against a Windows administrator bypassing the app.

## Plan

1. Core:
   - `Abstractions/ISessionLock`: `bool TryLock()` (success/failure, no Windows details).
   - `Services/EnforcementService(MonitorState Process)`: locks only when `Snapshot.HasEnabledRules && Snapshot.State == Blocked && Activity.State != Locked && !IAdminAuthService.IsAdminMode`.
   - Edge trigger: one lock on entering the `Blocked` episode; while `Blocked` persists, one additional lock each time activity transitions back to `Active` (prevents calling `LockWorkStation` every second); state resets on `Allowed`/`Warning`.
   - If `TryLock()` returns false or throws, the lock is retried on the following ticks (no false "already locked" marking).
2. Infrastructure:
   - `Windows/WindowsSessionLock : ISessionLock`: `user32.dll!LockWorkStation` P/Invoke; returns false on `false` return or exception; logs failures via `ILogger<WindowsSessionLock>`; never throws (enforcement failures must be logged, no destructive operations).
3. App:
   - DI: `ISessionLock → WindowsSessionLock`, `EnforcementService` singletons.
   - `App.OnStartup`: subscribe `EnforcementService.Process` to `MonitoringHost.StateChanged` wrapped in try/catch logged via `ILogger<EnforcementService>`.
   - Kill switch: if `CONTROLTIME_ENFORCEMENT` equals `off` (case-insensitive), skip the subscription and log a warning at startup; environment parsing lives only in App composition.
4. Tests (xUnit, Core only): fake `ISessionLock`, fake `IAdminAuthService`.
   - Never locks on `Allowed`/`Warning`, on `Blocked` without enabled rules, or while `Activity == Locked`.
   - Exactly one lock per entry into a `Blocked` episode.
   - Re-lock once per new `Active` episode while still `Blocked` (Idle does not trigger re-lock).
   - Admin mode suspends locking; expiry of admin mode while `Blocked` locks.
   - `TryLock()` failure is retried on the next tick.
5. Verification: `dotnet test`, `dotnet build`, manual lock validation.

## Implementation

- `ControlTime.Core` `ISessionLock` (`Abstractions/`): `bool TryLock()`, no Windows details.
- `ControlTime.Core` `EnforcementService` (`Services/`): fed by `MonitorState` ticks. Locks only when `HasEnabledRules && State == Blocked && Activity != Locked && !IsAdminMode`. Edge trigger per `Blocked` episode plus re-lock on each new `Active` episode while `Blocked` persists (`Idle` does not re-trigger); `Locked` marks the episode as handled; a failed or throwing `TryLock()` is retried on the next tick; state resets on `Allowed`/`Warning`.
- `ControlTime.Infrastructure` `Windows/WindowsSessionLock : ISessionLock`: `user32.dll!LockWorkStation` P/Invoke; logs the lock and any Win32/other failure via `ILogger<WindowsSessionLock>`; never throws.
- `ControlTime.App` `App.xaml.cs`: DI registrations (`ISessionLock → WindowsSessionLock`, `EnforcementService` singletons); `EnforcementService.Process` subscribed to `MonitoringHost.StateChanged` (after the Phase 13 notifier) wrapped in try/catch logged via `ILogger<EnforcementService>`.
- Kill switch: `CONTROLTIME_ENFORCEMENT=off` (case-insensitive) evaluated only in App composition; when set, the subscription is skipped and a startup warning is logged. No environment access in Core or Infrastructure.

## Tests

182 tests green (`dotnet test`), solution build clean (`dotnet build`).

- `EnforcementServiceTests` (fake `ISessionLock`, fake `IAdminAuthService`): never locks on `Allowed`/`Warning` (Active or Idle), without enabled rules, or while already `Locked`; exactly one lock per `Blocked` episode entry; one re-lock per new `Active` episode while `Blocked` persists (Idle→Active re-arms, repeated Active does not); admin mode suspends; expiry of admin mode while `Blocked` locks; new `Blocked` episode after `Allowed` locks again; failed `TryLock()` and throwing `TryLock()` are retried on the next tick without crashing.

## Manual validation

- With enforcement on: exhaust a rule's daily allowance (or wait for schedule end) → the Windows session locks within ~1 s of `Blocked`, a "Sesión de Windows bloqueada" information log appears, and the Dashboard shows Bloqueado.
- Unlock while still `Blocked` → the session locks again as soon as input resumes.
- Enter admin mode (password) while `Blocked` → no lock; after the 5-minute admin mode expires → locks.
- With `CONTROLTIME_ENFORCEMENT=off` set in the environment: no lock occurs and a startup warning is logged.
- Fresh install with no enabled rules → never locks.

## Decisions

- Re-lock policy: lock on entering `Blocked` plus once per activity episode while `Blocked` persists (no per-second `LockWorkStation` spam, and unlocking does not grant a free session).
- Admin mode suspends enforcement; when the 5-minute admin mode expires with `Blocked` still active, the session is locked.
- Test-only kill switch via `CONTROLTIME_ENFORCEMENT=off` environment variable handled in App composition only; real enforcement settings arrive with Phase 16.
- `Blocked` without enabled rules never locks: a fresh installation must not lock the machine before rules exist.
- Enforcement stays in-process (`MonitoringHost` ticks); Windows Service separation belongs to later phases.

## Known limitations

- Up to ~1 s of control remains between the `Blocked` decision and the lock (1 s monitoring tick).
- Usage continuing during that interval is recorded as real usage in sessions (by design).
- Enforcement is in-process: closing the app or killing the process bypasses it; hardening (tray persistence, Windows Service) belongs to later phases.
- A Windows administrator can still end the process or edit the database; the mechanism is configuration protection, not system security.
- Repeated `LockWorkStation` failures log once per tick until they succeed; no backoff yet.
