using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Infrastructure.Ipc;

public sealed class CompositeActivityMonitor : IActivityMonitor
{
    public static readonly TimeSpan DefaultReportFreshness = TimeSpan.FromSeconds(5);

    private readonly IActivityMonitor _fallback;
    private readonly ActivityReportSink _sink;
    private readonly TimeSpan _freshness;

    public CompositeActivityMonitor(
        IActivityMonitor fallback,
        ActivityReportSink sink,
        TimeSpan? freshness = null)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(sink);

        _fallback = fallback;
        _sink = sink;
        _freshness = freshness ?? DefaultReportFreshness;

        if (_freshness <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(freshness));
        }
    }

    public ActivityStatus GetCurrentStatus() =>
        _sink.TryGetFresh(_freshness) ?? _fallback.GetCurrentStatus();
}
