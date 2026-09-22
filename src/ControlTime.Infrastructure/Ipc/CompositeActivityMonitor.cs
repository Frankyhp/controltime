using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Infrastructure.Windows;

namespace ControlTime.Infrastructure.Ipc;

public sealed class CompositeActivityMonitor : IActivityMonitor
{
    public static readonly TimeSpan DefaultReportFreshness = TimeSpan.FromSeconds(5);

    private readonly IActivityMonitor _fallback;
    private readonly ActivityReportSink _sink;
    private readonly Func<int?> _activeConsoleSessionId;
    private readonly TimeSpan _freshness;

    public CompositeActivityMonitor(
        IActivityMonitor fallback,
        ActivityReportSink sink,
        Func<int?>? activeConsoleSessionId = null,
        TimeSpan? freshness = null)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(sink);

        _fallback = fallback;
        _sink = sink;
        _activeConsoleSessionId = activeConsoleSessionId ?? WindowsConsoleSessionId.TryGet;
        _freshness = freshness ?? DefaultReportFreshness;

        if (_freshness <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(freshness));
        }
    }

    public ActivityStatus GetCurrentStatus()
    {
        ActivityStatus? report = TryGetConsoleReport();

        return report ?? _fallback.GetCurrentStatus();
    }

    public bool IsUsingReportedActivity() => TryGetConsoleReport() is not null;

    private ActivityStatus? TryGetConsoleReport()
    {
        int? consoleSessionId = _activeConsoleSessionId();

        return consoleSessionId is int sessionId
            ? _sink.TryGetFreshForSession(sessionId, _freshness)
            : null;
    }
}
