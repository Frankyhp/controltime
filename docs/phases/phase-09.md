# Phase 09 — Usage Sessions

## Goal

Introduce the usage-history domain: usage is represented as sessions (`StartedAt`/`EndedAt`), never as an accumulated daily counter, with daily usage calculated from stored sessions.

## Scope

* Core: `UsageSession` domain type (open sessions supported via nullable `EndedAt`), `UsageCalculator` (pure day-intersection math), `IUsageSessionStore`, `UsageService`
* Infrastructure: `UsageSessionEntity`, EF migration `AddUsageSessions`, `SqliteUsageSessionStore`
* App: `DashboardService` evaluates with real usage today (from sessions) instead of `TimeSpan.Zero`; the dashboard's "Uso registrado hoy" is no longer simulated (still 0 until Phase 11 creates sessions)

Out of scope: activity detection (`IActivityMonitor`, Phase 10), automatic start/stop of sessions (Phase 11), background monitoring (Phase 12), usage history UI (Phase 19).

## Implementation

Core:

* `Domain/UsageSession`: immutable; `Id`, `StartedAt`, `EndedAt (DateTimeOffset?)`; `Duration` null while open; `TryCreate(start, end)` rejects `end <= start`; `TryCreateOpen(start)`; `TryClose(at, out closed)` returns a new closed session keeping `Id`/`StartedAt`
* `Services/UsageCalculator.GetUsageForDay(sessions, dayStartUtc, dayEndUtc)`: sum of `[Start, EndedAt) ∩ [dayStart, dayEnd)` intersections; open sessions ignored; handles midnight crossings and multi-day sessions naturally
* `Abstractions/IUsageSessionStore`: `AddAsync`, `UpdateAsync` (for closing), `GetSessionsOverlappingAsync(from, to)`
* `Services/UsageService`: `GetUsageTodayAsync(utcOffset)` / `GetUsageForDayAsync(day, utcOffset)` — explicit offset, ambient time zone stays a presentation concern; uses injected `TimeProvider`

Infrastructure:

* `UsageSessionEntity` (`Guid Id`, `StartedAtUtc`, `EndedAtUtc` nullable), index on `StartedAtUtc`, migration `AddUsageSessions`
* `SqliteUsageSessionStore`: overlap filter `StartedAtUtc < to && (EndedAtUtc is null || EndedAtUtc > from)`; loading revalidates through `TryCreate`/`TryCreateOpen`, corrupted data throws `InvalidOperationException`

App:

* DI registers `IUsageSessionStore` and `UsageService`; `DashboardService` gains `UsageService` and puts `UsageToday` into `DashboardSnapshot`; `DashboardViewModel` formats it (no more "simulado")

## Tests

22 new tests (118 total, green):

* `UsageSessionTests` (7): end ≤ start rejected; closed duration; open `Duration` null; `TryClose` validates and keeps id/start; closing an already-closed session rejected
* `UsageCalculatorTests` (9): no sessions → zero; inside day; outside day → zero; multiple summed; midnight crossing split 30/30 across two days; multi-day session clamped per day (2 h/24 h/1 h); full-window overlap clamped to 24 h; open session ignored; inverted window throws
* `UsageSessionPersistenceTests` (5, temp-file SQLite): round-trip open and closed sessions; `UpdateAsync` persists the close; unknown id on update throws; overlap query includes sessions started before the window and excludes non-overlapping ones; session ending exactly at window start excluded
* `DashboardServiceTests` (1 new + constructor adapted): usage of 1 h against a 2 h limit leaves 1 h remaining and stays allowed

## Manual validation

Startup smoke test executed (migration `AddUsageSessions` applied to the development database, window renders, process stable). Checklist for the user:

* App starts, dashboard renders with "Uso registrado hoy: 0:00" (no sessions exist yet)
* Inserting a session row directly into the `UsageSessions` table makes the dashboard show it within ~1 s and reduce "Tiempo restante" (optional check via a SQLite tool)

## Decisions

* Open sessions supported from the start (nullable `EndedAt`) to prepare crash-tolerant tracking in Phase 11 without changing the model later; day calculations only count closed sessions
* `UpdateAsync` included in the store interface now so closing a session does not require deleting/re-inserting rows
* The dashboard consumes real usage in this phase (agreed): the simulated zero is removed even though no session producer exists yet
* `TryClose` returns a new instance (domain values are immutable) instead of mutating

## Known limitations

* Nothing creates sessions yet: the producer (activity detection + automatic tracking) arrives in Phases 10–11, so the dashboard keeps showing 0 in normal use
* `TryCreateOpen` performs no validation on `startedAt`; open sessions are only created by trusted code (no UI input involved yet)
* Overlap queries are fine for the expected volume of sessions; no archiving strategy defined yet
* The dashboard now runs three store queries per second (rules, grants, sessions); acceptable until monitoring phases revisit polling
