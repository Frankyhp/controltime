# Phase 17a — Windows Service Foundation

Status: implemented. Service registration and live lock validation are manual (see Manual validation).

## Goal

Introduce a Windows Service (`ControlTime.Service`, runs as SYSTEM) that owns usage tracking and enforcement, independent of the graphical application. Closing or killing the App must no longer stop enforcement.

```text
ControlTime.Service (SYSTEM, session 0)
  1 s tick → IActivityMonitor (WTS) → UsageTrackingService → DashboardService
           → EnforcementService → ISessionLock (WTSDisconnectSession)
```

Core is reused unchanged (RuleEngine, UsageService, DashboardService, UsageTrackingService, EnforcementService with the 30 s grace). No business rules are duplicated.

## Scope

In scope:

- New project `src/ControlTime.Service` (Worker Service, `Microsoft.Extensions.Hosting.WindowsServices`, `UseWindowsService()`; runnable as console for development). References Core + Infrastructure.
- Machine-wide database: `ControlTimeStorage` defaults to `%PROGRAMDATA%\ControlTime`; `MigrateLegacyDatabase()` moves the legacy `%APPDATA%\ControlTime` database automatically on startup (called by both App and Service).
- Infrastructure WTS additions:
  - `WtsConsoleSessionMonitor : IActivityMonitor`: active console session connected → `Active`; no active session or disconnected → `Locked`. No idle (Service cannot read another session's input).
  - `WtsSessionLock : ISessionLock`: `WTSDisconnectSession(WTS_CURRENT_SERVER_HANDLE, activeConsoleSessionId)`; logs and never throws; user experience equivalent to a lock.
- Service loop (`BackgroundService`, 1 s `PeriodicTimer`): tick → tracking → snapshot → enforcement; per-tick try/catch logged; kill switch `CONTROLTIME_ENFORCEMENT=off` now evaluated in Service composition.
- App becomes consumer-only: no local `UsageTrackingService` tick, no local enforcement subscription (no double session writes); keeps local activity probe for display and read-only dashboard polling.
- `MonitoringService` Core change: `UsageTrackingService?` optional (App composes it without tracking).
- Manual service registration documented (`sc create` / `sc start` / `sc failure`); installer belongs to Phase 21.

Out of scope (17b / 18 / 21):

- Named-pipe IPC, App as Service client, admin mode via Service, App activity reports.
- Idle detection improvements, user-session agent, installer.

## Plan

1. Infrastructure:
   - `ControlTimeStorage`: default directory → `CommonApplicationData\ControlTime`; add `MigrateLegacyDatabase()` (legacy `%APPDATA%` file exists and ProgramData absent → move `db`, `db-wal`, `db-shm`; logged).
   - `Windows/WtsApi.cs`: minimal P/Invoke (`WTSGetActiveConsoleSessionId`, `WTSQuerySessionInformation` connect state, `WTSDisconnectSession`, `WTSFreeMemory`).
   - `Windows/WtsConsoleSessionMonitor.cs`: probe per call; `INVALID_ACTIVE_CONSOLE_SESSION` or `WTSDisconnected` → `Locked`; `WTSActive` → `Active`; failures logged → `Locked` (fail-safe: unknown console state must not count usage).
   - `Windows/WtsSessionLock.cs`: disconnects the active console session; no active session → no-op success; failures logged via `ILogger`, returns false, never throws.
2. Core:
   - `MonitoringService`: tracking step optional (`UsageTrackingService?`); when null, tick = probe + snapshot only.
3. Service project:
   - `Program.cs`: `Host.CreateApplicationBuilder`, `UseWindowsService()`; composition mirrors App.xaml.cs (DbContextFactory → ProgramData path, stores, hasher, `RuleEngine`, `UsageService`, `DashboardService`, `UsageTrackingService`, `MonitoringService`, `EnforcementService`, WTS monitor/lock); `Migrate()` + `MigrateLegacyDatabase()` on startup; logging to EventLog + Debug.
   - `MonitoringLoop.cs`: 1 s loop, invokes tracking+snapshot+enforcement per tick; enforcement skipped (with logged warning) when kill switch set; `TickFailed` logged, loop continues.
   - `appsettings` none required; no configuration surface yet.
4. App:
   - `CreateServices`: register `MonitoringService` with null tracking; remove `ISessionLock`/`EnforcementService` registrations and the `StateChanged` enforcement subscription; remove `UsageTrackingService` singleton usage in App tick.
   - Local `WindowsActivityMonitor` and dashboard polling remain (display only).
   - Keep `CONTROLTIME_ENFORCEMENT` read only for the Settings screen label; the authoritative kill switch moves to the Service.
5. Registration (manual, documented in this file's validation section):
   `sc create ControlTimeService binPath= "<path>\ControlTime.Service.exe" start= delayed-auto`
   `sc failure ControlTimeService reset= 86400 actions= restart/60000/restart/60000/restart/60000`
   `sc start ControlTimeService`
6. Tests:
   - Core: `MonitoringService` with null tracker performs no tracking and still returns a snapshot.
   - Existing suites unchanged and green (182+ tests).
   - WTS probes/lock are thin P/Invoke wrappers (no unit tests; covered by manual validation).

## Manual validation

Registration (elevated shell):

```text
sc create ControlTimeService binPath= "<ruta>\ControlTime.Service.exe" start= delayed-auto
sc failure ControlTimeService reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc start ControlTimeService
```

- `sc` registration → Service running → exhausting a rule's allowance locks the console via disconnect (access screen) within ~1 s + grace semantics preserved.
- Re-login after a Service lock → fresh `Active` episode → 30 s margin before re-lock.
- App closed (tray exit) or process killed while `Blocked` → Service keeps locking (the core goal of the phase).
- Legacy `%APPDATA%` database present and ProgramData absent → App startup moves it; rules/usage history preserved.
- `Blocked` with no enabled rules → never locks (unchanged).
- Kill switch on the Service (`CONTROLTIME_ENFORCEMENT=off` in SYSTEM environment) → no lock + startup warning.

## Decisions

- `WTSDisconnectSession` as the Service lock mechanism (session-0 `LockWorkStation` cannot lock the user session).
- Session connect state is read via `WTSEnumerateSessionsW` (stable, classic API). `WTSQuerySessionInformation(WTS_INFO_CLASS 28)` proved unreliable on the development machine (RPC error 1722) and was dropped.
- ProgramData database with automatic legacy move instead of per-user storage (shared by SYSTEM and users; protection level unchanged — still not safe against a local administrator).
- Service is the sole usage-session writer from 17a on; App never writes sessions.
- Fail-safe console state mapping: unknown/WTS failures map to `Locked` (never invents usage).

## Known limitations (accepted until 17b/18)

- No idle source on the Service: while the UI is closed, connected-session time counts as usage.
- UI admin mode does not suspend Service enforcement (use granted time to work while `Blocked`); fixed in 17b.
- App shows no "Service unavailable" indication yet (17b).
- Service cannot observe a manual Win+L lock (session stays `WTSActive`): usage keeps counting and enforcement proceeds normally; refined in 18.

## Implementation

- `ControlTime.Infrastructure` `ControlTimeStorage`: default database directory is now `%PROGRAMDATA%\ControlTime`; `MigrateLegacyDatabase()` moves a legacy `%APPDATA%\ControlTime` database (plus `-wal`/`-shm`) once, tolerating a concurrent move by the other process.
- `ControlTime.Infrastructure` `Windows/WtsApi`: `WTSGetActiveConsoleSessionId`, `WTSEnumerateSessionsW` (session connect state) and `WTSDisconnectSession` P/Invoke with the official `WTS_CONNECTSTATE_CLASS` values.
- `ControlTime.Infrastructure` `Windows/WtsConsoleSessionMonitor : IActivityMonitor`: console session `Active`/`Connected` → `Active`; no console session, anything else, or any failure → `Locked` (fail-safe, never invents usage).
- `ControlTime.Infrastructure` `Windows/WtsSessionLock : ISessionLock`: disconnects the active console session; already disconnected / no console session → success no-op; failures logged, never thrown.
- `ControlTime.Core` `MonitoringService`: `UsageTrackingService?` optional third parameter; when null, the tick performs probe + snapshot only.
- New project `src/ControlTime.Service` (Worker, `net10.0-windows`, `AddWindowsService`, added to the solution):
  - `Program.cs`: composition mirrors the App minus UI — ProgramData DbContextFactory, stores, hasher, `AdminAuthService` (no admin mode entry point yet), `RuleEngine`, `UsageService`, `DashboardService`, `UsageTrackingService`, `MonitoringService` (with tracking), WTS monitor + `WtsSessionLock`, `EnforcementService`, `MonitoringLoop` as hosted service.
  - `MonitoringLoop.cs`: startup legacy-move + `Migrate()`, then a 1 s `PeriodicTimer` tick (probe → tracking → snapshot → enforcement); per-tick failures logged, loop continues; `CONTROLTIME_ENFORCEMENT=off` (Service environment) disables enforcement with a logged warning.
- `ControlTime.App`: no longer registers `UsageTrackingService`, `ISessionLock` or `EnforcementService`; `MonitoringService` composed without tracking (display-only); the enforcement subscription was removed; `MigrateLegacyDatabase()` runs in the App DB preparation; `CONTROLTIME_ENFORCEMENT` remains only as the Settings screen label.

## Tests

198 tests green (`dotnet test`), solution build clean (`dotnet build`).

- `MonitoringServiceTests`: new test — tick without a tracking service returns activity + snapshot and writes no usage sessions. Existing fixture updated to the new constructor.
- WTS monitor/lock are thin P/Invoke wrappers: validated by a console smoke run (real WTS probes, real session enumeration) and by the manual checklist, not unit tests.

## Console smoke run (development machine)

Running `ControlTime.Service.exe` as a normal user in console mode: legacy `%APPDATA%` database moved to `C:\ProgramData\ControlTime\controltime.db`, migrations applied, monitoring loop ticking every 1 s against the real session, orphan session adoption observed, WTS probe returning `Active`, kill-switch warning logged. No exceptions.
