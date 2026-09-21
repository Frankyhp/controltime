using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class WarningNotifier
{
    private readonly WarningEvaluator _evaluator;
    private readonly INotifier _notifier;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<TimeSpan> _notifiedThresholds = [];

    private DateOnly? _notifiedDay;

    public WarningNotifier(
        WarningEvaluator evaluator,
        INotifier notifier,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(notifier);

        _evaluator = evaluator;
        _notifier = notifier;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Process(MonitorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ResetIfNewDay();

        DashboardSnapshot snapshot = state.Snapshot;

        if (snapshot.RemainingTime is not TimeSpan remaining)
        {
            if (snapshot.State != AccessState.Blocked)
            {
                _notifiedThresholds.Clear();
            }

            return;
        }

        _notifiedThresholds.RemoveWhere(threshold => threshold < remaining);

        WarningThreshold? threshold = _evaluator.Evaluate(snapshot);

        if (threshold is null || !_notifiedThresholds.Add(threshold.Remaining))
        {
            return;
        }

        foreach (WarningThreshold crossed in _evaluator.Thresholds
                     .Where(t => remaining <= t.Remaining))
        {
            _notifiedThresholds.Add(crossed.Remaining);
        }

        _notifier.Notify(new UsageWarning(
            threshold,
            remaining,
            snapshot.EffectiveDailyAllowance));
    }

    private void ResetIfNewDay()
    {
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        if (today != _notifiedDay)
        {
            _notifiedDay = today;
            _notifiedThresholds.Clear();
        }
    }
}
