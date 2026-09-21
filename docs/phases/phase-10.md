# Phase 10 — Windows Activity Detection

## Goal

Detect whether the logged-in Windows user is actively using the computer (`Active` / `Idle` / `Locked`), observed only: no restrictions are enforced and no usage sessions are produced yet.

## Scope

* Core: `ActivityState` enum, `ActivityStatus` record, `ActivityEvaluator` (pure state logic), `IActivityMonitor`
* Infrastructure: `Windows/` folder — `LastInputInfo` (idle time via P/Invoke), `SessionLockProbe` (lock detection via P/Invoke), `WindowsActivityMonitor` (`IActivityMonitor` implementation)
* App: DI registration of `IActivityMonitor`; Dashboard gains an "Actividad" card refreshed with the existing ~1 s timer

Out of scope: start/stop of usage sessions from activity (Phase 11), background monitoring component (Phase 12), enforcement (Phase 14), idle threshold exposed in Settings UI (Phase 16).

## Implementation

Core:

* `Domain/ActivityState`: `Active`, `Idle`, `Locked`
* `Domain/ActivityStatus`: record `(State, LastInputAt, Timestamp)`
* `Services/ActivityEvaluator.Evaluate(now, lastInputAt, isLocked, idleThreshold)`: `Locked` takes precedence; `Idle` when `now - lastInputAt >= threshold`; otherwise `Active`; non-positive threshold or future `lastInputAt` throw `ArgumentOutOfRangeException`; `Timestamp = now`
* `Abstractions/IActivityMonitor`: single synchronous `GetCurrentStatus()` — cheap polling call, safe to invoke from a background thread in Phase 12 (no events, no window handle)

Infrastructure (kept on `net10.0`; pure P/Invoke requires no TFM change or extra packages besides `Microsoft.Extensions.Logging.Abstractions`):

* `LastInputInfo.GetIdleTime()`: `GetLastInputInfo` (user32) + `GetTickCount64` (kernel32); unsigned wrap-around subtraction handles the 32-bit `dwTime` rollover; failure throws `Win32Exception`
* `SessionLockProbe.IsLocked()`: `OpenInputDesktop(MAXIMUM_ALLOWED)`; if the input desktop cannot be opened, or its name (via `GetUserObjectInformation`) is `Winlogon` or `Disconnect`, the session is considered locked
* `WindowsActivityMonitor`: composes both probes with injected `TimeProvider`; computes `lastInputAt = now - idleTime`; delegates the decision to `ActivityEvaluator`; optional `ILogger<WindowsActivityMonitor>` logs and rethrows API failures; idle threshold configurable via constructor, default 5 minutes (`DefaultIdleThreshold`)

App:

* `App.xaml.cs` registers `IActivityMonitor` as a singleton (`WindowsActivityMonitor` with default threshold)
* `DashboardViewModel` receives `IActivityMonitor`; each ~1 s refresh updates `ActivityText`/`ActivityBrush` in a separate try/catch (monitor failure shows "No disponible" without breaking the rest of the dashboard); `Presentation/ActivityStateTranslator` maps states to Spanish labels
* `DashboardView.xaml`: new "Actividad" card (green Activo / orange Ocioso / gray Bloqueado)

## Tests

10 new tests (128 total, green):

* `ActivityEvaluatorTests`: recent input → `Active`; one second before threshold → `Active`; exactly at threshold → `Idle`; long ago → `Idle`; locked with recent input → `Locked`; locked while idle → `Locked`; `LastInputAt`/`Timestamp` preserved; future `lastInputAt` throws; zero and negative threshold throw

## Manual validation

Startup smoke test executed (window renders, dashboard shows "Actividad", process stable). Checklist for the user:

* Dashboard shows "Activo" (green) while using the computer
* After ≥ 5 minutes without input the card shows "Ocioso" (orange); moving the mouse returns it to "Activo"
* Locking the session (Win+L) and unlocking shows "Bloqueado" (gray) while locked

## Decisions

* Polling-only pure P/Invoke (`GetLastInputInfo` + `OpenInputDesktop`) instead of `SystemEvents.SessionSwitch`: no TFM change for Infrastructure, no packages beyond logging abstractions, thread-agnostic (ready for Phase 12 background use), and no cached event state to keep in sync
* Lock detection reads the input desktop name (`Winlogon`/`Disconnect` ⇒ locked); a visible UAC secure desktop also reports `Winlogon` and is treated as locked — acceptable while the phase is observation-only
* `IActivityMonitor.GetCurrentStatus()` is synchronous: both probes are sub-millisecond, and the consumer (Phase 12 monitor) will own the scheduling
* Idle threshold is constructor-configurable with an internal default (5 min); moving it to `AppSettings` is deferred to a settings phase
* Monitor failures fail visible-but-isolated: logged in Infrastructure, shown as "No disponible" on the dashboard, without affecting the access-state snapshot

## Known limitations

* `OpenInputDesktop`-based lock detection is a heuristic (though widely used); it also reports UAC secure-desktop prompts as locked
* "Locked" is only observable while the ControlTime UI process is running in the user session (same as everything until the Windows Service phase)
* The dashboard now performs one extra Windows API probe pair per second; negligible, to be revisited when Phase 12 introduces centralized polling
* No idle-time hysteresis: state flips to `Active` on the first input after idle, which is the intended Phase 11 semantics for resuming usage
