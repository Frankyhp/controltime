# Phase 13 — Warnings and Notifications

## Goal

Warn the user before today's available time is exhausted, through Windows-visible notifications, without notification spam, plus a clear warning explanation on the Dashboard.

## Scope

In scope:

- Warning thresholds 15 / 5 / 1 minutes remaining, kept out of `RuleEngine`.
- Pure Core warning evaluation (`WarningThreshold`, `WarningEvaluator`).
- Core notification orchestrator with per-day deduplication (`WarningNotifier`) behind an `INotifier` abstraction.
- Windows balloon-tip notification implemented in the App (`NotifyIcon` + `ShowBalloonTip`, icon hidden; tray itself remains Phase 15).
- Explicit warning explanation on the Dashboard (remaining time of today's allowance).

Out of scope:

- Enforcement / session lock (Phase 14).
- System tray icon and window-to-tray hiding (Phase 15).
- User-configurable thresholds (Settings, Phase 16).
- Changes to `RuleEngine` warning threshold (stays at 5 minutes).

## Plan

1. Core — deterministic pieces:
   - `Domain/WarningThreshold`: record with `Remaining` (`TimeSpan`) and `Label`; standard set 15/5/1 min.
   - `Domain/UsageWarning`: record carrying the crossed threshold, `Remaining` and `EffectiveDailyAllowance` so presentation formatting stays outside Core decision logic.
   - `Abstractions/INotifier`: `Notify(UsageWarning)`; Core/App remain unaware of Windows toast details.
   - `Services/WarningEvaluator`: given a `DashboardSnapshot`, returns the most urgent threshold whose value is `>=` remaining time; `null` when remaining is `null` (unlimited) or the state is `Blocked`.
   - `Services/WarningNotifier`: consumes `MonitorState` ticks from `MonitoringHost`, tracks already-notified thresholds per local day (injected `TimeProvider`), resets at midnight, re-arms a threshold when remaining rises back above it (e.g., bonus grant), fires a single notification when several thresholds are crossed in one tick (the most urgent one; the skipped ones are marked as notified), logs notification failures without crashing the monitoring loop.
2. App — presentation and wiring:
   - `UseWindowsForms` in the App csproj; `Services/BalloonTipNotifier : INotifier, IDisposable` using `NotifyIcon.ShowBalloonTip` with `IconVisible = false`; formats the Spanish message (title + remaining vs effective allowance).
   - DI in `App.xaml.cs`: `INotifier`, `WarningEvaluator`, `WarningNotifier` (singleton); `WarningNotifier` subscribes to `MonitoringHost.StateChanged` and is instantiated in `OnStartup`.
   - Dashboard: warning-state explanation text now states remaining time explicitly.
3. Tests (xUnit, Core only, no WPF/Windows APIs):
   - `WarningEvaluatorTests`: boundaries (15:01 nothing, 15:00 → 15-min threshold, 4:59 → 5-min, 0 → 1-min), unlimited remaining → null, `Blocked` → null.
   - `WarningNotifierTests` with fake `TimeProvider` and fake `INotifier`: single fire per threshold per day, midnight reset, re-arm after bonus raises remaining, multiple thresholds crossed in one tick → one notification.
4. Verification: `dotnet test`, `dotnet build`, manual balloon observation.

## Implementation

- `ControlTime.Core` `WarningThreshold` (`Domain/`): record with `Remaining` and `Minutes`; standard set 15/5/1 min.
- `ControlTime.Core` `UsageWarning` (`Domain/`): crossed threshold, `Remaining` and `EffectiveDailyAllowance` for presentation formatting.
- `ControlTime.Core` `INotifier` (`Abstractions/`): `Notify(UsageWarning)`.
- `ControlTime.Core` `WarningEvaluator` (`Services/`): returns the most urgent crossed threshold from a `DashboardSnapshot`; `null` when remaining is unlimited or state is `Blocked`; rejects non-positive thresholds.
- `ControlTime.Core` `WarningNotifier` (`Services/`): per-local-day dedup via injected `TimeProvider`; re-arms thresholds whose value drops below remaining (bonus grants); marks skipped thresholds when several are crossed in one tick; cleared on unlimited allowance, preserved across `Blocked`, reset at midnight. Single-threaded by design (driven by the `Dispatcher`-dispatched `MonitoringHost.StateChanged`).
- `ControlTime.App` `BalloonTipNotifier` (`Services/`, `INotifier` + `IDisposable`): `NotifyIcon` with hidden icon + `ShowBalloonTip`, Spanish message with remaining vs effective allowance; App csproj enables `UseWindowsForms` and removes the `System.Windows.Forms`/`System.Drawing` implicit usings to avoid WPF name clashes.
- `ControlTime.App` `App.xaml.cs`: DI registrations (`INotifier`, `WarningEvaluator`, `WarningNotifier`); `WarningNotifier.Process` subscribed to `MonitoringHost.StateChanged` before `Start()`, wrapped in try/catch logged via `ILogger<WarningNotifier>` so a notification failure never breaks the UI update.
- `ControlTime.App` `DashboardViewModel`: warning-state reason now states remaining time explicitly ("Te quedan X del tiempo permitido de hoy.").
- `RuleEngine` untouched: its 5-minute threshold still drives `AccessState.Warning`.

## Tests

169 tests green (`dotnet test`), solution build clean (`dotnet build`).

- `WarningEvaluatorTests`: boundaries (15:01 none, 15:00 → 15, 14:59 → 15, 5:00 → 5, 4:59 → 5, 1:00 → 1, 0 → 1), unlimited → null, `Warning` state evaluated like `Allowed`, `Blocked` → null, non-positive threshold rejected.
- `WarningNotifierTests` (fake local `TimeProvider` + recording `INotifier`): single fire per threshold, one notification when multiple thresholds are crossed in one tick (skipped ones marked), midnight reset, re-arm after bonus raises remaining, `Blocked` neither notifies nor resets, unlimited allowance clears dedup, warning payload carries remaining and effective allowance.

## Manual validation

- Start the app with a rule whose daily limit leaves under 15/5/1 minutes remaining (optionally using "Regalar tiempo" to lower remaining relative to a new bonus): one balloon per threshold appears, titled "ControlTime — te quedan X de uso", without duplicates while remaining keeps decreasing.
- Balloons do not repeat for the same threshold later the same day; they appear again after midnight or after a grant pushes remaining back above the threshold.
- Dashboard shows the amber "Advertencia" state with text "Te quedan X del tiempo permitido de hoy."

## Decisions

- Notification mechanism: `NotifyIcon` balloon tips (no new NuGet package; native Windows 10/11 toasts). Rejected: `Microsoft.WindowsAppSDK`.
- Warning orchestration lives in Core Services behind `INotifier` (testable without WPF); the App only implements `INotifier`.
- `RuleEngine` keeps its own 5-minute warning threshold for `AccessState.Warning`; the 15/5/1 notification thresholds are independent of it, per the phase requirement that thresholds not be embedded into RuleEngine logic.

## Known limitations

- Balloon delivery depends on Windows notification settings (Focus assist / Do not disturb can suppress them silently).
- The tray icon stays hidden (`Visible = false`); balloon-only presence until Phase 15 introduces the tray.
- Notification strings are hardcoded in Spanish inside `BalloonTipNotifier`; localization is out of scope.
- Dedup state is in memory only: an app restart on the same day re-arms thresholds and may repeat a notification for an already-crossed threshold.
- A grant that raises remaining re-arms thresholds, so a grant below a still-crossed threshold does not re-notify (by design, avoids spam).
