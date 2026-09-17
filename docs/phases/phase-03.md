# Phase 03 — Rule Engine

## Goal

Implement deterministic rule evaluation in `ControlTime.Core`: given the current instant, usage today and the configured rules, decide whether usage is allowed.

## Scope

* `RuleEngine` class with `RuleResult Evaluate(DateTimeOffset now, TimeSpan usageToday, IReadOnlyCollection<UsageRule> rules)`
* No time grants yet (Phase 07), no persistence, no Windows or UI dependencies

## Implementation

`src/ControlTime.Core/RuleEngine.cs` (namespace `ControlTime.Core`).

* Constructor accepts an optional warning threshold, default `DefaultWarningThreshold = 5 minutes`; negative thresholds throw.
* Evaluation per enabled rule: allowed weekday → inside schedule `[Start, End)` → daily limit not reached (`usage >= limit` blocks; `null` limit is unlimited).
* Any enabled rule fully satisfied → `Allowed` (`Warning` when remaining time ≤ threshold). `RemainingTime` is the most permissive value among satisfied rules; `null` when one of them is unlimited.
* No rule satisfied → `Blocked` with reason (no enabled rules / forbidden day / outside schedule / limit reached) and `NextAllowedAt`: the earliest future schedule start on an allowed day within the next 7 days, computed with fixed-offset arithmetic. When blocked by the daily limit the search starts at the next day (usage assumed to reset at midnight).
* Disabled rules are ignored entirely. `null` rules or negative usage throw.

## Tests

26 new unit tests in `RuleEngineTests` (48 total), covering:

* empty rule set and all-disabled set → Blocked (fail-closed)
* schedule boundaries: exactly at Start allowed, exactly at End blocked, ±1 minute
* midnight schedule boundaries (`00:00–06:00`)
* forbidden day and `NextAllowedAt` across a weekend/week wrap
* daily limit: below, reached exactly, exceeded; next allowed start after midnight
* warning: remaining below threshold, exactly at threshold, one second above
* unlimited rules, mixed unlimited/limited, most-permissive remaining with multiple rules
* union semantics: one rule forbidding while another allows → Allowed
* multiple blocked rules → earliest `NextAllowedAt` (including a later window the same day)
* disabled rules never influence the result
* argument validation (negative usage, null rules, negative threshold)

## Manual validation

Not applicable (pure domain logic, fully unit-tested).

## Decisions

* Fail-closed: no enabled rule means usage is blocked.
* Warning threshold is provided via the `RuleEngine` constructor, keeping the `Evaluate` signature from `AGENTS.md`; Phase 13 can supply its own thresholds.
* Union-of-permissions semantics: any satisfied rule allows; reported remaining time is the most permissive.
* `NextAllowedAt` predicts only day/schedule restrictions (limits are assumed reset at local midnight).

## Known limitations

* Time grants are not considered yet (Phase 07 will extend the API).
* `NextAllowedAt` uses fixed UTC-offset arithmetic; daylight-saving offset changes are not modeled.
* Warning is evaluated only against the most permissive remaining time; if an unlimited rule is satisfied, no warning is raised.
