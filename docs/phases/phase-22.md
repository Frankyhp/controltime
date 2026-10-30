# Phase 22 — Release Candidate and Version 1.1

Status: complete — manual regression passed on the target machine (Phase 22 scope); release `v1.1.0` tagged and published on GitHub.

## Goal

Validate the whole product end-to-end and publish the first stable release: build `v1.1.0` from a clean, tagged commit. No new functionality — only fixes arising from regression.

Note: this phase was originally planned around `v1.0.0`. Phase 23 (floating overlay) was implemented by explicit user decision before the `v1.0.0` tag was ever created, and `<Version>` is already `1.1.0`; since no 1.0 artifact was ever published, the first release is `v1.1.0` and the `v1.0.0` tag is dropped entirely.

## Gate principle

RC = every product area exercised at least once on a real machine (dev machine as target), automated suite green, install/upgrade/uninstall validated. Bugs found → reproduce with a test where practical, fix forward on the release version (no RC suffixes; tag only at the end).

## Scope

1. Regression script: `docs/manual-testing.md` — reusable manual validation procedures (justified now per documentation-ownership rule), sections mapped to the roadmap full-regression list: admin authentication, rules/days/schedules/daily limits, bonus time, activity/idle detection, usage tracking, usage history, notifications, enforcement (incl. 30 s grace, admin-mode pause, kill switch), tray/startup, Windows Service (sole writer, IPC reports, unavailable banner, failure recovery), installation/upgrade/uninstall (Phase 21 checklist).
2. Automated: `dotnet test` (257 expected green) + `dotnet build` clean.
3. Manual regression execution on the development machine using `ControlTime-Setup-1.1.0.exe` (installer replaces the manual `sc create` registration from Phase 17a). `CONTROLTIME_ENFORCEMENT=off` except during the enforcement section.
4. Version metadata audit: `Directory.Build.props` = 1.1.0; verify `FileVersion`/`ProductVersion` on both published executables and `VersionInfoVersion`/`AppVersion` on the Setup executable; stable `AppId`.
5. Repository cleanup: `README.md` (required by the target structure, currently missing); no build artifacts tracked (`publish/`, `dist/` ignored); final review of `AGENTS.md` and `docs/`.
6. Release tasks (only with explicit user confirmation): phase commit → annotated tag `v1.1.0` → push → GitHub Release with `ControlTime-Setup-1.1.0.exe` attached and release notes (capabilities + known limitations).
7. Closing docs: this file completed (regression results, issues found/fixed); `AGENTS.md` updated.

Out of scope: code signing, auto-update, ARM64, additional anti-tampering, real multi-user support, historical changelog (no previous versions exist).

## Known limitations to publish

- Unsigned installer (SmartScreen warning on fresh machines).
- No auto-update: upgrading means re-running the new installer.
- Not a protection against a Windows administrator (documented security model).
- x64 only.

## Completion criteria

1. Test suite green, build clean.
2. `docs/manual-testing.md` checklist executed end-to-end (or issues found and resolved).
3. Phase 21 install/upgrade/uninstall checklist passed on the target machine.
4. README and version metadata verified.
5. Tag `v1.1.0` created and release published (explicit confirmation before any commit/tag/push).

## Implementation

- `docs/manual-testing.md`: full regression script (14 sections), reusable by future phases.
- `README.md` updated to the current architecture (App + Service, ProgramData database/logs, installer, kill switch, security model) — the previous README described the pre-Service single-app design; style kept Spanish.
- Version metadata: `Directory.Build.props` extended with `Product`/`AssemblyProduct`/`Company`/`AssemblyCompany`/`Copyright`/`AssemblyCopyright` (plain `Product` was not propagating to the PE `ProductName`); `installer/ControlTime.iss` gained `AppPublisher`/`VersionInfoCompany`/`VersionInfoCopyright`.
- Repository cleanup: `git ls-files` confirms zero tracked build artifacts (`publish/`, `dist/` ignored); no code changes → no functional risk.
- Not done here (human-required): executing the regression checklist and the install/upgrade/uninstall test on a target machine; commit, `v1.1.0` tag and GitHub Release (explicit confirmation required per action).

## Fixes found during regression

1. **Access state evaluated in UTC** (`DashboardService`): rule day-of-week and schedule window were evaluated against `GetUtcNow()` (UTC) instead of the user's local clock (`utcOffset` was only applied to usage/bonus/day). In UTC+2 this blocked Mondays until 11:00 local and shifted the weekday during the first two local hours. Fix: convert `now` once with `.ToOffset(utcOffset)` before evaluating rules and resolving the active schedule. Reproduced with 2 regression tests (`DashboardServiceTests`).
2. **Stale orphan usage session inflated daily usage** (`UsageTrackingService`): after an abnormal stop (debugger kill, crash, power loss), the open session was adopted on restart and kept open; while open it counted continuously from the start of the day, so "usage today" equaled the current wall-clock time (visible in the History week total) and inflated days easily exceeded the daily limit, keeping the state `Blocked`. New semantics: an orphan loaded on first tick is closed at its own `StartedAt` (zero length, unobserved tail discarded, warning logged); a fresh session is opened only if the current tick is Active. Matches the fail-safe rule "a monitoring failure must not produce invalid historical data"; graceful stops keep closing at the last observed input via `StopAsync`. Reproduced with 3 regression tests (`UsageTrackingServiceTests`), including the midnight-crossing inflation case.

3. **Zero-length sessions could not be reloaded** (`UsageSession`/`SqliteUsageSessionStore`): `TryClose` accepts `EndedAt == StartedAt` (Phase 20 clock-jump rule) but `TryCreate` — used by `ToDomain` when reading persisted rows — rejected it, so any zero-length closed session (orphan discard, clock-jump close) crashed every later read that included it (History "has invalid persisted data"). Fix: `TryCreate` now rejects only `EndedAt < StartedAt`, making the round-trip consistent; a zero-length row loads as a legitimate "no computable usage" record. Reproduced with persistence round-trip and domain tests.

4. **Read-only database for the App on installed machines** (installer): `%PROGRAMDATA%\ControlTime` and `controltime.db` are created by the Service running as LocalSystem (the installer starts it before the App ever runs). The inherited ProgramData ACL gives a standard user only read access, so every App write — including the startup `Migrate()` — failed with `SQLite Error 8: attempt to write a readonly database`, shown as a crash dialog at each logon ("No se pudo preparar la base de datos"). The Service itself was healthy. Fix in `installer/ControlTime.iss`: a new `ssPostInstall` step creates the data folder if absent and applies `icacls /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q` (BUILTIN\Users Modify, inherited by future files, applied recursively to the existing database), before `sc start`. Machines already installed are repaired by re-running the installer (upgrade); the immediate manual workaround is the same elevated `icacls` command.

5. **Suspended/frozen process inflated daily usage** (`UsageTrackingService`, found on a second machine): with Windows Fast Startup the Service process can survive a "shutdown" frozen in memory holding its open usage session, so the Phase 22 orphan discard (which runs only when a session is loaded from SQLite after a restart) never fires. Observed in production data: a session started 27 Oct 20:12 local stayed open through the night, the WTS fallback kept reporting `Active` (`LastInputAt = now`, no input evidence) while nobody was at the machine, and it closed the next day at 20:06 with a duration of 1 day 0 h 54 min — "usage today" showed ≈20 h (midnight→20:06), daily limit exceeded, and enforcement locked the session in ~30 s cycles. Fix: the tracking host may pass an `observationGapTolerance` (the Service composes 15 s for its 1 s tick loop); when the gap between two consecutive ticks exceeds it, the stretch in between is unobserved, so the open session is closed at the last observed tick (`ActivityStatus.Timestamp`, zero length if it started exactly there) and a fresh session opens only if the current tick is Active; a warning is logged. `null` (default) disables gap detection, keeping hosts with unknown cadence (tests) unchanged. Reproduced with 5 regression tests, including the midnight-crossing inflation case (`UsageTrackingServiceTests`). Historical rows are not rewritten: pre-existing inflated sessions on already-affected machines remain in the database and must be cleaned manually if desired.

## Tests

279 tests green (`dotnet test`), solution build clean (`dotnet build`).

Version metadata verified on rebuilt artifacts (during the original 1.0.0 audit, before the Phase 23 bump): `ControlTime.App.exe` / `ControlTime.Service.exe` — FileVersion 1.0.0.0, ProductName/Copyright/Company "ControlTime"; `ControlTime-Setup-1.0.0.exe` — FileVersion/ProductVersion 1.0.0, ProductName/Company/Copyright "ControlTime". The final release artifacts were rebuilt at 1.1.0 from the tagged tree (including the regression fixes and Phase 24) and re-audited before tagging.

## Manual validation

Delegated to `docs/manual-testing.md` (regression script) plus the Phase 21 installer checklist.

Result: the `docs/manual-testing.md` regression was executed end-to-end on a real machine and passed, exercising the Phase 22 scope (including the fixes above and the Phase 21 install/upgrade/uninstall checklist). The feature-specific manual checklists of Phase 23 (floating overlay, `docs/phases/phase-23.md`) and Phase 24 (service-side input probe, `docs/phases/phase-24.md`) remain pending dedicated validation on a target machine; they are not blockers for `v1.1.0` by explicit user decision.

## Decisions

- RC bug policy: fix forward on the release version, no RC version suffixes; the tag is created only after the full checklist passes.
- Regression procedures live in the shared `docs/manual-testing.md`, not in this file, per documentation ownership.
- GitHub Release attaches only the Setup executable (source is the tag).
- `v1.0.0` dropped: Phase 23 shipped before the tag existed and no 1.0 artifact was ever published, so the first release is `v1.1.0` (phase retitled accordingly).
- ProgramData ACL: BUILTIN\Users gets Modify on `%PROGRAMDATA%\ControlTime` (fix 4). A technically skilled user could therefore edit the usage database directly; this is consistent with the documented security model (resistance to casual bypass, not a security boundary) and unavoidable while the App writes rules/grants directly to the shared database.
- Release proceeds with the Phase 23 and Phase 24 feature checklists still pending on a target machine (explicit user decision after the Phase 22 regression passed); their validation continues against `v1.1.0` and any finding is fixed forward.

## Known limitations

- Manual regression requires a physical machine and human interaction (UI, locks, balloons); it cannot be automated.
- Enforcement testing temporarily locks the developer workstation (mitigated by the kill switch and admin password).
- The WTS fallback (used while no App activity report is fresh) reports `Active` with `LastInputAt = now` whenever the console session is Active/Connected — even with nobody at the keyboard. The gap detection (fix 5) bounds its damage across freezes/reboots, but between fresh reports usage is attributed optimistically. A real user-session input probe in the Service would remove the optimism.
