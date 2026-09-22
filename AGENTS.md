# ControlTime — Agent Instructions

## Project goal

ControlTime is a Windows desktop application for controlling computer usage through configurable rules.

The application will eventually support:

* allowed days of the week
* allowed usage schedules
* maximum daily usage time
* active/idle usage tracking
* usage history
* warnings before limits are reached
* Windows session locking when usage is not allowed
* automatic startup
* background monitoring
* protected configuration
* eventual Windows Service integration

The project is Windows-only.

Keep the architecture simple and local-first.

Do not introduce web technologies, cloud services, external APIs or distributed infrastructure unless explicitly requested.

---

## Current state

Phase 01 — Project Bootstrap is complete.

Phase 02 — Domain Model is complete.

Phase 03 — Rule Engine is complete.

Phase 04 — Persistence Foundation is complete.

Phase 05 — Administrator Authentication is complete.

Phase 06 — Rules Management UI is complete.

Phase 07 — Bonus Time / Time Grants is complete.

Phase 08 — Dashboard Foundation is complete.

Phase 09 — Usage Sessions is complete.

Phase 10 — Windows Activity Detection is complete.

Phase 11 — Automatic Usage Tracking is complete.

Phase 12 — Background Monitoring is complete.

Phase 13 — Warnings and Notifications is complete.

Phase 14 — Enforcement is complete.

Phase 15 — System Tray and Startup is complete.

Phase 16 — Administration and Configuration Hardening is complete.

Created:

* `ControlTime.slnx`
* `src/ControlTime.App`
* `src/ControlTime.Core`
* `src/ControlTime.Infrastructure`
* `tests/ControlTime.Tests`
* `ControlTime.App -> ControlTime.Core` project reference
* `ControlTime.App -> ControlTime.Infrastructure` project reference
* `ControlTime.Infrastructure -> ControlTime.Core` project reference
* `ControlTime.Tests -> ControlTime.Core` project reference
* Git repository
* GitHub remote
* `AGENTS.md`
* `docs/PLAN_CONTROLTIME.md`
* `docs/product-rules.md`
* `docs/phases/phase-01.md`
* `docs/phases/phase-02.md`
* `docs/phases/phase-03.md`
* `docs/phases/phase-04.md`
* `ControlTime.Core` domain model (`Domain/`: `UsageRule`, `AllowedSchedule`, `AccessState`, `RuleResult`, `DomainResult`)
* `ControlTime.Core` `RuleEngine` (deterministic rule evaluation)
* `ControlTime.Core` store interfaces (`Abstractions/`: `IUsageRuleStore`, `IAppSettingsStore`)
* `ControlTime.Infrastructure` SQLite persistence (EF Core `ControlTimeDbContext`, `InitialCreate` migration, `SqliteUsageRuleStore`, `SqliteAppSettingsStore`)
* `docs/phases/phase-05.md`
* `ControlTime.Core` `IPasswordHasher` (`Abstractions/`)
* `ControlTime.Core` `Pbkdf2PasswordHasher` (PBKDF2-HMAC-SHA256, self-describing hash format)
* `ControlTime.Core` `IAdminAuthService` / `AdminAuthService` (password setup, verification, time-limited admin mode via `TimeProvider`)
* `docs/phases/phase-06.md`
* `ControlTime.App` DI composition (`App.xaml.cs`: `ServiceProvider`, DbContext singleton, stores, `AdminAuthService`, `Migrate()` on startup)
* `ControlTime.App` minimal MVVM (`Mvvm/`: `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`)
* `ControlTime.App` Rules screen (`Views/RulesView`, `ViewModels/RulesViewModel`, `RuleListItem`, `Presentation/DomainErrorTranslator`): rules CRUD behind the admin gate
* `docs/phases/phase-07.md`
* `ControlTime.Core` `TimeGrant` (`Domain/`) and `ITimeGrantStore` (`Abstractions/`)
* `ControlTime.Core` `RuleEngine` bonus support: optional `bonusToday` raises each rule's daily limit during evaluation
* `ControlTime.Infrastructure` time grant persistence (`TimeGrantEntity`, `AddTimeGrants` migration, `SqliteTimeGrantStore`)
* `ControlTime.App` "Regalar tiempo" inline grant panel in the Rules screen (administrator password required for every grant)
* `docs/phases/phase-08.md`
* `ControlTime.Core` `DashboardSnapshot` (`Domain/`) and `DashboardService` (`Services/`): composes rules, today's grants and `RuleEngine` into a single dashboard snapshot
* `ControlTime.App` Dashboard screen (`Views/DashboardView`, `ViewModels/DashboardViewModel`): access state, usage (simulated), configured limit, bonus today, effective allowance, remaining time, active schedule
* `ControlTime.App` minimal navigation (`MainWindow` shell + `MainViewModel`: Dashboard / Reglas tabs)
* `docs/phases/phase-09.md`
* `ControlTime.Core` `UsageSession` (`Domain/`, open sessions via nullable `EndedAt`), `UsageCalculator` and `UsageService` (`Services/`), `IUsageSessionStore` (`Abstractions/`)
* `ControlTime.Infrastructure` usage session persistence (`UsageSessionEntity`, `AddUsageSessions` migration, `SqliteUsageSessionStore`)
* `ControlTime.App` Dashboard now evaluates with real usage today from stored sessions (no longer simulated)

* `docs/phases/phase-10.md`
* `ControlTime.Core` `ActivityState` / `ActivityStatus` (`Domain/`), `ActivityEvaluator` (`Services/`), `IActivityMonitor` (`Abstractions/`)
* `ControlTime.Infrastructure` Windows activity detection (`Windows/`: `LastInputInfo`, `SessionLockProbe`, `WindowsActivityMonitor`, pure P/Invoke polling)
* `ControlTime.App` Dashboard "Actividad" card (Activo / Ocioso / Bloqueado, refreshed ~1 s)

* `docs/phases/phase-11.md`
* `ControlTime.Core` `UsageTrackingService` (`Services/`): activity-driven open/close of usage sessions with orphan adoption
* `ControlTime.Core` `UsageCalculator`/`UsageService` `asOf` support: open sessions count toward daily usage up to now
* `ControlTime.Core` `IUsageSessionStore` extended with `GetOpenSessionAsync` / `DeleteAsync` (implemented in `SqliteUsageSessionStore`)
* `ControlTime.App` `UsageTrackingHost` (`Services/`): app-level ~5 s tracking loop started at startup; Dashboard usage is now fully real

* `docs/phases/phase-12.md`
* `ControlTime.Core` `MonitorState` (`Domain/`) and `MonitoringService` (`Services/`): one composed tick (activity probe → usage tracking → dashboard snapshot)
* `ControlTime.Infrastructure` all SQLite stores now take `IDbContextFactory<ControlTimeDbContext>` and open a short-lived context per operation (thread-safe background monitoring)
* `ControlTime.App` `MonitoringHost` (`Services/`, replaces `UsageTrackingHost`): 1 s background loop (`Task.Run` + `PeriodicTimer`), publishes `MonitorState` to the UI via the captured `Dispatcher`; `DashboardViewModel` is now pure presentation (no timers, no direct activity probe)

* `docs/phases/phase-13.md`
* `ControlTime.Core` `WarningThreshold` / `UsageWarning` (`Domain/`), `INotifier` (`Abstractions/`), `WarningEvaluator` and `WarningNotifier` (`Services/`): 15/5/1-minute warning thresholds with per-day dedup, independent of `RuleEngine`
* `ControlTime.App` `BalloonTipNotifier` (`Services/`, hidden `NotifyIcon` + `ShowBalloonTip`), warning notifications wired to `MonitoringHost.StateChanged`; Dashboard now explains remaining time explicitly in the warning state

* `docs/phases/phase-14.md`
* `ControlTime.Core` `ISessionLock` (`Abstractions/`) and `EnforcementService` (`Services/`): edge-triggered session locking while `Blocked` with enabled rules, suspended during admin mode, re-lock per activity episode, retry on failure; 30 s re-lock grace period on each new `Active` episode while `Blocked` (post-unlock / app restart / idle return), immediate lock when `Blocked` starts mid-episode
* `ControlTime.Infrastructure` `WindowsSessionLock` (`Windows/`, `LockWorkStation` P/Invoke with failure logging)
* `ControlTime.App` enforcement wired to `MonitoringHost.StateChanged`; `CONTROLTIME_ENFORCEMENT=off` environment kill switch evaluated only in App composition

* `docs/phases/phase-15.md`
* `ControlTime.Core` `IAutostartRegistrar` (`Abstractions/`)
* `ControlTime.Infrastructure` `WindowsAutoStartRegistry` (`Windows/`, `HKCU\...\Run` value, no admin privileges)
* `ControlTime.App` `TrayIconService` (`Services/`, replaces `BalloonTipNotifier`): single visible `NotifyIcon` implementing `INotifier`, context menu (Abrir / estado / tiempo restante / "Iniciar con Windows" toggle / Salir), tooltip mirrors live status; closing the main window now hides it (`OnExplicitShutdown` + cancelled `OnClosing`); real exit only via tray "Salir de ControlTime"; autostart preference persisted under app settings key `AutoStartEnabled` and synced on startup

* `docs/phases/phase-16.md`
* `ControlTime.Core` `AdminAuthService` hardening: `ChangePasswordAsync` (`AdminPasswordChangeResult`), side-effect-free `VerifyPasswordAsync`, in-memory attempt lockout (5 failures / 30 seconds via injected `TimeProvider`)
* `ControlTime.App` Settings screen (`Views/SettingsView`, `ViewModels/SettingsViewModel`, third tab "Ajustes"): change-password form, admin-gated "Iniciar con Windows" toggle, read-only enforcement status
* `ControlTime.App` controlled exit: tray "Salir" now requires the administrator password via `Views/AdminExitWindow` (`VerifyPasswordAsync`, never enters admin mode)
* `ControlTime.App` tray menu no longer contains the autostart toggle; startup preference sync on startup unchanged

* `docs/phases/phase-17a.md`
* `ControlTime.Service` project (`src/ControlTime.Service`, Windows Worker Service, added to the solution): owns the 1 s monitoring loop (activity → usage tracking → dashboard snapshot → enforcement) independent of the UI; `Program.cs` composition + `MonitoringLoop` hosted service; kill switch `CONTROLTIME_ENFORCEMENT=off` now authoritative in the Service environment
* `ControlTime.Infrastructure` machine-wide database: default location `%PROGRAMDATA%\ControlTime` + `ControlTimeStorage.MigrateLegacyDatabase()` (one-time move of the legacy `%APPDATA%` database, race-tolerant; run by both App and Service)
* `ControlTime.Infrastructure` `Windows/`: `WtsApi` (`WTSGetActiveConsoleSessionId` / `WTSEnumerateSessionsW` / `WTSDisconnectSession`), `WtsConsoleSessionMonitor : IActivityMonitor` (console session Active/Connected → Active; anything else or failure → Locked), `WtsSessionLock : ISessionLock` (disconnects the active console session, never throws)
* `ControlTime.Core` `MonitoringService`: `UsageTrackingService` now optional (App composes display-only ticks without tracking)
* `ControlTime.App` no longer performs usage-session writes or enforcement; `ControlTime.Service` is the sole usage-session writer and enforcer (manual `sc create` registration until the Phase 21 installer)

* `docs/phases/phase-17b.md`
* `ControlTime.Core` `Contracts/`: `ServiceStatus` IPC DTOs + `ServiceStatusMapper`; `Abstractions/IControlTimeGateway` (pure, no pipe types)
* `ControlTime.Infrastructure` `Ipc/`: `ActivityReportSink` (freshness-cached App activity), `CompositeActivityMonitor` (fresh App report > WTS fallback), `ControlTimePipeServer` / `ControlTimePipeClient` (named pipe `ControlTime.Monitoring`, one JSON line per request/response), `GatewayAdminAuthService : IAdminAuthService` (App-side façade); `Windows/WindowsNamedPipes` (SDDL DACL + overlapped `CreateNamedPipeW`)
* `ControlTime.Service` `ServiceStatusProvider` + `PipeServerHost`; `AdminAuthService` now lives only in the Service (admin mode pauses its enforcement); new `ServiceUnavailable` admin result members
* `ControlTime.App` `ServiceClientHost` replaces `MonitoringHost` (1 s poll: report local activity + fetch status); Dashboard banner and tray tooltip on unavailable Service; Settings enforcement label reads from the Service; SQLite busy timeout 5 s (App and Service are separate writers)
* Infrastructure and Tests target `net10.0-windows` (pipe security layer is Windows-only)

* `docs/phases/phase-18.md`
* `ControlTime.Infrastructure` session-attributed activity reports: `ActivityReportSink` is a thread-safe per-session cache (`Report(sessionId, ...)` / `TryGetFreshForSession`); `ControlTimePipeServer` derives the client's Windows session server-side (`GetNamedPipeClientProcessId` P/Invoke in `WindowsNamedPipes` → `Process.SessionId`, injectable resolver) and rejects `ReportActivity` from unresolvable connections
* `ControlTime.Infrastructure` `Windows/WindowsConsoleSessionId` (`WTSGetActiveConsoleSessionId` wrapper); `CompositeActivityMonitor` now consumes only fresh reports of the active console session (other sessions' reports ignored; no console session → WTS fallback) and exposes `IsUsingReportedActivity` (used by `ServiceStatusProvider`)
* `ControlTime.App` single instance per Windows session (named mutex `Local\ControlTime.App`; second instance exits immediately); the App is formally the user-session agent (no separate agent process introduced)

* `docs/phases/phase-19.md`
* `ControlTime.Core` `UsageHistoryEntry` / `UsageHistory` (`Domain/`) and `UsageHistoryService` (`Services/`): single-range load of sessions and grants, per-day usage via `UsageCalculator` (open sessions count up to now), per-day bonus attribution, `CurrentWeekUsage` summed from Monday
* `ControlTime.Core` `ITimeGrantStore` extended with `GetGrantsInRangeAsync` (implemented in `SqliteTimeGrantStore`)
* `ControlTime.App` Historial tab (`Views/HistoryView`, `ViewModels/UsageHistoryViewModel`): week-total card, 14-day list (Día / Uso / Bonus, "Hoy"/"Ayer" labels), sessions of the selected day, manual refresh; App registers `IUsageSessionStore` read-only (Service remains sole session writer)

Phase 17 was split into `Phase 17a — Windows Service Foundation` and `Phase 17b — Service IPC and App Client`.

Phase 17a — Windows Service Foundation is complete.

Phase 17b — Service IPC and App Client is complete.

Phase 18 — User Session Integration is complete.

Phase 19 — Usage History UI is complete.

Current phase:

`Phase 20 — Reliability and Recovery` (not started)

Do not implement functionality belonging to Phase 20 or later without planning Phase 20 first.

---

## Development model

Development is performed strictly by phases defined in:

`docs/PLAN_CONTROLTIME.md`

Each phase follows:

1. Plan
2. Build
3. Test
4. Review

A phase is complete only when:

* its implementation scope is finished
* automated tests pass
* the solution builds successfully
* its manual validation checklist passes where applicable
* no functionality from future phases has been implemented

Never implement functionality from a future phase unless explicitly requested.

Prefer small, coherent changes over large refactors.

Do not refactor unrelated code while implementing a phase.

---

## Current repository structure

```text
ControlTime/
├── AGENTS.md
├── ControlTime.slnx
│
├── docs/
│   ├── PLAN_CONTROLTIME.md
│   └── phases/
│
├── src/
│   ├── ControlTime.App/
│   ├── ControlTime.Core/
│   ├── ControlTime.Infrastructure/
│   └── ControlTime.Service/
│
└── tests/
    └── ControlTime.Tests/
```

---

## Target repository structure

```text
ControlTime/
├── AGENTS.md
├── README.md
├── ControlTime.sln
│
├── docs/
│   ├── PLAN_CONTROLTIME.md
│   └── phases/
│       └── phase-NN.md
│
├── src/
│   ├── ControlTime.App/
│   ├── ControlTime.Core/
│   └── ControlTime.Infrastructure/
│
└── tests/
    └── ControlTime.Tests/
```

Additional documentation files such as:

* `docs/architecture.md`
* `docs/product-rules.md`
* `docs/manual-testing.md`

should only be created when the project has enough stable information to justify them.

Do not create empty documentation files prematurely.

Additional projects such as a Windows Service must only be introduced in the phase where they are required.

---

## Technology stack

Use:

* C#
* .NET 10
* WPF
* SQLite
* Entity Framework Core
* xUnit
* Microsoft.Extensions.DependencyInjection
* Microsoft.Extensions.Logging

Use Windows APIs only through Infrastructure or dedicated Windows-specific projects introduced later.

Do not introduce additional frameworks or libraries without a concrete need.

---

# Architecture

## Dependency direction

The intended dependency direction is:

```text
ControlTime.App
      │
      ├──────────────► ControlTime.Core
      │
      └──────────────► ControlTime.Infrastructure
                              │
                              ▼
                       ControlTime.Core

ControlTime.Tests ───────────► ControlTime.Core
```

`ControlTime.Core` must never reference:

* `ControlTime.App`
* `ControlTime.Infrastructure`
* WPF
* Entity Framework Core
* SQLite
* Win32 APIs

---

# ControlTime.Core

Contains business and domain logic.

Examples:

* domain models
* rule evaluation
* usage calculations
* schedule evaluation
* application interfaces
* domain services
* result/value types

Business logic should live here whenever possible.

Core should consist primarily of deterministic, testable C#.

---

# ControlTime.Infrastructure

Contains implementations that interact with external systems.

Examples:

* SQLite
* Entity Framework Core
* repositories
* Windows APIs
* idle detection
* Windows session state
* session locking
* startup registration
* filesystem
* application settings persistence

Infrastructure may depend on Core.

Core must never depend on Infrastructure.

---

# ControlTime.App

Contains the WPF desktop application.

Responsibilities include:

* Views
* ViewModels
* navigation
* UI state
* dependency injection composition
* presenting information
* user interaction

Use MVVM.

Avoid business logic in:

* Views
* code-behind
* ViewModels

ViewModels may coordinate Core services but should not duplicate domain rules.

---

# ControlTime.Tests

Contains automated tests.

Prioritize unit tests for:

* RuleEngine
* schedules
* daily limits
* usage calculations
* session calculations
* time boundaries
* rule conflicts

Tests for Core should not require:

* WPF
* a running database
* Windows UI
* Windows APIs

Integration tests may be added later when required.

---

# Domain principles

## Usage rules

A usage rule may eventually define:

* enabled/disabled
* allowed days
* allowed start time
* allowed end time
* maximum daily usage

Rule evaluation belongs in Core.

The RuleEngine must not know about:

* databases
* WPF
* Windows APIs
* notifications
* locking the computer

Conceptually:

```csharp
RuleResult Evaluate(
    DateTimeOffset currentTime,
    TimeSpan usageToday,
    IReadOnlyCollection<UsageRule> rules);
```

Inputs and API details may evolve during the relevant phase.

---

## Time handling

Time handling is critical.

Do not call:

```csharp
DateTime.Now
```

inside domain logic.

Pass time explicitly or use a clock abstraction where appropriate.

Prefer:

```csharp
DateTimeOffset
```

for persisted timestamps.

Prefer:

```csharp
DateOnly
TimeOnly
TimeSpan
```

when they better express domain concepts.

Tests should eventually cover boundaries such as:

* schedule start
* schedule end
* daily limit reached exactly
* one second/minute before a limit
* midnight
* weekday transitions
* daylight-saving changes where relevant

Do not implement these tests before the corresponding phase.

---

## Usage tracking

Do not use only an accumulated daily counter.

Usage should eventually be represented by sessions.

Conceptually:

```text
UsageSession
- Id
- StartedAt
- EndedAt
```

Daily usage should be calculated from stored sessions.

The future implementation must account for sessions crossing midnight.

Do not implement usage tracking before its planned phase.

---

## Activity states

The domain may eventually distinguish states such as:

```text
Active
Idle
Locked
LoggedOut
```

Do not implement Windows activity detection until its corresponding phase.

---

# Persistence

SQLite is the planned local database.

Entity Framework Core is the planned persistence technology.

Persistence concerns should not leak unnecessarily into Core.

Keep schema changes explicit.

Use EF Core migrations once persistence is introduced.

Never automatically delete or recreate the production database because the model changed.

Do not add SQLite or Entity Framework before the persistence phase.

---

# Windows integration

Windows-specific functionality belongs in Infrastructure or dedicated Windows-specific projects introduced later.

Examples:

* idle detection
* session lock state
* `LockWorkStation`
* startup registration
* system tray
* notifications
* Windows Service integration
* IPC

Do not introduce Windows Service architecture before its planned phase.

The initial application runs as a normal desktop application.

---

# Enforcement

There is an important distinction between:

```text
Decision
```

and:

```text
Enforcement
```

Core decides whether usage is allowed.

Infrastructure performs actions such as locking the Windows session.

Conceptually:

```text
RuleEngine
    ↓
RuleResult
    ↓
Enforcement component
    ↓
Windows API
```

Do not mix these responsibilities.

---

# Background processing

Background monitoring must not block the WPF UI thread.

Use asynchronous or background execution where appropriate.

Do not introduce complex job systems or message brokers.

For periodic monitoring, use the simplest reliable .NET mechanism appropriate to the architecture at that time.

---

# UI principles

Initial target screens are:

* Dashboard
* Rules
* Usage history
* Settings

Keep UI functional before making it visually sophisticated.

The Dashboard should eventually display:

* current access state
* usage today
* daily limit
* remaining time
* active schedule
* warnings

Do not introduce a large UI framework unless clearly necessary.

---

# Error handling

Do not silently swallow exceptions.

Expected domain failures should use explicit results where appropriate.

Unexpected infrastructure failures should:

* be logged
* fail safely
* avoid corrupting usage data

A failure in usage monitoring must not produce invalid historical data silently.

---

# Logging

Use `Microsoft.Extensions.Logging` when logging is introduced.

Logs should eventually help diagnose:

* startup failures
* database failures
* monitoring failures
* Windows API failures
* enforcement actions

Do not log sensitive data unnecessarily.

Do not use `Console.WriteLine` as the primary logging mechanism in application code.

Do not add logging infrastructure before it is needed.

---

# Testing rules

New Core business functionality should normally include automated tests.

Before a phase is considered complete, run:

```bash
dotnet test
```

and:

```bash
dotnet build
```

Existing tests must remain green.

Do not modify a correct existing test merely to make a broken implementation pass.

When fixing a bug in business logic:

1. reproduce it with a test where practical
2. fix the implementation
3. verify the new test passes
4. run the complete test suite

---

# Coding conventions

Use modern C#.

Enable nullable reference types.

Prefer:

* clear names
* small classes
* constructor dependency injection
* immutable values where useful
* records for result/value types when appropriate
* async/await for I/O
* explicit domain types

Avoid:

* global mutable state
* service locator patterns
* unnecessary inheritance
* giant service classes
* hidden side effects
* premature abstractions
* speculative generic frameworks

---

# Architecture constraints

Do not introduce without explicit need:

* CQRS
* MediatR
* event buses
* generic repository frameworks
* microservices
* REST APIs
* web servers
* Electron
* React
* Docker
* cloud databases
* external authentication services

The simplest solution that satisfies the known requirements is preferred.

---

# Security model

ControlTime is initially designed as a local Windows utility.

Do not assume that the application can prevent a Windows administrator from bypassing its restrictions.

Later phases may improve resistance against casual bypass through:

* protected settings
* background processes
* Windows Service
* separation of UI and enforcement

Do not implement security mechanisms before their corresponding phase.

Never describe a mechanism as impossible to bypass unless that is actually guaranteed.

---

# Phase discipline

Before making changes:

1. Read `AGENTS.md`.
2. Read `docs/PLAN_CONTROLTIME.md`.
3. Identify the current phase.
4. Inspect relevant existing code.
5. Inspect relevant tests if they exist.
6. Determine the smallest coherent implementation.

During implementation:

1. Stay inside the current phase.
2. Keep dependency direction correct.
3. Add relevant tests when appropriate.
4. Avoid unrelated refactors.

After implementation:

1. Run tests.
2. Run build.
3. Perform the phase validation checklist where possible.
4. Review `git diff`.
5. Summarize changes.
6. Identify remaining issues.

Do not automatically begin the next phase.

---

# Phase documentation

Each completed phase should have:

```text
docs/phases/phase-NN.md
```

Keep phase documents concise.

Recommended structure:

```text
# Phase NN — Name

## Goal

## Scope

## Implementation

## Tests

## Manual validation

## Decisions

## Known limitations
```

Do not duplicate large amounts of information already contained in:

* `AGENTS.md`
* `docs/PLAN_CONTROLTIME.md`

If shared documentation such as `architecture.md` or `product-rules.md` is introduced later, avoid duplicating its contents inside phase files.

Phase documents should describe what changed, not reproduce the whole project specification.

---

# Documentation ownership

Currently:

`docs/PLAN_CONTROLTIME.md`

contains the development roadmap.

`docs/phases/phase-NN.md`

contains phase-specific implementation records.

Future shared documentation files should only be created when justified.

Examples:

`docs/architecture.md`

Use for stable architectural decisions that affect multiple phases.

`docs/product-rules.md`

Use for stable product behaviour and business rules that affect multiple phases.

`docs/manual-testing.md`

Use for reusable manual validation procedures that would otherwise be repeated across many phase files.

When information becomes a permanent project rule, move it into the appropriate shared document instead of repeating it in future phase files.

---

# Scope rule

When uncertain, prefer the smallest implementation that:

1. satisfies the current phase
2. respects the architecture
3. remains compatible with known future requirements

Do not implement speculative future functionality.
