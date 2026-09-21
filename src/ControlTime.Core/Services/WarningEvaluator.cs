using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class WarningEvaluator
{
    private readonly IReadOnlyList<WarningThreshold> _thresholds;

    public WarningEvaluator(IReadOnlyList<WarningThreshold>? thresholds = null)
    {
        _thresholds = thresholds ?? WarningThreshold.Standard;

        if (_thresholds.Any(t => t.Remaining <= TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(thresholds));
        }
    }

    public IReadOnlyList<WarningThreshold> Thresholds => _thresholds;

    public WarningThreshold? Evaluate(DashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.State == AccessState.Blocked)
        {
            return null;
        }

        if (snapshot.RemainingTime is not TimeSpan remaining)
        {
            return null;
        }

        return _thresholds
            .Where(t => remaining <= t.Remaining)
            .OrderBy(t => t.Remaining)
            .FirstOrDefault();
    }
}
