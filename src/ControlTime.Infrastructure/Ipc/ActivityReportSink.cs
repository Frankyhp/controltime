using ControlTime.Core.Domain;

namespace ControlTime.Infrastructure.Ipc;

public sealed class ActivityReportSink(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private ActivityStatus? _lastReport;
    private DateTimeOffset _receivedAt;

    public void Report(ActivityStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        _lastReport = status;
        _receivedAt = _timeProvider.GetUtcNow();
    }

    public ActivityStatus? TryGetFresh(TimeSpan maxAge)
    {
        ActivityStatus? report = _lastReport;

        if (report is null)
        {
            return null;
        }

        return _timeProvider.GetUtcNow() - _receivedAt <= maxAge
            ? report
            : null;
    }

    public bool HasFreshReport(TimeSpan maxAge) => TryGetFresh(maxAge) is not null;
}
