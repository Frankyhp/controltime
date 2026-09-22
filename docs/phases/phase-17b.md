# Phase 17b — Service IPC and App Client

## Goal

Make the App a client of the Windows Service over a local named pipe: status, administrator authentication and activity reports flow through the Service process, which is the single authority for enforcement and admin mode.

```text
ControlTime.App                                ControlTime.Service (SYSTEM)
  ServiceClientHost ── GetStatus  ~1 s ───────►  ServiceStatusProvider (last MonitorState + admin + flags)
                   ── ReportActivity ~1 s ────►  ActivityReportSink → CompositeActivityMonitor
                   ── EnterAdminMode(pwd) ────►  AdminAuthService (sole instance) → EnforcementService
  GatewayAdminAuthService (IAdminAuthService cache of polled status)
```

## Scope

In scope:

- Core contracts: `ServiceStatus` DTO, `IpcRequest`/`IpcResponse`, `ActivityReportPayload`, `ServiceStatusMapper` (`ToServiceStatus`/`ToMonitorState`), `IControlTimeGateway` abstraction. No Windows/pipe types in Core.
- Infrastructure IPC:
  - `ActivityReportSink`: caches the latest reported `ActivityStatus` with freshness window.
  - `CompositeActivityMonitor : IActivityMonitor`: report fresher than 5 s wins; otherwise WTS fallback.
  - `ControlTimePipeServer`: named pipe `ControlTime.Monitoring`, one JSON line per request/response, multiple concurrent connections; admin operations execute server-side against `AdminAuthService`.
  - `ControlTimePipeClient : IControlTimeGateway`: one connection per operation, 1 s connect timeout, availability flag.
  - `GatewayAdminAuthService : IAdminAuthService`: App-side façade over the gateway (UI code unchanged); `IsAdminMode` served from the polled status.
  - `WindowsNamedPipes`: `CreateNamedPipeW` P/Invoke with SDDL `D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GWGR;;;IU)` (System/Admins full, interactive users read+write) and `FILE_FLAG_OVERLAPPED`.
- Service: `ServiceStatusProvider`, `PipeServerHost` (BackgroundService), `MonitoringLoop.LastState`; admin mode now pauses enforcement for real (same process).
- App: `MonitoringHost` replaced by `ServiceClientHost` (1 s: probe local activity → `ReportActivityAsync` → `GetStatusAsync` → map → UI events; `AvailabilityChanged`); Dashboard warning banner and tray tooltip on service unavailable; Settings enforcement label now reflects the Service status; App no longer registers stores/hasher/admin logic beyond rules/grants/settings writes.
- New result member `ServiceUnavailable` on the admin result enums with UI messaging; SQLite connection string sets a 5 s busy timeout (App and Service are now separate writers).
- Infrastructure and Tests TFM moved to `net10.0-windows` (`PipeSecurity` is not available on plain `net10.0`; the whole IPC layer is Windows-only anyway).

Out of scope:

- User-session agent, authenticated activity reports, automatic Service registration (Phase 18/21).

## Notable implementation findings

- `System.IO.Pipes` `PipeSecurity` does not exist in modern .NET (any TFM): the pipe DACL is set via SDDL + `ConvertStringSecurityDescriptorToSecurityDescriptorW` + `CreateNamedPipeW`. `LocalFree` lives in `kernel32.dll`.
- A handle handed to `new NamedPipeServerStream(PipeDirection, isAsync: true, isConnected: false, SafePipeHandle)` must be created with `FILE_FLAG_OVERLAPPED` and must not be disposed by the caller.
- `AdminAuthService.SetPasswordAsync` intentionally enters admin mode (Phase 5 design); IPC tests account for it.

## Tests

212 tests green (`dotnet test`), solution build clean (`dotnet build`).

- `ServiceStatusMapperTests`: DTO round-trip preserves activity, snapshot fields and active schedule; no-schedule case.
- `CompositeActivityMonitorTests`: no reports → fallback; fresh report wins (fallback not probed); stale report falls back; sink freshness window.
- `IpcTests` (real named pipes, in-process server + client): status round-trip; status unavailable → null; activity report lands in sink; admin mode enter (correct/incorrect password) verified server-side; `HasPassword`/`VerifyPassword` through the pipe; `GatewayAdminAuthService` reflects server admin mode; missing service → `ServiceUnavailable` results and `IsServiceAvailable == false`.

## Manual validation (done / pending)

- Done (console smoke, kill switch off): Service running → client polls status (`access`, `usage`, `pwd`, `enf` flags); App-style `ReportActivity(Idle)` flips service activity to Idle with `UsingReportedActivity=true` and usage tracking closes the open session at the reported last-input time (no more phantom usage). No service failures logged.
- Pending: UI run (banner on stopped service, tray tooltip, admin unlock through the gateway, exit prompt verified by the Service) and `sc` registration as in Phase 17a.

## Decisions

- Request/response one JSON line per operation over a single named pipe; no push channel (1 s polling cadence already existed).
- The App keeps writing rules/grants/settings directly to SQLite; only admin mode and enforcement state are Service-owned. SQLite busy timeout protects concurrent writers.
- Pipe ACL grants interactive users read+write; admin commands require the administrator password verified server-side (lockout applies server-side).
- `IAdminAuthService` stays the UI-facing abstraction; the local implementation moved to the Service.

## Known limitations

- Activity reports are not authenticated: a process running as the logged-in user could fake them (same trust level as editing the database directly; formalized in Phase 18 with a real agent).
- While the Service is down, the App only shows the unavailable banner; there is no local fallback tracking (by design: single writer).
- `HasPassword` on an unavailable service reports `false`, which can show the password-setup panel until the Service is reachable.
- Enforcement kill switch is read from the Service environment (`SYSTEM`), not from the user's environment.
- Tray tooltip keeps the last known state when the Service becomes unreachable until the next availability change repaints it.
