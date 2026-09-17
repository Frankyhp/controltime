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
* `ControlTime.Core` domain model (`Domain/`: `UsageRule`, `AllowedSchedule`, `AccessState`, `RuleResult`, `DomainResult`)

Current phase:

`Phase 03 — Rule Engine` (not started)

Do not implement functionality belonging to Phase 03 or later.

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
│   └── ControlTime.Infrastructure/
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
