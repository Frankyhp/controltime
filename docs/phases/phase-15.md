# Phase 15 — System Tray and Startup

## Goal

Make ControlTime behave like a persistent Windows utility: closing the main window keeps the application running in the system tray, and automatic startup after Windows user login becomes available as an optional user preference.

## Scope

In scope:

- Visible tray icon with context menu: Open ControlTime, current status, remaining time, "Iniciar con Windows" toggle, "Salir de ControlTime".
- Closing the main window (X) hides it instead of exiting; monitoring and enforcement keep running.
- Real exit only from the tray menu ("Salir de ControlTime"), which stops monitoring through normal container disposal.
- Optional automatic startup at user login via `HKCU\...\Run`, default off, toggle in the tray menu, preference persisted in app settings.

Out of scope (later phases):

- Admin-password-protected exit and settings hardening (Phase 16).
- Running without the UI process / Windows Service (Phase 17).
- Installer-configured startup (Phase 21).

## Plan

### Tray (App)

- New `TrayIconService` (`App/Services/`): single visible `NotifyIcon` (WinForms is already enabled) that also implements `INotifier`, replacing `BalloonTipNotifier` (Phase 13 left its icon hidden on purpose; this phase activates it and the old class is removed).
- Context menu (Spanish, consistent with the UI):
  - "Abrir ControlTime" → shows the main window.
  - Disabled informational items: access state (Permitido / Advertencia / Bloqueado) and remaining time, updated from `MonitoringHost.StateChanged` (same es-ES `H:mm` formatting as the Dashboard); the tray tooltip mirrors the status.
  - Checkable "Iniciar con Windows" → toggles the autostart registrar and persists the preference.
  - "Salir de ControlTime" → raises exit; no admin gate (Phase 16).
- Double-click on the tray icon shows the main window.

### Close window vs exit

- `MainWindow.OnClosing`: unless a shutdown flag is set, cancel and `Hide()`.
- `App.xaml`: `ShutdownMode="OnExplicitShutdown"` so the app lifetime is owned by the tray exit path only.
- Tray "Salir": hide the icon, set the window shutdown flag, `Application.Shutdown()` → `OnExit` disposes the container, which stops `MonitoringHost` and disposes the tray service.

### Startup

- `IAutostartRegistrar` (`Core/Abstractions/`): `Enable()`, `Disable()`, `IsEnabled()`.
- `WindowsAutoStartRegistry` (`Infrastructure/Windows/`): writes/removes the `ControlTime` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` pointing at `Environment.ProcessPath`; no administrator privileges required.
- Preference persisted via `IAppSettingsStore` key `AutoStartEnabled`; `App.OnStartup` syncs the registry to the stored preference on divergence (failures logged, never fatal).
- Toggle default: off until the user enables it.

### Wiring

- `App.OnStartup`: resolve `TrayIconService`, sync autostart preference, subscribe tray status updates to `MonitoringHost.StateChanged`, handle the tray exit request.
- `MainWindow` becomes a DI singleton so window and tray share the same instance.
- DI: `INotifier` resolves to the `TrayIconService` singleton; `IAutostartRegistrar` → `WindowsAutoStartRegistry`.

## Implementation notes

- `Microsoft.Win32.Registry` required no PackageReference (part of the shared framework); the only addition was `[SupportedOSPlatform("windows")]` on `WindowsAutoStartRegistry` to keep the build warning-free.
- `MainWindow` is now a DI singleton so the tray and the app composition share the same instance.
- `App.OnStartup` resolves `TrayIconService`, subscribes `UpdateState` to `MonitoringHost.StateChanged` (try/catch logged), fire-and-forgets `SyncAutoStartFromSettingsAsync()`, and unsubscribes in `OnExit` before container disposal.

## Tests

- No new Core business logic beyond the `IAutostartRegistrar` abstraction, so no new automated tests (precedent: Phases 10/13/14 Windows-integration/UI layers are smoke-tested manually).
- `dotnet build`: succeeded, 0 warnings, 0 errors.
- `dotnet test`: 182 passed, 0 failed.

## Manual validation

Executed by the agent (smoke):

- App launched: process stable, window rendered, tray icon created, no `HKCU\...\Run\ControlTime` value (default off respected).
- `WM_CLOSE` sent to the main window (equivalent to pressing X): process stayed alive and `MainWindowHandle` became 0 (window hidden, app still running in tray).

Remaining checklist for the user:

- Tray menu shows live state ("Estado: …" / "Tiempo restante: …") and tray tooltip mirrors it.
- Double-click / "Abrir ControlTime" returns the window with live data.
- Enable "Iniciar con Windows": `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ControlTime` appears pointing at the executable; disable removes it; state persists across app restarts and Windows re-login.
- Balloon warnings still work with the shared icon.
- "Salir de ControlTime": tray icon disappears and the process terminates (monitoring stops).

## Decisions

- Single `NotifyIcon` shared by tray and balloon notifications (`BalloonTipNotifier` removed).
- Startup is optional, default off, toggled from the tray menu (no product rule requires it; the Phase 21 installer can configure it later).
- `HKCU\Run` chosen over the Startup folder: per-user, atomic, no extra files.
- Tray exit is not admin-gated in this phase (Phase 16 owns controlled exit).

## Known limitations

- While hidden, the app process is still required for monitoring and enforcement; killing the process bypasses both (Windows Service arrives in Phase 17).
- The tray exit is available to any user able to reach the tray (hardening in Phase 16).
- If the executable is moved, the `HKCU\Run` value goes stale until the toggle is re-applied.
