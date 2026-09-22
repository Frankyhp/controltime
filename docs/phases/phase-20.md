# Phase 20 — Reliability and Recovery

## Goal

Strengthen ControlTime (App + Service) for long-term normal use: persistent diagnostics on disk, crash tolerance, clock-change safety, clean usage-session close on shutdown, and graceful handling of corrupted data.

```text
20.1 rolling file logs (%PROGRAMDATA%\ControlTime\logs, App + Service)
20.2 App unhandled-exception handlers; Service DB prepare with bounded retry + fail-safe idle
20.3 clock/DST/midnight safety in Core (UsageSession/UsageCalculator/UsageTrackingService)
20.4 explicit usage-session close on Service StopAsync
20.5 per-row tolerance on rules load; explicit corrupted-password behaviour
```

## Scope

In scope:

- Infrastructure:
  - `Logging/ControlTimeFileLoggerProvider`: minimal rolling file logger (no third-party libraries). One file per day per process (`controltime-{process}-{yyyyMMdd}.log`, sequence suffix past a 10 MB cap), 14-day retention, `Information` and above, writes never throw back into callers. `ControlTimeLoggingExtensions.AddControlTimeFileLogs()` registers it.
  - `ControlTimeStorage.LogDirectory` (`%PROGRAMDATA%\ControlTime\logs`) + `EnsureLogDirectory()`.
  - `SqliteUsageRuleStore.GetAllAsync`: per-row tolerance — an invalid persisted row is logged (`Warning`) and skipped, never fails the whole load.
- Core:
  - `IPasswordHasher.IsValidFormat` (implemented in `Pbkdf2PasswordHasher` via a shared format parser also used by `Verify`).
  - `AdminAuthService`: a stored hash that exists but is malformed is an explicit `CorruptedPassword` result (`AdminAuthenticationResult` / `AdminPasswordChangeResult`, appended after `ServiceUnavailable` so IPC error codes stay stable). It never enters admin mode, never counts toward the attempt lockout, and never mutates the stored setting. `VerifyPasswordAsync` returns `false` without registering a failed attempt.
  - `UsageSession.TryClose`: closing at exactly `StartedAt` is allowed (zero-length session); closing earlier is rejected. `TryCreate` (closed sessions) remains strict.
  - `UsageTrackingService`: optional `ILogger<UsageTrackingService>`; closing with `LastInputAt < StartedAt` (backward clock jump) now closes the session at `StartedAt` with a warning log instead of deleting it (previous silent data loss); new `CloseOpenSessionAsync(closeAt)` used by Service shutdown, with the same clamp.
  - Core now references `Microsoft.Extensions.Logging.Abstractions` (abstractions only; still no WPF/EF/SQLite/Win32).
- App:
  - File logging registered alongside `AddDebug`; startup/shutdown and tray-exit audit lines.
  - `DispatcherUnhandledException` + `TaskScheduler.UnobservedTaskException` handlers: log `Critical`, keep the UI alive (`Handled`/`SetObserved`) — the App is presentation-only and the Service enforces independently, so losing the tray/activity reports would be worse than continuing.
  - `ServiceClientHost` logs Service availability transitions (Warning on lost, Information on recovered) alongside its existing poll-failure errors.
  - Corrupted-password message surfaced in Rules and Settings unlock/change-password flows.
- Service:
  - File logging registered (Event Log registration unchanged).
  - `MonitoringLoop`: `PrepareDatabase` now runs with 6 attempts / 5 s delay; on final failure it logs `Critical` and returns without starting ticks — fail-safe idle (no usage writes, no enforcement, `LastState` stays null so the App shows Service unavailable) instead of killing the host silently.
  - `MonitoringLoop.StopAsync`: closes the open usage session at the last known `LastInputAt` (fallback: now) before stopping; failures are logged and orphan adoption remains the safety net.

Out of scope:

- Installer/packaging (Phase 21), anti-tampering, database encryption, third-party logging/resilience libraries, repair flows for corrupted data (policy is skip + log, never silent repair).

## Decisions

- Corrupt-data policy: skip + log; never repair silently, never fail the whole screen.
- Zero-length close (end == start) is valid for closing an open session; this makes the backward-clock-jump case preserve an auditable (zero-usage) row instead of deleting it. One pre-existing test asserted the old discard-by-delete behaviour and was updated to the new intended behaviour.
- `HasPasswordAsync` still returns true with a corrupted hash (a hash exists); the explicit failure surfaces on verification, which is where the user acts on it.
- Service DB-failure policy: bounded retry then fail-safe idle loop (per plan); not retry-forever.
- The App continues after logged unhandled UI exceptions (presentation-only process); revisit per-scenario if a crash loop ever appears.
- File logger lives in Infrastructure with per-process file names: App and Service never share a file handle, so no cross-process locking was needed.

## Tests

257 tests green (`dotnet test`), solution build clean (`dotnet build`).

- New: `UsageSessionTests` zero-length close; `UsageTrackingServiceTests` backward clock jump (session kept, zero usage, non-negative day totals), `CloseOpenSessionAsync` (normal close, clamp-before-start clamp, no-op without session); `UsageCalculatorTests` future open session ignored, zero-length session counts as nothing, forward clock jump across midnight splits coherently, DST short day (23 h) and long day (25 h) window totals stay coherent; `AdminAuthServiceTests` corrupted hash (explicit result, no admin mode, no lockout counting, settings untouched, recovery after repair); `PasswordHasherTests` `IsValidFormat` valid/corrupted matrix; `RuleStoreCorruptedRowTests` (real SQLite + raw-SQL corrupted row: valid rules load, corrupted skipped); `ControlTimeFileLoggerTests` (writes Information+, formats line/exception, retention deletes stale files and keeps recent ones).
- Updated: `UsageTrackingServiceTests.Close_WithNoUsageSinceStart_*` now expects a zero-length closed session instead of deletion (see Decisions).

## Manual validation

1. Kill the Service (`taskkill`) → App banner/tooltip shows Service unavailable; sessions recovered by adoption on restart; both behaviors visible in `%PROGRAMDATA%\ControlTime\logs`.
2. System clock backward while a session is open → Dashboard/Historial never show negative usage; no session lost (zero-length row).
3. System clock forward across midnight → day rolls over, previous day closes correctly at midnight boundary.
4. Windows shutdown with an open session → after boot no multi-hour phantom usage (adoption safety net if `StopAsync` never ran).
5. `sc stop ControlTimeService` with an open session → session closed explicitly in the database (row has `EndedAt`).
6. Corrupt one rules row via SQL (e.g. `AllowedDays='x'`) → Rules screen loads the valid rules, `Warning` line in the Service/App log.
7. Make the database unreadable (locked file/permissions) → Service logs warnings ×6 then one `Critical`, does not enforce; App shows its startup error dialog; logs on disk either way.

## Known limitations

- The corrupted admin password has no in-UI repair flow (message tells the administrator to repair/reinstall data); a supported reset flow is future work.
- `StopAsync` cannot guarantee the session close if Windows kills the process hard; orphan adoption remains the backstop.
- Log files are not access-restricted beyond `%PROGRAMDATA%` defaults and may contain paths/timestamps; no sensitive data (no passwords) is ever logged.
- File logger drops lines on IOException (e.g. disk full) rather than throwing into callers.
