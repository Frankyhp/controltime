# Phase 11 — Automatic Usage Tracking

## Goal

Connect activity detection (Phase 10) with usage sessions (Phase 09): activity opens/continues sessions, idle/lock closes them, and the Dashboard now shows real usage today including the session currently in progress.

## Scope

* Core: `UsageTrackingService` (state machine), `UsageCalculator` gains `asOf` (open sessions count up to now), `UsageService.GetUsageForDayAsync` passes `asOf`, `IUsageSessionStore` gains `GetOpenSessionAsync`/`DeleteAsync`
* Infrastructure: `SqliteUsageSessionStore` implements the new store members
* App: `UsageTrackingHost` — app-level `DispatcherTimer` (~5 s) driving ticks; started from `App.OnStartup`, disposed with the container

Out of scope: dedicated background monitoring component (Phase 12), warnings/notifications (Phase 13), enforcement (Phase 14), idle threshold as user setting (Phase 16).

## Implementation

Core:

* `Services/UsageTrackingService.TickAsync(ActivityStatus)`:
  * first tick loads any orphan open session from the store (startup/adoption after normal or abnormal exit)
  * `Active` with no open session → `TryCreateOpen(status.Timestamp)` + `AddAsync`
  * `Active` with an open session (own or orphan) → continue; no duplicate is ever created (singleton + in-memory open-session cache + reentrancy guard)
  * `Idle`/`Locked` with an open session → close at `max(StartedAt, status.LastInputAt)`: usage ended at the last input, not when the idle threshold elapsed, so the ~5 min idle threshold is never billed as usage
  * close would produce a zero-usage session (`LastInputAt <= StartedAt`) → `DeleteAsync` (discard) instead of `UpdateAsync`
  * midnight needs no special case: open sessions keep running and `UsageCalculator` intersects per day
* `UsageCalculator.GetUsageForDay(..., asOf?)`: with `asOf`, an open session contributes `[StartedAt, min(asOf, dayEnd))`; without `asOf`, open sessions are ignored (previous behaviour unchanged)
* `UsageService.GetUsageTodayAsync` passes `asOf = now`, so the Dashboard accumulates the in-progress session live

App:

* `Services/UsageTrackingHost`: `DispatcherTimer` (`DefaultTickInterval` 5 s) + one immediate tick at `Start()`; each tick is `TickAsync(monitor.GetCurrentStatus())`; failures are logged (`ILogger`) and the loop continues; `Dispose` stops the timer
* Precision does not depend on the tick interval: closes use `LastInputAt`; only session start may lag up to one interval

## Tests

17 new tests (145 total, green):

* `UsageTrackingServiceTests` (9): Active opens at `Timestamp`; repeated Active does not duplicate; Idle closes at `LastInputAt` (not `Timestamp`); Locked closes; Idle without session is a no-op; Active adopts an orphan open session without opening another; first-tick Idle closes the orphan at `LastInputAt`; zero-usage close discards the session; idle-then-active cycle yields closed + new open session
* `UsageCalculatorTests` (5 new): open session with `asOf` counts up to it; `asOf` beyond the window clamps; midnight-crossing open session splits 30/30; `asOf` before start returns zero; closed + open summed
* `UsageSessionPersistenceTests` (3 new): no open → null; returns the latest open; `DeleteAsync` removes and unknown id throws

## Manual validation

Startup smoke test executed end-to-end with synthetic zero-movement input (SendInput) since agent activity does not count as user input:

* machine idle ≥ threshold → app runs, no session created (correct)
* after input → tracker created exactly one open session in the development database
* process killed (simulated abnormal shutdown) and app relaunched while active → the orphan session was adopted with the same `Id`, no duplicate
* test rows cleaned from the development database afterwards

Checklist for the user: use the computer normally → a session appears in the Dashboard "Uso registrado hoy" within ~5 s; leave the machine idle past the threshold → usage stops growing and the session closes at the moment of the last input.

## Decisions

* Abnormal shutdown policy (agreed): on restart with the machine active, the orphan open session is adopted and continues; without a heartbeat the exact crash time is unknowable, so the crash interval may be counted as usage
* Session closes at `LastInputAt`, not at detection time: the idle threshold (5 min) never inflates recorded usage
* Zero-usage sessions (`LastInputAt <= StartedAt`, e.g. lock immediately after open) are discarded via new `DeleteAsync` rather than relaxing `TryClose`'s `end > start` rule
* Tracking driver is an app-level `DispatcherTimer` host, not a UI-screen concern; Phase 12 may replace it with the formal background monitoring component
* Midnight handled purely by day-intersection math; sessions are never split at midnight

## Known limitations

* Adoption after a crash can overcount usage for the period the app was dead while the machine stayed active; a heartbeat (`LastSeenActiveAt` on open sessions) would be the fix if this proves relevant
* `UsageTrackingHost` runs on the UI thread's dispatcher: if the UI is frozen, tracking pauses (no ticks) — sessions close conservatively at `LastInputAt` when it resumes
* Tick interval 5 s delays session start by up to 5 s after a wake from idle
* If the app never sees `Idle`/`Locked` again before shutdown, the open session stays open until the next adoption pass
