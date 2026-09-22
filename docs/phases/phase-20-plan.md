# Phase 20 — Reliability and Recovery (PLAN — not implemented)

Status: planned, not started. This file is the implementation plan; on completion it will be condensed into `docs/phases/phase-20.md`.

## Goal

Strengthen ControlTime (App + Service) for long-term normal use: persistent diagnostics, crash tolerance, clock-change safety, clean shutdown, and graceful handling of corrupted data.

## Current weak points (verified in code)

- App logging is `AddDebug()` only (`App.xaml.cs` `CreateServices`): nothing survives to disk. Service logs go to the Windows Event Log via `AddWindowsService` only.
- `MonitoringLoop.PrepareDatabase()` (Service startup `Migrate()` + legacy move) runs unguarded inside `ExecuteAsync`: an unreadable/corrupt database kills the host silently.
- No App-domain crash handling: no `DispatcherUnhandledException` / `TaskScheduler.UnobservedTaskException` handlers.
- Clock changes are unhandled in usage math: `UsageCalculator` does not clamp sessions with `EndedAt < StartedAt`, sessions entirely in the future, or negative spans caused by backward clock jumps / DST shifts. `UsageTrackingService` deletes a session that fails `TryClose` (silent data loss).
- No clean session close on shutdown: `sc stop` / Windows shutdown leaves an open session to be adopted on next Service start (adoption works but is not an explicit close).
- Corrupted rows are all-or-nothing: a single invalid rules row (via `UsageRuleMapper`) fails the whole store load; `AdminAuthService` behaviour with an unreadable password setting is not explicit.

## Scope

### 20.1 — Persistent logging

- Minimal rolling file logger provider implemented in `ControlTime.Infrastructure` (no new third-party libraries), writing to `%PROGRAMDATA%\ControlTime\logs\` (one file per day or size-rolled, with a small retention cap).
- Registered in both App and Service DI (`AddLogging`).
- Audit and fill logging gaps for: startup, persistence failures, monitoring failures, Windows API failures, enforcement events, Service/IPC failures (`ControlTimePipeServer` / `ControlTimePipeClient` / `ServiceClientHost`).

### 20.2 — Crash tolerance

- App: `DispatcherUnhandledException` + `TaskScheduler.UnobservedTaskException` handlers → log, then decide continue vs. clean shutdown (never leave usage data half-written).
- Service: wrap `PrepareDatabase()` with bounded retry + critical log; Service must fail safe (no enforcement, no usage writes) rather than die silently when the database is unreadable/corrupt at startup.

### 20.3 — Clock changes, DST, midnight (Core, test-first)

- `UsageCalculator`: explicitly ignore/clamp sessions with `EndedAt < StartedAt`, sessions starting after `asOf`/day end (future sessions), and negative overlaps; never produce negative or absurd daily usage.
- `UsageTrackingService`: on backward clock jump with an open session, avoid deleting the session (close at `StartedAt` and log) instead of losing it.
- New Core tests: clock jump forward/backward, DST transition day, midnight crossing with open session, `asOf` earlier than session start, future-dated sessions ignored.

### 20.4 — Ordered shutdown and incomplete sessions

- Service: on `StopAsync`/`ApplicationStopping`, close the open usage session with the last known `LastInputAt` (explicit close instead of relying only on orphan adoption).
- Verify orphan adoption still survives: Service killed, Windows restart, Service restart.

### 20.5 — Corrupted configuration / invalid data

- Per-row tolerance in rules loading: an invalid row is logged and skipped, never fails the whole load.
- `AdminAuthService` with unreadable/corrupt password setting: explicit behaviour (no admin mode, clear error surfaced in Settings).

## Out of scope

- Installer / packaging (Phase 21).
- Anti-tampering hardening, database encryption, analytics.
- New third-party logging or resilience libraries.

## Decisions (proposed)

- File logging via a small custom `ILoggerProvider` in Infrastructure rather than Serilog/NLog (scope rule: simplest solution, no new frameworks).
- Service remains the only place that closes sessions; the App never writes usage data (unchanged invariant).
- Corrupt-data policy is "skip + log", never "repair silently" and never "fail the whole screen".
- Clock-change safety stays in Core as pure functions (`UsageCalculator` / `UsageTrackingService`) so it is unit-testable without Windows.

## Acceptance criteria

- `dotnet build` and `dotnet test` green, including new tests for 20.3 and 20.5.
- Logs observable on disk from both App and Service after each manual scenario below.
- Manual validation checklist passes (where feasible on the dev machine).
- `docs/phases/phase-20.md` written; this plan file removed or reduced to a pointer.

## Manual validation checklist

1. Kill the Service process (`taskkill`) → sessions recover via adoption on restart; tooltip/banner reflects Service unavailability meanwhile; log entries written.
2. Change the system clock backward while a session is open → no negative usage, no lost session, day totals remain coherent on Dashboard/Historial.
3. Change the system clock forward across midnight → day rolls over, previous day closed correctly.
4. Shut down Windows with an open session → after boot, session closed sensibly (no multi-hour phantom usage).
5. `sc stop ControlTimeService` with an open session → session closed explicitly in the database.
6. Corrupt one rules row (manual SQL edit) → Rules screen loads with the valid rules, warning logged.
7. Make the database temporarily unreadable (file locked/permissions) → Service logs critical, retries, does not enforce blindly; App shows its startup error.

## Known risks / open questions

- Rolling-file retention size/days not yet chosen (suggest: one file per day, keep 14, cap per-file size).
- `DispatcherUnhandledException` continue-vs-exit policy may need per-scenario tuning during implementation.
- Whether Service should retry-forever (with backoff) or give up after N attempts on startup database failure — plan says bounded retry then fail-safe idle loop, revisit if noisy.
