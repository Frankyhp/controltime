# Phase 06 — Rules Management UI

## Goal

Allow an administrator to manage usage rules from the WPF application. All modifications require admin mode; normal mode is read-only.

## Scope

* DI composition at application startup (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`)
* Minimal hand-written MVVM infrastructure (no UI frameworks)
* Rules screen: create, edit, enable/disable, delete (inline confirmation), weekdays, allowed schedule, daily limit
* Admin gate in the same screen: first-run password setup, unlock panel, automatic lock on session expiry
* UI language: Spanish. Domain validation errors translated in App via a lookup keyed by the Core messages (`Presentation/DomainErrorTranslator`); unknown errors fall back to the original text.

Out of scope: dashboard, multi-screen navigation, bonus time, tray, password change, rate limiting, ViewModel automated tests.

## Implementation

App packages: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Debug` (10.0.12).

Composition (`App.xaml.cs`):

* `ServiceProvider` built in `OnStartup`, disposed in `OnExit`
* `ControlTimeDbContext` singleton (SQLite at `ControlTimeStorage` path); stores transient; `Pbkdf2PasswordHasher` and `AdminAuthService` singletons (admin state is process-wide); `RulesViewModel`/`MainWindow` transient, `MainWindow` receives its ViewModel by constructor injection
* `App.xaml` no longer uses `StartupUri`; startup runs `Migrate()` and shows a Spanish error + exits on failure
* Logging registered with the Debug provider

MVVM (`Mvvm/`): `ObservableObject` (INPC + `SetField`) and `RelayCommand`/`AsyncRelayCommand` (with built-in reentrancy guard and `RaiseCanExecuteChanged`). No CommunityToolkit/MVVM frameworks.

`ViewModels/RulesViewModel`:

* Admin gate: `HasPasswordAsync` decides between first-run setup panel and unlock panel; `DispatcherTimer` (1 s) detects automatic admin-session expiry (Phase 05 `AdminAuthService`) and locks editing, showing a status message; "Bloquear ahora" calls `ExitAdminMode`
* All editing commands share `CanExecute = IsAdminMode`; the whole editor panel is also disabled via `IsEnabled` binding
* CRUD via `IUsageRuleStore`; the list reloads after every change and keeps selection
* Validation: form-level parse errors in Spanish (invalid `HH:mm`, invalid minutes); domain rules are not duplicated — `AllowedSchedule.TryCreate` / `UsageRule.TryCreate` produce the verdict and `DomainErrorTranslator` localizes the error text
* Delete requires two presses: the "Eliminar" button switches to "¿Eliminar definitivamente?" + "Cancelar" (inline confirmation, no dialogs)
* Password entry comes from `PasswordBox` via two code-behind click handlers (plumbing only; `PasswordBox` has no bindable password)

Views:

* `MainWindow` is a shell hosting `RulesView` (UserControl). Navigation will arrive with Phase 08 (Dashboard)
* `RulesView`: status panels (setup/unlock/admin) at the top, rules ListBox (read-only list, `Display` one-liner per rule) and editor form (name, enabled, 7 weekday checkboxes, start/end, "Sin límite" + minutes) below it, status/error text at the bottom

The domain never receives invalid data: creation always goes through the `TryCreate` factories.

## Tests

No new automated tests by decision: the business logic exercised by this UI lives in Core (already covered by 73 green tests) and ViewModels would require retargeting the test project to `net10.0-windows` for little added value. The real startup path (DI + migration + XAML) was smoke-tested by running the app.

## Manual validation

Startup smoke test executed (app launches, database migrates, window renders). Remaining checklist for the user:

* First run without password → setup panel forced; mismatch and <8 chars rejected; correct setup enters admin mode
* App restart → normal mode; rules visible read-only; wrong password shows "Contraseña incorrecta."; correct password unlocks
* Create a rule → persists; list and editor reflect it; restart keeps it (DB: `%APPDATA%\ControlTime\controltime.db`)
* Select rule → editor loads values; "Guardar" updates; enable/disable via checkbox + save
* Invalid input → Spanish errors: start ≥ end, no day, empty name, invalid `HH:mm`, non-numeric minutes
* Delete → inline confirmation; "Cancelar" aborts; confirm removes and reloads
* Admin session expiry (~5 min) or "Bloquear ahora" → editor disabled, unlock panel reappears

## Decisions

* UI in Spanish; Core keeps English domain messages and App translates them through an exact-match lookup with fallback
* Inline deletion confirmation via buttons, no dialogs
* No ViewModel tests in this phase (see Tests)
* DI container lives in `App` only; no service locator; singletons only where state requires it (DbContext, admin service)
* Hand-written MVVM primitives instead of adding a toolkit

## Known limitations

* Single-screen app: no navigation yet (Phase 08)
* No logging beyond startup Debug provider; action auditing arrives with later phases
* Admin expiry granularity is 1 s (timer) and purely visual; commands also fail-safe because they check `IsAdminMode`
* Domain error translation is keyed by exact English strings; changing a Core message requires updating the translator (unknown messages still display, untranslated)
