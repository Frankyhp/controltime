# Phase 16 — Administration and Configuration Hardening

## Goal

Improve protection against casual bypass, per `docs/PLAN_CONTROLTIME.md` (Phase 16): admin session expiration, sensitive actions, password change, controlled application exit, disabling monitoring, settings modification.

Status: implemented.

## Scope

In scope:

1. Core — `AdminAuthService`:
   - `ChangePasswordAsync(current, new)` returning an explicit result (`Success` / `InvalidPassword` / `NotConfigured` / `TooShort` / `InvalidCharacters`): verifies the current password, persists a new PBKDF2 hash, keeps admin mode active (the user just proved knowledge of the password). Same validation rules as `SetPasswordAsync` (min length 8, whitespace-only rejected).
   - `VerifyPasswordAsync(password)` returning `bool`, with no side effects (does not enter admin mode). Used by the controlled-exit prompt.
   - Attempt lockout: 5 consecutive failed authentications produce a 30-second cooldown during which authentication returns a new `LockedOut` result. In-memory state, driven by the injected `TimeProvider` (testable with a fake clock), fully reset by a successful authentication. Applies to `TryEnterAdminModeAsync` and `ChangePasswordAsync`; `VerifyPasswordAsync` also registers failures and returns `false` while locked out (it returns `bool`, so the exit prompt cannot distinguish lockout from a wrong password).
2. App — new "Ajustes" screen (third tab in `MainViewModel`): `SettingsViewModel` + `Views/SettingsView`, reusing the Rules-screen admin-gate pattern (`IAdminAuthService` + 1 s `DispatcherTimer` for expiry, setup/unlock panels, admin bar). Contents:
   - Change-password form (current / new / confirmation), errors translated to Spanish, including "Demasiados intentos. Espera unos segundos."
   - "Iniciar con Windows" toggle (moved from the tray): requires admin mode like any enforcement-affecting setting; uses `IAutostartRegistrar` + `AutoStartEnabled` app setting (Phase 15 keys and sync unchanged).
   - Read-only enforcement status (activo / desactivado por `CONTROLTIME_ENFORCEMENT`).
3. App — controlled exit: tray "Salir de ControlTime" always prompts for the administrator password (a minimal WPF window with a `PasswordBox`, no full MVVM), verified with `VerifyPasswordAsync`. Success → `MainWindow.CloseForShutdown()` + `Application.Shutdown()`; failure, lockout or cancel → no exit. The prompt result never enters admin mode.
4. App — `TrayIconService`: remove the "Iniciar con Windows" menu item and its toggle logic. The startup preference sync in `App.OnStartup` (`SyncAutoStartFromSettingsAsync` behavior) stays as is. Rest of the tray menu unchanged.

Out of scope (later phases or documented limitations):

- Windows Service / enforcement without the UI process (Phase 17).
- Encryption or tamper-resistance of the SQLite database; any claim of protection against a Windows local administrator (see Security model in `AGENTS.md`).
- Moving the `CONTROLTIME_ENFORCEMENT=off` kill switch: kept exactly as is (environment variable evaluated only in App composition; requires access to Windows environment settings, outside the casual-user threat model). Documented as a known limitation.
- Persistent (across-restart) lockout counters.

## Admin session expiration review

Reviewed, no change: 5-minute time-limited admin mode (Phase 05) remains the policy; the mechanism already uses an injectable `TimeProvider`. The Ajustes screen honors expiry exactly like the Rules screen (toggle and password form disable on expiry).

## Tests

New Core unit tests (`AdminAuthServiceTests` extension or a sibling file, in-memory settings store + fake `TimeProvider`):

- change password: correct current password succeeds and the new password verifies; old password no longer verifies
- change password with wrong current password returns `InvalidPassword` and stores nothing
- change password before setup returns `NotConfigured`
- weak new password (`TooShort`, whitespace-only) rejected, original password still valid
- successful change keeps admin mode active
- `VerifyPasswordAsync`: true/false without entering admin mode (`IsAdminMode` stays false)
- lockout boundaries: 4th failure still allows immediate retry; 5th failure locks; retry just before the 30-second deadline still `LockedOut`; at/after the deadline authentication is allowed again
- a successful authentication before the lockout threshold resets the failure counter
- lockout applies to `ChangePasswordAsync` as well as `TryEnterAdminModeAsync`

UI (Ajustes screen, exit prompt, tray change) smoke-tested manually, consistent with Phases 06/13/15 precedent.

Existing suite (182 tests) must stay green; `dotnet build` must be warning-free.

## Manual validation

- Ajustes tab: locked without admin mode (same unlock flow as Reglas); admin bar and expiry lock editing after 5 minutes.
- Change password end-to-end: after changing, the unlock panel rejects the old password and accepts the new one.
- Enter 5 wrong passwords on any unlock/change form → prompt locks; wait 30 s → works again.
- Tray "Salir" with wrong password or Cancel: app keeps running; with correct password: tray icon disappears and process terminates.
- Five wrong passwords in the exit prompt also trigger the 30-second lockout.
- Tray menu no longer shows "Iniciar con Windows"; the Ajustes toggle still writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ControlTime` and persists `AutoStartEnabled`.
- With `CONTROLTIME_ENFORCEMENT=off`: Ajustes shows enforcement as disabled by environment variable; startup warning still logged (existing behavior).

## Decisions

- Exit always asks for the password (even while admin mode is active), mirroring the per-action authentication of "Regalar tiempo" (Phase 07).
- Lockout is in-memory, 5 failures / 30 seconds: enough for casual-user deterrence, no persistence or new tables; a process restart resets it (documented limitation).
- The autostart toggle moves from the tray to Ajustes because it is a setting that affects enforcement (see `docs/product-rules.md`); a `ToolStripMenuItem` is a poor surface for password entry anyway.
- `VerifyPasswordAsync` is separated from `TryEnterAdminModeAsync` so the exit prompt does not open an admin session as a side effect.
- New Ajustes screen instead of extending the Rules screen: AGENTS.md already lists Settings as a target screen and no later phase owns it.

## Known limitations

- Protects against normal/casual users only. A Windows local administrator can edit/delete the SQLite database, change environment variables (including `CONTROLTIME_ENFORCEMENT=off`), or kill the process.
- The lockout counter lives in memory: restarting the app clears it.
- The exit gate is UI-level: killing the process still bypasses monitoring and enforcement (fixed by the Phase 17 Windows Service).
- No audit trail of administrative actions (possible later hardening).
