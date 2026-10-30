# Phase 25 — Historical Usage Cleanup Tool

## Goal

Provide a supported, documented way to repair usage databases inflated by the
pre-Phase-22 gap bug: overnight sessions (observed 21 h, 23 h, 1 day, 3 days)
that were never actually used keep inflating Historial and daily limits, so
those days remain `Blocked` forever. The Phase 22 fix prevents new ones but
does not rewrite history.

## Scope

- Documented SQL procedure (`sqlite3` CLI or any SQLite client), kept out of
  the shipped binaries: lists sessions longer than an administrator-provided
  threshold and closes them at their own `StartedAt` (zero length — the
  established "no computable usage" convention from Phase 22).
- Runbook for an installed machine: stop Service → backup → preview → apply →
  start Service → verify.
- Validation section with the exact INSERTs to reproduce inflated sessions on
  a development database.

## Out of scope

- Automatic self-repair on startup.
- Deleting rows (zero-length closing preserves the audit trail).
- Recomputing enforcement history (state is always re-derived from rules).

## Requirements

- A SQLite client on the machine. Recommended: `winget install SQLite.SQLite`
  (provides `sqlite3.exe`), or DB Browser for SQLite (paste the same SQL in
  the "Execute SQL" tab).
- An elevated console for `net stop` / `net start`.
- The tool edits the **production database** as-is: a machine-local operation,
  not a shipped artifact.

## Procedure (installed machine)

Database: `%PROGRAMDATA%\ControlTime\controltime.db`. The `UsageSessions`
table stores `StartedAtUtc` / `EndedAtUtc` as ISO-8601 UTC text
(`YYYY-MM-DD HH:MM:SS.FFFFFFF`, no offset suffix); `EndedAtUtc IS NULL` means
open. All timestamps below are **UTC**: to clean "yesterday's" night on a
UTC+2 machine, use the UTC equivalent of the local evening boundary.

### 1. Stop the Service and back up

```powershell
net stop ControlTimeService
cd $env:PROGRAMDATA\ControlTime
copy controltime.db "controltime.backup-$(Get-Date -Format yyyyMMdd-HHmmss).db"
```

The Service must be stopped: it is the only session writer, and the backup
must be taken with no WAL activity.

### 2. Preview — list the inflated sessions

Pick a threshold that only real overnight sessions exceed (e.g. 10 h =
`36000` seconds; `3 days` = `259200`). Sessions of zero length (already
cleaned) are excluded.

```sql
SELECT Id, StartedAtUtc, EndedAtUtc,
       CAST(COALESCE(strftime('%s', EndedAtUtc), strftime('%s', 'now'))
            - strftime('%s', StartedAtUtc) AS INTEGER) AS DurationSeconds
FROM UsageSessions
WHERE (EndedAtUtc IS NOT NULL
       AND strftime('%s', EndedAtUtc) - strftime('%s', StartedAtUtc) > 36000)
   OR (EndedAtUtc IS NULL
       AND strftime('%s', 'now') - strftime('%s', StartedAtUtc) > 36000)
ORDER BY StartedAtUtc;
```

Review every row: sessions that legitimately exceed the threshold (rare;
long attended use) should NOT be cleaned. Lower the threshold only if the
remaining inflated sessions still exceed it.

### 3. Apply — close them at their own start (zero length)

Same `WHERE` as the preview: zero-length sessions (already cleaned) are
excluded naturally because `strftime` returns NULL when
`EndedAtUtc = StartedAtUtc`.

```sql
BEGIN IMMEDIATE;
UPDATE UsageSessions
SET EndedAtUtc = StartedAtUtc
WHERE (EndedAtUtc IS NOT NULL
       AND strftime('%s', EndedAtUtc) - strftime('%s', StartedAtUtc) > 36000)
   OR (EndedAtUtc IS NULL
       AND strftime('%s', 'now') - strftime('%s', StartedAtUtc) > 36000);
-- Verify the count matches the preview; then COMMIT (ROLLBACK otherwise):
COMMIT;
```

Rows are never deleted: a zero-length closed session is the persisted form of
"no computable usage" (same convention as orphan discard and clock-jump
closes).

Residual **open** sessions are cleaned too (second `OR` branch). With the
Service stopped there is no legitimate open session; if left, the Service's
Phase 22 orphan discard would zero it on next start anyway — doing it here
keeps the outcome identical and explicit.

### 4. Restart and verify

```powershell
net start ControlTimeService
```

Optional consistency check: `sqlite3 controltime.db "PRAGMA integrity_check;"`
must return `ok`. A `VACUUM;` can shrink the file afterwards (not required).

## Warnings

- The legitimate evening usage contained in those sessions is lost too: the
  bogus session mixed real and phantom time and cannot be reconstructed.
- Run with the Service stopped. If it was running, the Service keeps its
  in-memory open session and will persist it over the edits on close.
- Take the backup before applying. If anything looks wrong in the preview,
  `ROLLBACK` instead of `COMMIT` and the database is untouched.
- Today's inflated session may keep the machine `Blocked` until cleaned; the
  Dashboard reflects the repair within one monitoring tick after restart.

## Tests

No code changes in the shipped projects (Core/Infrastructure/App/Service are
untouched): `dotnet build` and `dotnet test` must simply remain green. The
procedure itself is validated manually below.

## Manual validation (development machine)

Dev database path: `%PROGRAMDATA%\ControlTime\controltime.db` (same as
installed, unless `ControlTimeStorage.DatabaseDirectory` was overridden).

### Reproduce the bug

With the Service stopped, insert two inflated sessions (≈21 h ending around
now, and a 3-day-old ≈23 h one) plus one legitimate 2-minute session today
that must survive the cleanup:

```sql
-- ~21 h inflated session (last night through now, UTC):
INSERT INTO UsageSessions (Id, StartedAtUtc, EndedAtUtc) VALUES
  (lower(hex(randomblob(16))), datetime('now', '-21 hours', 'subsec'), datetime('now', 'subsec'));
-- ~23 h inflated session from 3 days ago:
INSERT INTO UsageSessions (Id, StartedAtUtc, EndedAtUtc) VALUES
  (lower(hex(randomblob(16))), datetime('now', '-72 hours', 'subsec'), datetime('now', '-49 hours', 'subsec'));
-- legitimate 2-minute session today (must NOT be cleaned):
INSERT INTO UsageSessions (Id, StartedAtUtc, EndedAtUtc) VALUES
  (lower(hex(randomblob(16))), datetime('now', '-2 hours', 'subsec'), datetime('now', '-118 minutes', 'subsec'));
```

The `datetime(..., 'subsec')` output above matches the text format the
application writes (`YYYY-MM-DD HH:MM:SS.FFFFFFF`, UTC). If you prefer a
SQLite GUI client, paste the same statements in its SQL console; avoid
editors that rewrite timestamp literals (they can break the range
comparisons the stores rely on).

### Validate

- [ ] Historial shows the inflated day(s); with a tight daily limit, today is
      `Blocked` (may require an enabled rule exceeding the inflated usage).
- [ ] Preview SQL (threshold 36000) lists exactly the inflated sessions
      (durations ≈ 21 h and ≈ 23 h), and not the 2-minute legitimate session.
- [ ] Already-cleaned zero-length rows from earlier runs are not listed.
- [ ] Apply SQL reports the same row count as the preview; after `COMMIT`,
      the preview returns no rows.
- [ ] `EndedAtUtc = StartedAtUtc` for every cleaned row; no rows were deleted
      (`SELECT COUNT(*) FROM UsageSessions;` unchanged).
- [ ] Historial no longer counts the inflated time; the affected days show
      near-zero usage and are no longer `Blocked`.
- [ ] The 2-minute legitimate session is untouched and still counted.
- [ ] Session still loadable by the App: Historial opens without the
      "invalid persisted data" error (zero-length round-trip, Phase 22).
- [ ] `PRAGMA integrity_check;` → `ok`.

## Decisions

- Documented SQL procedure (option chosen in planning) instead of a shipped
  binary or a PowerShell P/Invoke helper: one-time per machine operation, the
  SQL itself is the audit trail, and nothing breaks if the publish layout
  changes. `sqlite3` CLI recommended; any SQLite client works.
- Close at `StartedAt` (zero length) instead of deleting: preserves the audit
  trail and reuses the established Phase 20/22 "no computable usage"
  convention, which the stores and Historial already handle.
- Threshold is administrator-provided with no default on purpose: a wrong
  default silently destroying legitimate evening usage is the worst failure
  mode of this tool.
- Residual open sessions are included in the same cleanup (identical outcome
  to the Service's orphan discard, but explicit and Service-independent).

## Known limitations

- Legitimate time inside cleaned sessions is lost (irreconstructible by
  design — flagged in the warnings).
- `strftime('%s', ...)` truncates to whole seconds; irrelevant at hour-scale
  thresholds.
- A session whose legitimate use legitimately exceeds the threshold would be
  zeroed too; the mandatory preview step exists precisely to catch that.
- Already-`Blocked` *future* schedules are unaffected (they never were stored;
  enforcement state is re-derived from rules).
