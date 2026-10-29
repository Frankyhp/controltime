# Phase 24 — Service-Side User Input Probe

## Goal

Measure real user input from the Windows Service so the WTS fallback no longer
reports `Active / LastInputAt = now` without evidence (idle machines and Modern
Standby S0 stretches were inflating usage between fresh App IPC reports).

## Scope

- New Infrastructure probe chain (`Windows/`): `InputProbeApi` (Win32
  primitives), `ConsoleInputProbeRunner` (Service-side validated launch
  sequence), `UserInputProbeChild` (helper side), `ConsoleSessionInputProbe`
  (cache + cadence + throttled logging).
- `WtsConsoleSessionMonitor` classifies an active console session as
  Active/Idle from the probed last input (via `ActivityEvaluator`, idle
  threshold `WindowsActivityMonitor.DefaultIdleThreshold`); without a fresh
  probe result it keeps the historical optimistic `Active / LastInputAt = now`.
- `ControlTime.Service` gains the helper mode `--probe-input <mapName>`
  (early exit before host/database construction) and wires the probe into the
  WTS fallback in `Program.cs`. No new shipped binary, no installer change.
- Phase 18 priority is unchanged: the probe only runs while no fresh App IPC
  report exists, because the probe sits behind `CompositeActivityMonitor`'s
  fallback.

## Implementation

Technique exactly as validated in `docs/spike-phase-24-input-probe.md`:

- `WTSQueryUserToken` + `DuplicateTokenEx` (primary) + `LoadUserProfile`
  (username via new `WtsApi.TryGetUserName`) + `CreateEnvironmentBlock` +
  `CreateProcessAsUserW` with `lpDesktop = NULL` and
  `CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT`.
- The helper is `ControlTime.Service.exe --probe-input <mapName>`: it calls
  `GetLastInputInfo` for its token's session and publishes the UTC FILETIME
  into a pagefile-backed `Global\ControlTime.InputProbe-<pid>-<guid>` section
  created by the Service with SDDL `D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GWGR;;;AU)`
  (`WindowsNamedPipes.SystemAdminsInteractiveSddl`). The unguessable GUID in
  the map name authenticates the payload; no window station/desktop DACLs are
  touched and no new IPC protocol or agent process is introduced.
- `ConsoleSessionInputProbe.TryGetLastInputAt(sessionId, out lastInputAt)`
  never blocks the 1 s monitoring tick: it serves the last probe result while
  fresh (lifetime 15 s) and schedules the ~1-2 s probe launch on the thread
  pool at most once every 5 s and at most one in flight. Results are keyed by
  session id (session switch invalidates the cache).
- Any probe failure (any Win32 step, helper timeout 5 s — the helper is
  terminated — or missing payload) keeps the optimistic `Active / LastInputAt
  = now`; failures are logged at most once per minute
  (`ConsoleSessionInputProbe.FailureLogThrottle`).
- `WtsConsoleSessionMonitor.EvaluateActive` is the pure decision point
  (probe value → `ActivityEvaluator`; future-dated probe clamps to now).
- Because the usage session closes at the probed `LastInputAt` (not at
  detection time), close-on-idle precision equals the probe cadence (~5 s),
  not the idle threshold.

## Tests

- `ConsoleSessionInputProbeTests`: first call returns false and schedules one
  probe; fresh result served from cache without a new probe; expired result
  re-probes; other-session results not served; single-flight (no concurrent
  probes); failing probe keeps returning false and repeats only after the
  interval; interval respected even when the result expires earlier;
  constructor argument guards.
- `WtsConsoleSessionMonitorTests` (`EvaluateActive`): no probe → optimistic
  Active; recent probe → Active with real `LastInputAt`; probe older than the
  idle threshold → Idle closing at the real moment; future probe clamped.
- `dotnet build` and full `dotnet test` green (279 tests). Helper-mode smoke:
  `ControlTime.Service.exe --probe-input <missing>` exits immediately with
  code 2 and no side effects.

## Manual validation

- [ ] Machine logged in and unattended (App closed) longer than the idle
      threshold: usage stops growing within one probe interval past the
      threshold; Service log shows the Idle transition and the usage session
      closed at the real last-input moment.
- [ ] Modern Standby (S0) suspend + resume does not add the suspended stretch
      to the day.
- [ ] With the App open, fresh IPC reports still win (banner/tray unchanged).
- [ ] Service run from a console (non-System): probe unavailable → optimistic
      fallback with throttled log, no per-tick errors.

## Decisions

- Helper is a mode of the Service exe itself (spike recommendation): no extra
  project, no installer changes; the probe path never opens the database.
- Probe failure keeps the optimistic `Active` with throttled logging (agreed
  in the plan): a broken probe must never stop counting legitimate usage.
- Probing happens in the background with a cached result; the monitoring tick
  is never blocked by the 1-2 s probe cost.
- AU write access on a GUID-named section instead of per-user SID DACLs: the
  unguessable name is the authentication; a local attacker able to guess it
  could only fake their own session's usage.

## Known limitations

- Probe granularity is 5 s (interval) and each probe costs ~1-2 s of a helper
  process launch.
- Input on the secure desktop is still invisible; lock state remains
  whatever WTS reports (unchanged, `Locked` only when the console session is
  gone/disconnected — a locked-but-active session with no input is reported
  `Idle` from this phase, which is the desired tracking behaviour).
- Already-persisted inflated sessions from before the fix are untouched
  (Phase 25 cleanup tool).
