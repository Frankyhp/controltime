# Phase 07 — Bonus Time / Time Grants

## Goal

Allow an administrator to grant extra usage minutes for the current day. The grant raises the effective daily allowance without modifying rules, and never bypasses weekday or schedule restrictions.

## Scope

* Core: `TimeGrant` domain type + `ITimeGrantStore` abstraction
* Core: `RuleEngine` evaluates an optional `bonusToday` added to each rule's daily limit
* Infrastructure: `TimeGrantEntity`, `TimeGrants` DbSet, EF migration `AddTimeGrants`, `SqliteTimeGrantStore`
* App: "Regalar tiempo" inline panel in the Rules screen; every grant operation requires the administrator password
* Grants are append-only (auditable). No editing or deleting grants in this phase.

Out of scope: dashboard display of grants (Phase 08), usage sessions (Phase 09), grant history UI (Phase 19).

## Implementation

Core:

* `Domain/TimeGrant`: immutable, `Id`/`GrantedAt (DateTimeOffset)`/`Minutes (int)`, `Amount => TimeSpan.FromMinutes(Minutes)`; `TryCreate` factories return `DomainResult`, minutes must be positive
* `Abstractions/ITimeGrantStore`: `AddAsync` + `GetGrantsForDayAsync(DateOnly day, TimeSpan utcOffset)` — day boundaries resolved through the explicit offset, no ambient time zone in Core
* `RuleEngine.Evaluate` gained optional `TimeSpan bonusToday = default` (negative throws): limit check becomes `usageToday >= limit + bonusToday` and `remaining = max(limit + bonusToday - usageToday)`. Weekdays, schedules and unlimited rules are unaffected by the bonus.

Infrastructure:

* `TimeGrantEntity` stores `GrantedAtUtc (DateTime)` (UTC text column keeps SQLite range comparisons safe); `SqliteTimeGrantStore` filters `[dayStartUtc, dayEndUtc)` and maps back to the domain, rejecting corrupted persisted data
* Migration `AddTimeGrants` creates the `TimeGrants` table; existing databases are preserved (applied on startup via `Migrate()`)

App:

* DI registers `ITimeGrantStore`; `RulesViewModel` receives it plus an optional `TimeProvider`
* Rules screen gained a permanent "Tiempo extra para hoy" area: "Regalar tiempo" opens an inline row (minutes + administrator `PasswordBox` + Confirmar/Cancelar, password via code-behind plumbing as in Phase 06)
* Confirm flow: parse positive minutes → `TryEnterAdminModeAsync` (password is requested on every grant, even while already in admin mode) → `TimeGrant.TryCreate(now, minutes)` → `AddAsync` → status shows minutes granted and today's total; wrong password shows "Contraseña incorrecta."; missing password falls back to the first-run setup panel
* The dashboard does not yet consume the bonus; the RuleEngine parameter is ready for Phase 08

## Tests

17 new tests (90 total, green):

* `TimeGrantTests`: non-positive minutes rejected; fields/`Amount` exposed
* `BonusTimeRuleEngineTests`: zero bonus keeps previous behaviour; allowance 120+30; multiple grants summed; usage exactly at limit+bonus blocked; warning threshold measured against allowance+bonus; unlimited rule yields no remaining time; forbidden weekday and outside-schedule still blocked despite bonus; negative bonus throws
* `TimeGrantPersistenceTests` (temp-file SQLite): round-trip across contexts; other days ignored; same-day grants summed; day boundaries honoured through the explicit UTC offset

## Manual validation

Startup smoke test executed (migration applied, window renders). Checklist for the user:

* "Regalar tiempo" → wrong password rejected with error; correct password stores the grant and status shows today's total
* Restart app → panel closes but previously granted minutes remain in the database
* Minutes not numeric or ≤ 0 → Spanish validation error, nothing stored
* Bonus never unlocks a forbidden weekday or an outside-schedule time (no UI yet consumes remaining time until Phase 08; verified by engine tests)

## Decisions

* Minutes stored as `int` at domain level; converted to `TimeSpan` only at evaluation time
* Optional `bonusToday` parameter on `Evaluate` instead of a new API → existing callers and 73 previous tests untouched
* `GetGrantsForDayAsync` takes an explicit `utcOffset`; App computes it from `TimeZoneInfo.Local` (presentation concern)
* Inline panel instead of a modal window: keeps the Phase 06 "no dialogs" decision and avoids constructing windows through a service locator
* Grant confirmation is a code-behind click handler (like unlock) because `PasswordBox` has no bindable password; the business flow stays in the ViewModel
* Grants are append-only; no revoke/undo in this phase

## Known limitations

* The granted bonus is not displayed anywhere except transiently in the status message; the Dashboard (Phase 08) will surface it
* Usage is still zero/simulated until Phase 09+, so the bonus has no observable effect on blocking in the live app yet
* Day attribution of a grant uses the machine's local offset at query time; a grant stored while the clock/time zone changes could shift days (edge case, revisited with reliability Phase 20)
