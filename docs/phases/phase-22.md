# Phase 22 — Release Candidate and Version 1.1

Status: in progress — automated tasks complete; manual regression and the release tag (`v1.1.0`) are pending.

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

## Tests

262 tests green (`dotnet test`), solution build clean (`dotnet build`).

Version metadata verified on rebuilt artifacts (during the original 1.0.0 audit, before the Phase 23 bump): `ControlTime.App.exe` / `ControlTime.Service.exe` — FileVersion 1.0.0.0, ProductName/Copyright/Company "ControlTime"; `ControlTime-Setup-1.0.0.exe` — FileVersion/ProductVersion 1.0.0, ProductName/Company/Copyright "ControlTime". The final release artifacts are rebuilt and re-audited at 1.1.0 before tagging.

## Manual validation

Delegated to `docs/manual-testing.md` (regression script) plus the Phase 21 installer checklist.

## Decisions

- RC bug policy: fix forward on the release version, no RC version suffixes; the tag is created only after the full checklist passes.
- Regression procedures live in the shared `docs/manual-testing.md`, not in this file, per documentation ownership.
- GitHub Release attaches only the Setup executable (source is the tag).
- `v1.0.0` dropped: Phase 23 shipped before the tag existed and no 1.0 artifact was ever published, so the first release is `v1.1.0` (phase retitled accordingly).

## Known limitations

- Manual regression requires a physical machine and human interaction (UI, locks, balloons); it cannot be automated.
- Enforcement testing temporarily locks the developer workstation (mitigated by the kill switch and admin password).
