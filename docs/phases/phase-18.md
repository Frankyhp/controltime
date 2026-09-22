# Phase 18 — User Session Integration

## Goal

Integrate the App with the logged-in user's Windows desktop session: the App is formally the user-session agent (activity reporter, lock/idle probe) and the Service only trusts activity reports attributable to the active console session. No new process is introduced.

```text
ControlTime.App (user session = the agent)
  ├─ single instance per Windows session
  ├─ GetLastInputInfo + SessionLockProbe ──► ReportActivity (pipe)
  ▼
ControlTime.Service (SYSTEM)
  ├─ attributes each report to the client's real Windows session (PID → SessionId)
  ├─ ActivityReportSink: per-session freshness cache
  └─ CompositeActivityMonitor: fresh report of the ACTIVE CONSOLE SESSION only > WTS fallback
```

## Scope

In scope:

- Activity reports attributed to a real Windows session (server-side, unfakeable from the payload):
  - `ControlTimePipeServer` keeps its own `SafePipeHandle` reference and calls `GetNamedPipeClientProcessId` (new P/Invoke in `WindowsNamedPipes`), then resolves PID → `Process.SessionId` (injectable `Func<int, int?>` for tests).
  - `ReportActivity` from a connection whose session cannot be resolved is rejected (logged), never applied.
  - `ActivityReportSink` is now a per-session cache: `Report(sessionId, status)`, `TryGetFreshForSession(sessionId, maxAge)`, `HasFreshReportForSession(...)` (thread-safe via lock).
- `CompositeActivityMonitor` only consumes fresh reports belonging to the **active console session** (`WindowsConsoleSessionId.TryGet` over `WTSGetActiveConsoleSessionId`, injectable `Func<int?>`); no active console session → WTS fallback (`Locked`). New `IsUsingReportedActivity()` used by `ServiceStatusProvider` for the `UsingReportedActivity` flag.
- App single instance per Windows session: named mutex `Local\ControlTime.App` acquired in `OnStartup`; a second instance shuts down immediately (no duplicate reporters or tray icons; `Local\` is per-session, so different users can still run their own App).

Out of scope:

- Separate headless user agent (deliberately not introduced — the App already auto-starts at login, survives window close to tray and requires the administrator password to exit; Windows session isolation does not currently require another process).
- Multi-user concurrent support, installer / automatic Service registration (Phase 21), encrypted/signed reports.

## Decisions

- The App **is** the user-session agent (plan's own rule: only add a process if session isolation requires it). The `App → Agent → Service` diagram remains a possible future evolution, documented not implemented.
- Session attribution is derived server-side from the pipe connection (client process PID), never from a payload field, so a client cannot claim another session. PID→SessionId has an unavoidable PID-reuse race window (accepted; worst case a report is attributed to the reuse-racing session for ≤5 s of freshness).
- Unattributable reports are rejected rather than applied globally (fail-safe: never invents activity).
- Mutex name `Local\ControlTime.App` (per-session, not `Global\`): a second concurrent user gets their own agent.

## Tests

217 tests green (`dotnet test`), solution build clean (`dotnet build`).

- `ActivityReportSinkTests` (new): per-session attribution, per-session freshness windows, latest-report-per-session wins, unknown session → null.
- `CompositeActivityMonitorTests`: console-session report wins (fallback not probed), other-session report ignored (fallback probed), no active console session → fallback, stale report → fallback, `IsUsingReportedActivity` semantics.
- `IpcTests.ReportActivity_LandsInSinkAttributedToClientSession`: real pipe round trip lands in the sink under the client process's real session id; a foreign session id sees nothing.

## Manual validation

Checklist (requires the registered Service; same registration as Phase 17a):

- Login → App auto-starts → Service status shows `UsingReportedActivity=true` within ~1 s.
- Lock (Win+L) with UI closed to tray → App reports `Locked` → open usage session closes; enforcement proceeds.
- Unlock → fresh `Active` episode → 30 s re-lock grace preserved when `Blocked`.
- Logout → App dies with the session → WTS fallback `Locked`, no orphan open sessions after adoption.
- Restart Windows → Service (delayed-auto) + App (HKCU Run) return; usage continues from stored sessions.
- Service restart (`sc stop/start`) → App shows unavailable banner then recovers; no double usage-session writes.
- UI closed (hidden to tray) → idle detection keeps working (the App is the agent).
- UI reopened while running → second instance exits immediately, first instance untouched (single instance).
- Fast user switching → user A's hidden App no longer influences Service activity while the console is on user B (B's reports or WTS fallback win).

## Known limitations

- The console-session App's reports are still forgeable by that same interactive user (a process in their own session gets the same attribution); making that impossible requires OS-level trust (Service-side impersonation or signed agents) beyond the security model.
- If the console user's App is killed (requires admin), idle/lock precision degrades to the WTS fallback (usage keeps counting) until the App returns.
- PID→SessionId resolution has a theoretical PID-reuse race (see Decisions).
- `GetNamedPipeClientProcessId` is Windows-only; consistent with the Windows-only Infrastructure.
