# Phase 04 — Persistence Foundation

## Goal

Introduce local persistence for usage rules and application settings using SQLite and EF Core, with explicit migrations.

## Scope

* EF Core `DbContext` with two entities: usage rules and key/value app settings
* Store implementations behind Core interfaces
* First explicit migration (`InitialCreate`)
* Round-trip integration tests against a temporary SQLite file

Out of scope: usage sessions, TimeGrant, DI/logging container, admin password storage, UI wiring.

## Implementation

Packages (Infrastructure only): `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 and `Microsoft.EntityFrameworkCore.Design` 10.0.12. `dotnet-ef` 10.0.12 pinned via local tool manifest (`dotnet-tools.json`).

Core (`Abstractions/`):

* `IUsageRuleStore` — `GetAllAsync`, `AddAsync`, `UpdateAsync`, `RemoveAsync(Guid)`
* `IAppSettingsStore` — `GetAsync(key)`, `SetAsync(key, value)` (string values; concrete settings arrive in later phases)

Infrastructure:

* `ControlTimeDbContext` — `UsageRules` + `AppSettings` tables; `AppSettingEntity` keyed by `Key`
* `UsageRuleEntity` — `Guid Id`, `Name`, `Enabled`, `AllowedDays` (CSV of weekday integers), `ScheduleStart`/`ScheduleEnd` (`TimeOnly`), `DailyLimitTicks` (`long?`; `NULL` = unlimited)
* `UsageRuleMapper` — entity ↔ domain; loading revalidates through the existing `AllowedSchedule.TryCreate` / `UsageRule.TryCreate` and throws `InvalidOperationException` for corrupt persisted data
* `SqliteUsageRuleStore`, `SqliteAppSettingsStore`
* `ControlTimeStorage` — database located at `%APPDATA%\ControlTime\controltime.db` (directory overridable for tests/tools)
* `ControlTimeDbContextFactory` — design-time factory so `dotnet ef` runs against Infrastructure alone
* `Migrations/` — `InitialCreate`

Database creation is always via `Migrate()`. `EnsureDeleted` is never used; model changes require new migrations.

Tests reference `ControlTime.Infrastructure` (only test project with a DB dependency); production dependency direction is unchanged.

## Tests

6 new integration tests (54 total) in `PersistenceIntegrationTests`, each on a fresh temp SQLite file with `MigrateAsync()`:

* rule round-trip preserving id/name/enabled/days/schedule/limit
* `null` daily limit round-trips as unlimited
* update persists name, enabled, days and limit changes
* remove deletes and second remove returns `false`
* settings get (missing key → `null`), set, read from a fresh context, and set-twice upsert

## Manual validation

Not automated beyond tests: the production file is created on first migration at `%APPDATA%\ControlTime\controltime.db` when a store context migrates it (exercised by the UI wiring phase).

## Decisions

* Allowed days stored as a CSV text column (max 7 values; no child table).
* Database stored under `%APPDATA%\ControlTime`.
* Generic key/value settings table for now; typed settings introduced with the phases that need them.
* Loading maps through the domain `TryCreate` factories so stored data can never produce invalid domain objects silently.

## Known limitations

* No DI/logging integration yet; stores take a `DbContext` directly.
* Settings values are plain strings; the admin password hashing phase will define its own persistence.
* Corrupt persisted rules throw on load instead of being quarantined or repaired.
