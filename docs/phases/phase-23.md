# Phase 23 — Floating Remaining-Time Overlay

## Goal

Show a movable, always-on-top floating timer with today's remaining usage time, toggled from the system tray menu, so the user can see how much time is left without opening the Dashboard.

## Scope

* Version 1.1 (first published release; the planned `v1.0.0` was never tagged — see `phase-22.md`).
* Presentation only in `ControlTime.App`.
* No changes to Core, Service, Infrastructure or IPC: remaining time already arrives every second via `ServiceClientHost.StateChanged` (`MonitorState.Snapshot.RemainingTime`).

## Implementation

* `ControlTime.App` `Views/TimerOverlayWindow` (XAML + minimal code-behind):
  * borderless window (`WindowStyle=None`, `AllowsTransparency=True`, transparent background, `SizeToContent`, `ShowInTaskbar=False`, `ShowActivated=False`);
  * single large `TextBlock` (`H:mm`, tabular numerals) with semi-transparent white foreground (`#99FFFFFF`) and a soft drop shadow for legibility over arbitrary content;
  * movable via `DragMove()` on left button press;
  * `Topmost` re-asserted on `Deactivated`;
  * no business logic: `SetRemainingText` / `MarkPositionSaved` only.
* `ControlTime.App` `Services/TimerOverlayController` (singleton):
  * subscribes to `ServiceClientHost.StateChanged` / `AvailabilityChanged`;
  * formats `Snapshot.RemainingTime` (`—` when there is no limit/rules or no state);
  * overlay is shown only while the Service is available: it hides automatically when the Service goes away and reappears when it returns, without changing the user preference;
  * toggled from the tray (`ToggleAsync`), never mutating UI state in the tray itself.
* `TrayIconService`: checkable menu item "Mostrar tiempo restante en pantalla" below "Abrir ControlTime"; `TimerOverlayToggleRequested` event + `SetTimerOverlayPreference` to mirror the persisted preference.
* Persistence in `IAppSettingsStore` (per-user database):
  * `TimerOverlayVisible` (`true`/`false`),
  * `TimerOverlayLeft` / `TimerOverlayTop` (invariant-culture doubles), saved after each drag and defensively on hide/shutdown.
* Position handling: first show defaults to the top-right of the primary work area (applied after first layout, since `SizeToContent` measures late); stored position is clamped to the virtual screen so the overlay never ends up unreachable after monitor changes.
* `App.xaml.cs`: DI registrations (`TimerOverlayWindow`, `TimerOverlayController`), tray wiring, `InitializeAsync` on startup, `Shutdown()` (persist position, unsubscribe, hide) on both exit paths.
* `Directory.Build.props`: `<Version>` bumped to `1.1.0` (flows into all assemblies and the installer).

## Tests

* No new automated tests: the feature is pure presentation and formatting; the shared `H:mm` formatting matches the existing tray/Dashboard behaviour.
* `dotnet build` clean; `dotnet test` green (257 tests).

## Manual validation

1. Tray menu → "Mostrar tiempo restante en pantalla": overlay appears top-right, above all windows, showing `H:mm` matching Dashboard/tray.
2. Drag the overlay near a screen edge → it stays reachable; restart the App and re-show it → it reappears in the same position.
3. Toggle off → overlay disappears; restart App → remains hidden. Toggle on → restart App → reappears at the saved position.
4. Stop `ControlTimeService` → overlay hides; start it again → overlay reappears (preference intact).
5. With no enabled rules / no daily limit the overlay shows `—`.
6. Open other windows / change focus → overlay stays on top; activate another topmost window and click elsewhere → overlay re-asserts topmost.
7. Tray "Salir de ControlTime" (admin password) → overlay closes and position is persisted.

## Decisions

* The App is purely a presenter, so the overlay lives entirely in the App; the Service and IPC are untouched.
* The overlay hides (rather than showing stale/`—` data) while the Service is unavailable: without the Service there are no trustworthy numbers to display.
* Visibility and position are persisted; the default position is top-right of the primary work area.
* Implemented as v1.1 by explicit user decision, before any release tag existed; `v1.0.0` was dropped and 1.1.0 became the first release (`v1.1.0`); the fixed-`AppId` installer already supports the upgrade.
* `ShowActivated=False`: showing the overlay never steals keyboard focus.

## Known limitations

* Not visible on the Windows lock screen (secure desktop); no normal window can render there.
* `Topmost` is heuristic: other topmost windows can temporarily out-rank it (mitigated by the `Deactivated` re-assert).
* The overlay captures mouse clicks inside its (small) text area; click-through is out of scope.
* Drag completion is detected after `DragMove()` returns; the position is saved at that moment, not continuously.
