# ControlTime — Development Plan

## Objective

Build a Windows desktop application that controls computer usage according to configurable rules.

The application will allow an administrator to configure:

* permitted days
* permitted schedules
* maximum daily usage time
* bonus time for the current day
* application settings

Administrative actions require password authentication.

The application will:

* track real computer usage
* distinguish active and idle periods
* calculate remaining available time
* warn before limits are reached
* lock the Windows session when usage is not allowed
* remain available from the Windows system tray
* start automatically with Windows
* eventually separate UI and enforcement through a Windows Service

Development is incremental.

Each phase must remain small enough to implement, test and review independently.

---

# Development workflow

Every phase follows:

```text
Plan
  ↓
Build
  ↓
Automated tests
  ↓
Manual validation
  ↓
Review
```

Do not start the next phase until the current phase is considered complete.

Each completed phase should be documented in:

```text
docs/phases/phase-NN.md
```

---

# Target architecture

Initial architecture:

```text
┌─────────────────────┐
│   ControlTime.App   │
│        WPF          │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  ControlTime.Core   │
│                     │
│ Rules               │
│ Usage calculations  │
│ Domain models       │
└─────────▲───────────┘
          │
          │ implements
          │
┌─────────┴───────────┐
│ ControlTime.        │
│ Infrastructure      │
│                     │
│ SQLite              │
│ Windows APIs        │
└─────────────────────┘
```

Later architecture may evolve to:

```text
┌──────────────────┐
│ ControlTime.App  │
│       WPF        │
└────────┬─────────┘
         │
         │ IPC
         ▼
┌──────────────────┐
│ ControlTime      │
│ User Agent       │
└────────┬─────────┘
         │
         │ IPC
         ▼
┌──────────────────┐
│ ControlTime      │
│ Windows Service  │
└──────────────────┘
```

This architecture must not be introduced prematurely.

---

# Phase 01 — Project Bootstrap

## Goal

Create the basic .NET solution and dependency structure.

## Scope

Target structure:

```text
ControlTime.sln

src/
  ControlTime.App/
  ControlTime.Core/
  ControlTime.Infrastructure/

tests/
  ControlTime.Tests/

docs/
  PLAN_CONTROLTIME.md
  product-rules.md
  phases/
```

Configure:

* .NET 10
* WPF application
* Core class library
* Infrastructure class library
* xUnit tests
* project references
* nullable reference types
* implicit usings where appropriate

Remove unnecessary template files.

## Dependency direction

```text
ControlTime.App
      │
      ├──────────► ControlTime.Core
      │
      └──────────► ControlTime.Infrastructure
                          │
                          ▼
                   ControlTime.Core

ControlTime.Tests ───────► ControlTime.Core
```

## Out of scope

Do not implement:

* rules
* database
* authentication
* bonus time
* usage tracking
* Windows APIs
* application UI functionality
* background processes

## Completion criteria

```text
dotnet build
```

passes.

```text
dotnet test
```

passes.

All dependency directions follow `AGENTS.md`.

---

# Phase 02 — Domain Model

## Goal

Define the core domain concepts without introducing persistence, WPF or Windows dependencies.

## Main concepts

Define models and value objects required for:

* usage rules
* allowed weekdays
* allowed schedules
* daily usage limits
* rule evaluation results

Potential concepts:

```text
UsageRule
RuleResult
AccessState
```

Exact API design should be decided during the phase plan.

## Tests

Cover:

* valid rule construction
* disabled rules
* schedule boundaries
* daily limit boundaries
* invalid domain values where applicable

## Out of scope

No:

* database
* WPF logic
* Windows APIs
* enforcement
* admin authentication

---

# Phase 03 — Rule Engine

## Goal

Implement deterministic rule evaluation.

Given:

* current date/time
* usage accumulated today
* configured rules
* granted bonus time

determine whether usage is currently allowed.

Initially granted bonus time may be zero until its dedicated phase is implemented.

## Potential results

```text
Allowed
Warning
Blocked
```

Result information may include:

* reason
* remaining available time
* next allowed time

## Important cases

Cover:

* allowed day
* forbidden day
* before schedule
* inside schedule
* after schedule
* daily limit not reached
* daily limit reached exactly
* multiple rules
* midnight boundaries

## Tests

This phase should have extensive unit tests.

No database or Windows dependency is allowed.

---

# Phase 04 — Persistence Foundation

## Goal

Introduce local persistence.

Use:

* SQLite
* Entity Framework Core

Persist initially:

* usage rules
* application settings

Introduce migrations.

## Requirements

Database creation must be predictable.

Schema changes must use migrations.

Do not automatically destroy an existing database when the model changes.

## Out of scope

Usage sessions and bonus time persistence will be introduced later.

---

# Phase 05 — Administrator Authentication

## Goal

Introduce the application's administrative access model.

## Behaviour

ControlTime has:

```text
Normal mode
Admin mode
```

Normal mode allows viewing non-sensitive information.

Administrator authentication is required for actions that modify restrictions or sensitive settings.

## Implement

* administrator password setup
* administrator password verification
* secure password hashing
* admin session/state
* entering admin mode
* leaving admin mode

The administrator password must never be persisted in plain text.

## Tests

Cover:

* valid password
* invalid password
* password verification
* admin state transitions
* secure persistence representation

## Out of scope

Do not implement bonus time yet.

---

# Phase 06 — Rules Management UI

## Goal

Allow administrators to manage usage rules through the WPF application.

## Implement

Initial MVVM structure.

Create the Rules screen.

Support:

* create rule
* edit rule
* enable/disable rule
* delete rule
* choose weekdays
* configure allowed start/end times
* configure daily usage limit

## Security

All rule modifications require administrator access.

Normal mode may display rule information where appropriate but cannot modify it.

## Validation

Prevent invalid configurations such as:

* invalid time ranges
* negative limits
* malformed schedules

---

# Phase 07 — Bonus Time / Time Grants

## Goal

Allow an administrator to grant additional usage time for the current day.

## Domain concept

Introduce:

```text
TimeGrant
- Id
- GrantedAt
- Minutes
```

Time grants are stored independently from usage rules.

## Behaviour

Example:

```text
Daily limit:       120 min
Bonus time:         30 min
Effective limit:   150 min
```

Effective daily allowance:

```text
configured daily limit
+ bonus time granted today
```

Remaining time:

```text
daily limit
+ grants today
- usage today
```

## Restrictions

A bonus:

* applies only to the current day
* may be granted multiple times
* does not change the configured rule
* does not bypass forbidden weekdays
* does not bypass schedule restrictions

## UI flow

```text
Press "Regalar tiempo"
        ↓
Enter administrator password
        ↓
Enter number of minutes
        ↓
Confirm
        ↓
Add time to today's allowance
```

Authentication must be requested for each grant operation.

## Persistence

Persist all grants.

Do not collapse them into a single daily counter.

## Tests

Cover:

* zero grants
* one grant
* multiple grants
* grants from previous days ignored
* effective allowance calculation
* schedule restrictions still apply
* weekday restrictions still apply

---

# Phase 08 — Dashboard Foundation

## Goal

Create the primary application dashboard.

## Display

* current access state
* configured daily limit
* bonus time today
* effective daily allowance
* active schedule
* current rule status

At this stage actual usage may still be zero or simulated because real activity tracking has not yet been introduced.

## Purpose

Validate:

```text
UI
 ↓
Core
 ↓
Persistence
```

before introducing Windows monitoring.

---

# Phase 09 — Usage Sessions

## Goal

Introduce the usage-history domain.

Usage must be represented as sessions rather than only an accumulated counter.

## Concept

```text
UsageSession
- Id
- StartedAt
- EndedAt
```

## Implement

* usage session persistence
* daily usage calculation
* querying usage by date
* handling sessions crossing midnight

## Tests

Cover:

* no usage
* one session
* multiple sessions
* midnight crossing
* sessions spanning dates
* open/incomplete sessions if supported

---

# Phase 10 — Windows Activity Detection

## Goal

Detect whether the logged-in Windows user is actively using the computer.

## Potential states

```text
Active
Idle
Locked
```

## Implement

Windows-specific functionality in Infrastructure.

Expose it through a Core abstraction such as:

```text
IActivityMonitor
```

## Requirements

Idle threshold must be configurable internally or later via settings.

## Out of scope

Do not enforce restrictions yet.

This phase only observes Windows state.

---

# Phase 11 — Automatic Usage Tracking

## Goal

Connect activity detection with usage sessions.

## Behaviour

```text
Active
   ↓
start / continue usage session

Idle
Locked
   ↓
stop / pause usage session
```

## Handle

* active → idle
* idle → active
* lock
* unlock
* application restart
* abnormal shutdown
* midnight
* duplicate sessions

## Dashboard

Now display real:

* usage today
* bonus time
* remaining time

---

# Phase 12 — Background Monitoring

## Goal

Make usage monitoring independent from individual UI screens.

Introduce an application-level background monitoring component.

## Responsibilities

* periodically evaluate activity
* maintain usage sessions
* evaluate rules
* calculate current application state
* update the UI

The WPF UI thread must remain responsive.

## Out of scope

No Windows Service yet.

The application still runs inside the logged-in user's session.

---

# Phase 13 — Warnings and Notifications

## Goal

Warn the user before available time is exhausted.

## Potential warning thresholds

```text
15 minutes remaining
5 minutes remaining
1 minute remaining
```

Thresholds should not be unnecessarily embedded into RuleEngine logic.

## Requirements

* Windows-visible notifications
* avoid duplicate notification spam
* warning state on Dashboard
* clear explanation of why access is close to being blocked

---

# Phase 14 — Enforcement

## Goal

Apply restrictions when Core determines usage is not allowed.

## Initial enforcement

Use the Windows session lock mechanism.

Conceptually:

```text
RuleEngine
    ↓
Blocked
    ↓
Windows enforcement
    ↓
LockWorkStation
```

## Blocking causes

May include:

* forbidden weekday
* outside permitted schedule
* daily allowance exhausted

## Architecture

Core decides whether access is allowed.

Infrastructure performs the Windows action.

## Requirements

Enforcement failures must be logged.

Avoid destructive system operations.

---

# Phase 15 — System Tray and Startup

## Goal

Make ControlTime behave like a persistent Windows utility.

## System tray

Implement a tray icon.

Closing the main window should normally:

```text
Press X
   ↓
Hide window
   ↓
Application remains running in tray
```

Tray actions may include:

```text
Open ControlTime
Current status
Remaining time
Exit
```

## Startup

Configure optional or required automatic startup after Windows user login according to product rules.

## Behaviour

Clearly distinguish:

```text
Close window
```

from:

```text
Exit ControlTime
```

At this stage exiting the application still stops monitoring.

That limitation will be addressed later through the Windows Service architecture.

---

# Phase 16 — Administration and Configuration Hardening

## Goal

Improve protection against casual bypass.

Review:

* admin session expiration
* sensitive actions
* password change
* controlled application exit
* disabling monitoring
* settings modification

Administrative authentication may be requested again for especially sensitive operations.

## Security scope

This protects against normal/casual users.

Do not claim protection against a Windows local administrator.

---

# Phase 17a — Windows Service Foundation

## Goal

Introduce a Windows Service that owns usage tracking and enforcement, independent of the graphical application.

```text
ControlTime.Service (SYSTEM)
    │
    ├── MonitoringService tick (Core, reused)
    ├── Usage tracking (sole writer of usage sessions)
    └── EnforcementService → WTSDisconnectSession
```

## Why split Phase 17

The original Phase 17 (Service + IPC + App as client) is too large for one Plan/Build/Test cycle.

* 17a: Service project, shared machine-wide database, Service-side activity/lock via WTS, Service owns tracking and enforcement; App becomes read-only for usage state.
* 17b: IPC (named pipe), App becomes client of the Service (status, admin mode, activity reports).

## Technical constraints (Windows)

A service runs as SYSTEM in session 0:

* `LockWorkStation` and `GetLastInputInfo` do not affect or observe the user session.
* `WTSDisconnectSession` on the active console session works from SYSTEM and is user-equivalent to a lock (access screen, processes alive, re-login required).
* Idle detection for another session is not possible from the Service; full precision arrives in 17b (App reports activity) and is formalized in Phase 18.

## Scope

* New project `src/ControlTime.Service` (Worker Service, `UseWindowsService`, console-runnable for development).
* Database moved to `C:\ProgramData\ControlTime` (shared by App and Service) with automatic move of the legacy `%APPDATA%` database on startup.
* Service-side activity source: active console session connected → `Active`; no active/connected session → `Locked`. No idle yet.
* Service-side lock: `WTSDisconnectSession` implementation of `ISessionLock`.
* Service owns `UsageTrackingService` (sole writer) and `EnforcementService` (30 s re-lock grace reused: re-login after a Service lock yields a fresh `Active` episode).
* App stops local usage tracking and enforcement (no double writes); UI keeps local activity probe and read-only Dashboard polling for display.
* Manual service registration (`sc create`, documented; installer arrives in Phase 21).

## Out of scope

* IPC and App as Service client (17b).
* Idle detection from the Service (17b via App reports).
* User-session agent (Phase 18).
* Installer (Phase 21).

## Known limitations accepted in 17a

* While the UI is closed, idle time is counted as usage (no idle source on the Service side).
* UI admin mode does not pause Service enforcement (arrives in 17b); legitimate administration while `Blocked` uses granted time.

---

# Phase 17b — Service IPC and App Client

## Goal

Make the App a client of the Service so status, administration and activity flow through the protected Service process.

```text
ControlTime.App ──named pipe──► ControlTime.Service
     status poll ←─┘            ├ AdminAuthService (sole authority)
     admin commands ──►         ├ Enforcement (paused during admin mode)
     activity reports ──►       └ Activity source: fresh App report > WTS fallback
```

## Scope

* Core: plain DTO records + `IMonitoringGateway` abstraction (no Windows/pipe types in Core).
* Infrastructure: named-pipe server (Service side) and client (App side); single JSON line per request/response; pipe accessible to the interactive user, but admin commands require the administrator password verified by the Service.
* Messages: `GetStatus` (dashboard + activity + admin mode + enforcement state), `ReportActivity` (App heartbeat ~1 s), `EnterAdminMode(password)`, `ExitAdminMode`.
* Service activity preference: App report fresher than ~5 s wins; otherwise WTS fallback (`Active` while console connected / `Locked`).
* `AdminAuthService` lives only in the Service; App admin flows (Rules gate, grants, exit, change password, attempt lockout) go through the gateway.
* App: local `MonitoringHost` loop replaced by gateway polling (~1 s, off the UI thread); Dashboard, tray tooltip and warnings fed from Service status.
* Settings screen: Service state visible (running / not available).

## Out of scope

* User-session agent (Phase 18).
* Installer / automatic service registration (Phase 21).

## Important

Do not duplicate business rules.

RuleEngine remains reusable Core functionality.

Do not expose unnecessary remote/network interfaces (local named pipe only).

---

# Phase 18 — User Session Integration

## Goal

Handle Windows features that require interaction with the logged-in user's desktop session.

If necessary, introduce a small user-session agent.

Potential final architecture:

```text
ControlTime.App
      │
      ▼
User Agent
      │
      ▼
Windows Service
```

Only introduce an additional process if Windows session isolation requires it.

## Test

* login
* logout
* lock
* unlock
* restart
* service restart
* UI closed
* UI reopened

Multiple Windows user sessions should only be supported if explicitly required.

---

# Phase 19 — Usage History UI

## Goal

Expose stored usage information.

## Display

At minimum:

* today
* previous days
* weekly overview
* bonus time granted
* basic session details where useful

Example:

```text
Today        1h 42m   +30m bonus
Yesterday    1h 58m
Monday       2h 00m
Sunday       1h 15m
```

Keep statistics intentionally simple.

No advanced analytics are required.

---

# Redesign Phase 01 — Visual Design System

Presentation-only redesign inserted after Phase 19. Does not renumber any phase and
does not change behaviour, domain logic, persistence, services or Windows integration.

## Goal

Make the WPF UI look like a modern Windows desktop utility using plain WPF resource
dictionaries and control templates only (no third-party UI libraries).

See `docs/phases/redesign-phase-01.md`.

---

# Phase 20 — Reliability and Recovery

## Goal

Strengthen the application for long-term normal use.

## Review

* crashes
* incomplete sessions
* database failures
* corrupted configuration
* application restart
* Windows restart
* Windows shutdown
* service restart
* system clock changes
* daylight-saving transitions
* midnight transitions

## Logging

Ensure diagnostic logging exists for:

* startup
* persistence failures
* monitoring failures
* Windows API failures
* enforcement events
* Service/IPC failures

---

# Phase 21 — Installer and Distribution

## Goal

Create a repeatable Windows installation process.

## Installer responsibilities

Depending on final architecture:

* install desktop application
* install Windows Service
* configure Service
* configure startup
* create required directories
* support upgrades
* support uninstall

Evaluate packaging technology during this phase.

Do not select installer technology prematurely.

---

# Phase 22 — Release Candidate and Version 1.0

## Goal

Validate and publish the first stable release.

## Full regression

Review:

* admin authentication
* rules
* weekdays
* schedules
* daily limits
* bonus time
* activity tracking
* usage history
* idle handling
* notifications
* enforcement
* system tray
* startup
* Windows Service
* installation
* upgrades
* uninstall

Do not introduce major new functionality.

## Release tasks

* final build
* automated tests
* manual regression
* clean install test
* upgrade test
* uninstall test
* repository cleanup
* version metadata
* release documentation

Tag the release:

```text
v1.0.0
```

---

# Phase 23 — Floating Remaining-Time Overlay

## Goal

Show a movable always-on-top floating timer with today's remaining usage
time, toggled from the system tray menu.

## Scope

* Presentation only in `ControlTime.App` (v1.1).
* No changes to Core, Service, Infrastructure or IPC.

## Features

* Borderless transparent always-on-top overlay window showing remaining
  time as `H:mm` with semi-transparent text.
* Movable by dragging.
* Show/hide toggle ("Mostrar tiempo restante en pantalla") in the tray
  context menu.
* Visibility preference and window position persisted in app settings.
* Overlay automatically hides while the Windows Service is unavailable
  and reappears when the Service returns, if the preference is visible.

## Out of scope

* Click-through overlay.
* Overlay on the Windows lock screen (secure desktop makes this
  impossible for any normal window).
* Configurable font size, color or opacity.
* Seconds resolution.

---

# Product milestones

## Milestone A — Rules prototype

```text
Phase 01 — Bootstrap
Phase 02 — Domain Model
Phase 03 — Rule Engine
Phase 04 — Persistence
Phase 05 — Administrator Authentication
Phase 06 — Rules UI
Phase 07 — Bonus Time
Phase 08 — Dashboard
```

Result:

A functional desktop application where an administrator can configure rules and grant bonus time.

The application can calculate whether access should be allowed but does not yet measure real Windows usage.

---

## Milestone B — Functional ControlTime

```text
Phase 09 — Usage Sessions
Phase 10 — Activity Detection
Phase 11 — Automatic Tracking
Phase 12 — Background Monitoring
Phase 13 — Warnings
Phase 14 — Enforcement
Phase 15 — Tray and Startup
```

Result:

ControlTime measures real computer usage, warns the user and can lock the Windows session.

This is the first practically usable version.

---

## Milestone C — Hardened ControlTime

```text
Phase 16 — Administration Hardening
Phase 17a — Windows Service Foundation
Phase 17b — Service IPC and App Client
Phase 18 — User Session Integration
Phase 19 — Usage History UI
Redesign Phase 01 — Visual Design System (presentation-only, inserted after Phase 19)
Phase 20 — Reliability
```

Result:

Closing the UI no longer disables enforcement and the application is robust enough for normal long-term use.

---

## Milestone D — Distribution

```text
Phase 21 — Installer
Phase 22 — Release Candidate / 1.0
```

Result:

Installable and tested ControlTime 1.0.

---

# Guiding principles

Throughout the project:

```text
Core first
Business rules tested
Small phases
Windows integration isolated
Persistence isolated
UI separated from domain logic
Admin actions authenticated
Bonus time auditable
No premature complexity
```

When real Windows limitations require architectural changes:

1. document the limitation
2. identify the smallest viable change
3. update shared documentation if necessary
4. update this roadmap where appropriate
5. avoid rewriting working components without a concrete reason
