using ControlTime.Core.Domain;

namespace ControlTime.Infrastructure.Ipc;

public sealed class ActivityReportSink(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<int, (ActivityStatus Status, DateTimeOffset ReceivedAt)> _reports = [];

    public void Report(int sessionId, ActivityStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            _reports[sessionId] = (status, _timeProvider.GetUtcNow());
        }
    }

    public ActivityStatus? TryGetFreshForSession(int sessionId, TimeSpan maxAge)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_reports.TryGetValue(sessionId, out (ActivityStatus Status, DateTimeOffset ReceivedAt) report))
            {
                return null;
            }

            return now - report.ReceivedAt <= maxAge
                ? report.Status
                : null;
        }
    }

    public bool HasFreshReportForSession(int sessionId, TimeSpan maxAge) =>
        TryGetFreshForSession(sessionId, maxAge) is not null;
}
