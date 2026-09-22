using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class MonitoringService(
    IActivityMonitor activityMonitor,
    DashboardService dashboardService,
    UsageTrackingService? trackingService = null)
{
    private readonly IActivityMonitor _activityMonitor = activityMonitor
        ?? throw new ArgumentNullException(nameof(activityMonitor));
    private readonly UsageTrackingService? _trackingService = trackingService;
    private readonly DashboardService _dashboardService = dashboardService
        ?? throw new ArgumentNullException(nameof(dashboardService));

    public async Task<MonitorState> TickAsync(
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        ActivityStatus status = _activityMonitor.GetCurrentStatus();

        if (_trackingService is not null)
        {
            await _trackingService.TickAsync(status, cancellationToken);
        }

        DashboardSnapshot snapshot = await _dashboardService.GetSnapshotAsync(
            utcOffset,
            cancellationToken);

        return new MonitorState(status, snapshot);
    }
}
