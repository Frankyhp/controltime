# Manual Testing — ControlTime Regression Script

Reusable manual validation procedures. Referenced by phase documents instead of being duplicated in them.

## Preconditions

- Windows 10/11 x64, administrator account for install steps.
- Build artifacts: `pwsh build/publish.ps1` → `dist\ControlTime-Setup-<version>.exe`.
- Working rules to test with (create in the Rules screen):
  - **R-Schedule**: enabled, current weekday, time window covering now + ~30 min, generous daily limit.
  - **R-Tight**: enabled, current weekday, time window covering now, very small daily limit (e.g. 2 min) — used for warnings/enforcement.
  - **R-Disabled**: disabled rule that would otherwise allow everything.
- Kill switch: `CONTROLTIME_ENFORCEMENT=off` set in the **SYSTEM** environment (e.g. via `[Environment]::SetEnvironmentVariable(..., 'Machine')` then restart the Service) for all sections except Enforcement.
- Useful state to know: database and logs live in `%PROGRAMDATA%\ControlTime`; Service name `ControlTimeService`; check status with `sc query ControlTimeService` and `Get-Service ControlTimeService`.

Useful log locations: `%PROGRAMDATA%\ControlTime\logs\` (one file per day, per process: App and Service).

## 1. Administrator authentication

- [ ] First run of the App → Rules (or Settings) requires setting an administrator password before any action.
- [ ] Wrong password on unlock → error message, admin mode not granted.
- [ ] 5 consecutive failures → lockout message (~30 s), correct password during lockout does not grant admin mode.
- [ ] Correct password → admin mode window; Rules CRUD works while in admin mode; expired → gate returns.
- [ ] Settings: change password with wrong current password → rejected; with correct one → succeeds, new password works everywhere (Rules unlock, grants, exit).
- [ ] Tray "Salir de ControlTime" always requires the administrator password and never enters admin mode.

## 2. Rules / weekdays / schedules / daily limits

- [ ] Create/edit/delete rule with days, start/end time, daily limit; persists across App restart.
- [ ] Rule with a non-current weekday → Dashboard shows it does not grant access today.
- [ ] Outside schedule window → Dashboard state reflects not allowed.
- [ ] Rule disabled → ignored by evaluation (Dashboard + Service enforcement).
- [ ] Multiple rules → most permissive applicable state wins as designed (matches RuleEngine unit tests).
- [ ] Corrupted persisted row tolerance: not testable from UI; logs (`GetAllAsync` skip + warning) cover it — spot-check logs only if touched.

## 3. Bonus time (time grants)

- [ ] "Regalar tiempo" requires the administrator password; wrong password → no grant.
- [ ] Grant raises today's effective allowance in Dashboard (`Bono hoy` + remaining time recalculated).
- [ ] Grant does not affect yesterday/tomorrow; expires at midnight (verify next day).
- [ ] Historial: bonus attributed to the correct day.

## 4. Activity and idle detection

- [ ] Dashboard "Actividad" shows Activo while typing/moving mouse.
- [ ] No input for the idle threshold → Ocioso.
- [ ] Win+L lock → Bloqueado (App visible in another session or after re-login via state history; at minimum the App must not show Activo while locked).
- [ ] App closed (hidden to tray) → Service falls back to WTS console state (see logs); usage continues for connected console session as designed.

## 5. Usage tracking (sessions)

- [ ] Active time accumulates in "Uso hoy" within ~5 s granularity while the App is open.
- [ ] Stop moving/typing → accumulation stops at the idle threshold.
- [ ] Kill the App process while a session is open → Service adopts/orphans correctly (no lost or duplicated usage; check logs for adoption).
- [ ] Only the Service writes sessions: kill App → usage keeps counting in `%PROGRAMDATA%` database (verify via Dashboard next App start or logs).
- [ ] Stop the Service (`sc stop ControlTimeService`) → open session closed at last known activity; resume → new session opened.

## 6. Usage history

- [ ] Historial week total equals sum of the visible days since Monday (allowing open-session-to-now).
- [ ] 14-day list: Uso matches sessions per day; "Hoy"/"Ayer" labels correct around midnight.
- [ ] Selecting a day shows its sessions; times coherent with activity periods.
- [ ] Manual refresh updates after a new active period.

## 7. Warnings and notifications

- [ ] With R-Tight and enforcement off: balloon at ~15, ~5 and ~1 minute remaining (allow for the rule limit size; a 15-min limit shows the 5/1 warnings).
- [ ] Each threshold fires at most once per day (dedup) — do not re-notify on subsequent ticks.
- [ ] Dashboard shows the explicit remaining-time warning text.
- [ ] Notifications arrive even when the main window is hidden (tray icon present).

## 8. Enforcement

(Enable: remove `CONTROLTIME_ENFORCEMENT` from Machine environment and restart the Service.)

- [ ] Exhaust R-Tight's limit while inside its schedule → console locked (disconnect/access screen) within ~1 s of `Blocked`.
- [ ] Re-login → new Active episode → ~30 s grace before re-lock while still Blocked.
- [ ] Enter admin mode → enforcement suspended (work while Blocked); on admin-mode expiry → re-locked with the grace rule.
- [ ] Grant bonus time while Blocked → works again (limit raised), no lock.
- [ ] `Blocked` with only disabled rules → never locks.
- [ ] App closed/killed while Blocked → Service keeps locking (Phase 17a core goal).
- [ ] Kill switch re-enabled + Service restart → no locks, warning logged at startup.
- (Restore kill switch afterwards for the remaining sections.)

## 9. Tray and startup

- [ ] Tray tooltip mirrors live state (state + remaining time; changes with activity/limits).
- [ ] Context menu: Abrir shows the window; estado/tiempo restante readable; "Iniciar con Windows" toggle removed from tray (Phase 16) — verify it is NOT present.
- [ ] Closing the main window hides it (process and tray persist); real exit only via tray "Salir" + admin password.
- [ ] Settings "Iniciar con Windows" toggle (admin-gated): enabling writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value; reboot or `reg query` confirms; disabling removes it.
- [ ] Second instance: launching the App again while running → second instance exits immediately (single instance per session).

## 10. Windows Service

- [ ] App works (Dashboard live) while Service is stopped, with the "Service unavailable" banner shown (Dashboard) and tray tooltip reflecting it.
- [ ] Restart Service → banner disappears, tracking/enforcement resume.
- [ ] Fresh App activity report is preferred over WTS fallback (logs: `CompositeActivityMonitor` uses report; idle detection works with App open).
- [ ] `sc failure ControlTimeService` shows `restart/60000 ×3`; `Kill-Process (Service PID)` → auto-restarts within ~60 s.
- [ ] Both processes tolerate concurrent DB access (no busy errors in logs; 5 s busy timeout).

## 11. Reliability and recovery (spot checks)

- [ ] App survives repeated UI operations without crashing; crashes logged as `Critical` in App log if forced (only if deliberately induced).
- [ ] Service with an unwritable/locked database at startup → stays idle after retries (no crash loop), App shows Service unavailable. (Optional; needs deliberately breaking DB access.)
- [ ] Day boundary: sessions crossing midnight attribute usage to the correct day (spot check around 00:00 or accept unit coverage).

## 12. Installation (clean)

On a machine without ControlTime (or after full uninstall + data deletion):

- [ ] `ControlTime-Setup-<version>.exe` (SmartScreen warning expected — unsigned): installs per-machine to `%PROGRAMFILES%\ControlTime` (`Service\` subfolder), Start Menu shortcuts, Spanish UI.
- [ ] `sc query ControlTimeService` → RUNNING, start type delayed-auto, Log On as LocalSystem; `sc qdescription`/`sc failure` show display name and `restart/60000 ×3`.
- [ ] `%PROGRAMDATA%\ControlTime\controltime.db` created on first run; logs directory appears.
- [ ] Post-install "Iniciar ControlTime" launches the App as the current user.
- [ ] No global autostart key written to HKLM Run.

## 13. Upgrade

- [ ] With everything installed and running, run the new installer → stops Service and closes App automatically, replaces files, Service running afterwards.
- [ ] Data preserved: admin password, rules, history, grants all intact.
- [ ] App single-instance and tray behaviour unchanged after upgrade.

## 14. Uninstall

- [ ] Uninstall answers "No" to data deletion → Service stopped+deleted (`sc query` → 1060), program files gone, Start Menu shortcut gone, `%PROGRAMDATA%\ControlTime` intact.
- [ ] Reinstall afterwards picks up the old database seamlessly.
- [ ] Uninstall answering "Sí" → `%PROGRAMDATA%\ControlTime` removed too.

## Notes

- Time-related sections (schedule boundaries, midnight, DST) are covered exhaustively by `ControlTime.Tests` unit tests; manual checks here only verify wiring, not rule math.
- When testing short limits, remember warnings dedup per day — use a second test rule or the next day.
