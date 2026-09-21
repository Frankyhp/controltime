# Phase 12 — Background Monitoring

## Goal

Make usage monitoring independent from individual UI screens by introducing an application-level background monitoring component that periodically evaluates activity, maintains usage sessions, evaluates rules, calculates the current application state, and updates the UI — without blocking the WPF UI thread.

## Scope

* Core: `MonitorState` (activity + dashboard snapshot composite) and `MonitoringService` (one composed tick: probe activity → track usage → build dashboard snapshot)
* Infrastructure: `IDbContextFactory<ControlTimeDbContext>`; all four stores open a short-lived context per operation (background loop + UI can now touch SQLite concurrently)
* App: `MonitoringHost` replaces `UsageTrackingHost` — single 1 s background loop (`Task.Run` + `PeriodicTimer`) raising `StateChanged` marshaled to the UI `Dispatcher`; `DashboardViewModel` becomes pure presentation (no timers, no activity probe)

Out of scope: Windows Service, warnings/notifications (Phase 13), enforcement (Phase 14), idle threshold as user setting (Phase 16).

## Implementation

Core:

* `Domain/MonitorState`: record pairing `ActivityStatus` + `DashboardSnapshot` — the single value the background monitor publishes per tick
* `Services/MonitoringService.TickAsync(utcOffset, ct)`: one probe via `IActivityMonitor.GetCurrentStatus()` → `UsageTrackingService.TickAsync(status)` → `DashboardService.GetSnapshotAsync(offset)` → `MonitorState`; no scheduling in Core (the host owns it, as decided in Phase 10)

Infrastructure:

* `SqliteUsageRuleStore` / `SqliteAppSettingsStore` / `SqliteTimeGrantStore` / `SqliteUsageSessionStore` now take `IDbContextFactory<ControlTimeDbContext>` and open a `ControlTimeDbContext` per operation (`await using ... factory.CreateDbContext()`), so the background loop and the UI never share a `DbContext` instance; also removes the long-lived change tracker

App:

* `Services/MonitoringHost` (replaces `UsageTrackingHost`): `Start()` captures the UI `Dispatcher` and runs a loop on the thread pool; immediate first tick, then `PeriodicTimer` at `DefaultTickInterval` (1 s); each tick runs `MonitoringService.TickAsync(DateTimeOffset.Now.Offset)`; success caches `LastState` and raises `StateChanged` via `Dispatcher.BeginInvoke`, failure is logged (`ILogger`) and raises `TickFailed`, loop continues; `Dispose` cancels and waits up to 2 s for the loop
* `App.OnStartup`: `AddDbContextFactory`, `Migrate()` via a short-lived factory context, `MonitoringHost.Start()`; host stopped by container disposal on exit
* `DashboardViewModel`: subscribes to `MonitoringHost.StateChanged`/`TickFailed` on `Initialize()` (applying `LastState` immediately), unsubscribes on `Stop()`; dropped its `DispatcherTimer`, its `IActivityMonitor` dependency and its own snapshot polling — the "Actividad" card is now fed by the same centralized per-tick probe

## Tests

5 new tests (150 total, green):

* `MonitoringServiceTests`: Active tick opens a session and publishes activity + consistent snapshot (rule limit, Allowed state); Active→Idle closes at `LastInputAt` and the snapshot's usage reflects the closed session; exactly one activity probe per tick; repeated Active never duplicates sessions; UTC offset propagates so same-local-day grants count as bonus
* Shared doubles extracted to `TestDoubles.cs` (`FixedTimeProvider` with settable `Now`, fake rule/grant/session stores, `FakeActivityMonitor` with probe counter) and reused by `DashboardServiceTests`
* Persistence tests adapted via `TestDbContextFactory` (creates a fresh context per call, mirroring production); assertions unchanged

## Manual validation

Checklist for the user (not executed by the agent to avoid writing usage data into the development database):

1. Start the app → Dashboard shows state, usage and activity within ~1 s and updates continuously while the Dashboard is not even loaded (monitoring is app-level)
2. Use the computer normally → "Uso registrado hoy" grows within ~1 s of activity (was ~5 s)
3. Switch to the Reglas tab → tracking keeps running (verify back on Dashboard that usage kept growing while on another screen)
4. Leave the machine idle past the threshold → usage stops growing; session closes at the moment of last input
5. Freeze/hog the UI briefly (e.g. open a rule editor with many rules) → tracking is no longer tied to the UI dispatcher and continues on background threads
6. Simultaneous UI action + background tick (grant bonus while watching usage grow) → no "second operation started" DbContext errors in debug output

## Decisions

* DbContext concurrency solved with `IDbContextFactory` + per-operation context (chosen over a global async semaphore or keeping the singleton, which is not thread-safe once the loop left the UI thread)
* Single unified 1 s monitoring loop (chosen over 1 s state / 5 s tracking split): negligible local cost, faster session start after wake-from-idle, no stale-state window between tracking and dashboard, one centralized activity probe
* Scheduling lives in App (`MonitoringHost`); Core's `MonitoringService` stays a deterministic, testable composition of a single tick
* `DashboardViewModel` keeps `Initialize()`/`Stop()` as subscribe/unsubscribe (view Loaded/Unloaded), so the pushed state survives tab navigation; `StateChanged` is marshaled to the dispatcher by the host, keeping the VM free of threading concerns

## Known limitations

* Last-state is cached in memory only; after a tick failure the Dashboard shows the previous state plus an error banner until the next success
* The host loop reads `DateTimeOffset.Now.Offset` per tick from the machine's local zone (App layer); Core remains offset-agnostic
* If the app is closed while a session is open, closure still depends on the next-run adoption pass (Phase 11 behaviour, unchanged)
