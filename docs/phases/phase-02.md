# Phase 02 — Domain Model

## Goal

Define the core domain concepts of ControlTime in `ControlTime.Core`, without persistence, WPF or Windows dependencies.

## Scope

* `AllowedSchedule`: allowed daily time window
* `UsageRule`: immutable usage rule (days, schedule, daily limit, enabled flag)
* `AccessState`: enum of possible access states
* `RuleResult`: data-only result shape for future rule evaluation
* `DomainResult`: shared validation outcome for `TryCreate` factories

No rule evaluation logic (Phase 03). No packages added.

## Implementation

All types live in `src/ControlTime.Core/Domain/` under namespace `ControlTime.Core.Domain`.

* `DomainResult` — `readonly record struct` with `Ok` / `Invalid(error)` factories and a `Failed` helper.
* `AllowedSchedule` — `TimeOnly Start`/`End`, private constructor, `TryCreate` rejecting `Start >= End`. Membership helpers with `[Start, End)` semantics: `IsWithin`, `IsBeforeSchedule`, `IsAfterSchedule`.
* `UsageRule` — immutable class with private constructor and `TryCreate` (explicit `Guid` overload for tests). Validation: non-blank name (trimmed), at least one allowed day, `DailyLimit` either `null` (unlimited) or strictly positive. Disabled rules are valid objects.
* `AccessState` — `Allowed`, `Warning`, `Blocked`.
* `RuleResult` — positional record: `State`, `Reason`, `RemainingTime`, `NextAllowedAt`. Data only; produced by the RuleEngine in Phase 03.

Tests use a `GlobalUsings.cs` (`ControlTime.Core.Domain`, `Xunit`) replacing the template `<Using>` entry.

## Tests

22 unit tests in `AllowedScheduleTests` and `UsageRuleTests`:

* schedule boundaries: exactly at Start inside, exactly at End outside, one minute before each boundary, mid-range
* `IsBeforeSchedule` / `IsAfterSchedule` boundary semantics
* invalid schedules rejected: `Start > End`, `Start == End` (no midnight-crossing support)
* valid rule construction; disabled rules constructible
* `null` daily limit accepted (unlimited); zero and negative limits rejected
* blank/whitespace name rejected; name trimmed; empty allowed-days set rejected
* smallest positive limit (1 second) accepted

## Manual validation

Not applicable (no UI, no runtime behavior).

## Decisions

* Validation failures use the `TryCreate` + `DomainResult` pattern instead of throwing constructors (explicit results for expected domain failures).
* `DailyLimit` is nullable: `null` means unlimited usage for the day.
* Schedules crossing midnight (`Start > End`) are rejected in this model.
* `[Start, End)` half-open interval: entering the schedule is inclusive, leaving it is exclusive.

## Known limitations

* No rule evaluation, conflicts resolution, warnings, TimeGrant or usage sessions — later phases.
* Schedules cannot cross midnight; revisit only if the product requires night-spanning access.
* `AccessState.Warning` and `RuleResult` fields are placeholders whose semantics will be finalized by the RuleEngine in Phase 03.
