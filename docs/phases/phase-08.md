# Phase 08 — Dashboard Foundation

## Goal

Create the primary application dashboard, validating the full flow UI → Core → Persistence before any Windows monitoring is introduced.

## Scope

* Core: `DashboardSnapshot` record + `DashboardService` (composes stores + `RuleEngine` into a single snapshot)
* App: `DashboardView` / `DashboardViewModel` showing current access state, configured daily limit, bonus today, effective daily allowance, active schedule, current rule status (reason / next allowed time)
* App: minimal navigation in `MainWindow` (Dashboard / Reglas) via `ContentControl` + `DataTemplate`s bound to a `MainViewModel`
* Usage today is simulated as zero (real tracking arrives in Phase 09+)

Out of scope: usage sessions (Phase 09), activity detection (Phase 10), warnings/notifications, session locking, settings screen, grant history UI.

## Plan

Core:

* `Domain/DashboardSnapshot`: `State`, `Reason`, `RemainingTime`, `NextAllowedAt`, `ConfiguredDailyLimit` (null = unlimited/no enabled rules), `BonusToday`, `ActiveSchedule`; computed `EffectiveDailyAllowance = ConfiguredDailyLimit + BonusToday`
* `Services/DashboardService`: loads enabled rules + today's grants (explicit `utcOffset`, ambient time zone stays a presentation concern), evaluates `RuleEngine.Evaluate(now, TimeSpan.Zero, rules, bonusToday)`, resolves the active schedule (rule satisfied now, or the rule matching `NextAllowedAt`)

App:

* DI registers `RuleEngine`, `DashboardService`, `DashboardViewModel`, `MainViewModel`
* `DashboardViewModel`: `DispatcherTimer` (~1 s) refresh, Spanish display strings (state, limit, bonus, allowance, schedule, reason, next allowed)
* `MainWindow`: tab-style header switching `Current` between `DashboardViewModel` and `RulesViewModel`; `RulesViewModel` lifecycle (Loaded/Unloaded) preserved through view recreation

## Implementation

Core:

* `Domain/DashboardSnapshot`: record with `State`, `Reason`, `RemainingTime`, `NextAllowedAt`, `ConfiguredDailyLimit`, `BonusToday`, `ActiveSchedule`, `HasEnabledRules`; computed `EffectiveDailyAllowance = ConfiguredDailyLimit + BonusToday` (null when unlimited)
* `Services/DashboardService`: loads rules + today's grants (explicit `utcOffset` parameter; ambient time zone stays in the App), evaluates `RuleEngine.Evaluate(now, TimeSpan.Zero, rules, bonusToday)`, derives the configured limit from enabled rules (`null` if any is unlimited) and resolves the active schedule (rule satisfied now, otherwise the rule matching `NextAllowedAt`)

App:

* `MainViewModel` holds `Current` (Dashboard/Rules ViewModel); `MainWindow` is now a shell with a tab-style header (`RadioButton`s + `ContentControl` with `DataTemplate`s per ViewModel); `RulesViewModel` lifecycle (Loaded/Unloaded → Initialize/Stop) keeps working because templates recreate the view on each switch
* `DashboardViewModel`: 1-second `DispatcherTimer` refresh, Spanish display strings, state color brush, `AccessReasonTranslator` maps the engine's blocked reasons to Spanish
* DI registers `RuleEngine` (singleton), `DashboardService`, `DashboardViewModel`, `MainViewModel`

## Tests

6 new tests (96 total, green):

* `DashboardServiceTests` (fakes for both stores + fixed `TimeProvider`): no enabled rules → blocked without limit info; inside schedule exposes limit/bonus/allowance/remaining; active schedule is the satisfied rule's window; outside schedule resolves the next allowed window's schedule; unlimited rule has no configured allowance; grants from other days excluded from bonus

## Manual validation

Startup smoke test executed (migration applied, window renders, process stable). Checklist for the user:

* Dashboard shows the correct state for the current time and configured rules
* After "regalar tiempo" in Rules, the dashboard shows the updated bonus and effective allowance within ~1 s
* Blocked states display the reason and (when available) the next allowed time
* Switching tabs preserves each screen's behaviour (admin gate, grants, rule CRUD)

## Decisions

* `DashboardService` lives in Core and consumes the store interfaces directly; ViewModels stay presentation-only
* Usage today is hardcoded zero ("0:00 (simulado)") in the ViewModel until Phase 09 supplies real sessions
* `HasEnabledRules` flag added to the snapshot to distinguish "no limit because unlimited rule" from "no limit because nothing is configured"
* Navigation implemented with `ContentControl` + implicit `DataTemplate`s instead of a navigation framework; no dialogs introduced
* Blocked-reason translation to Spanish lives in `Presentation/AccessReasonTranslator`; unknown reasons fall back to the original text

## Known limitations

* The dashboard refresh timer polls the SQLite stores every second; acceptable until monitoring phases introduce push-based state
* RuleEngine still evaluates weekday/time in UTC terms (pre-existing); the dashboard mirrors that behaviour
* "Uso registrado hoy" is simulated; bonus/allowance are correct but nothing observable changes in the state until real usage (Phase 09+)
