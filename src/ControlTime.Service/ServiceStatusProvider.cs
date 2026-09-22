using ControlTime.Core.Abstractions;
using ControlTime.Core.Contracts;
using ControlTime.Core.Services;
using ControlTime.Infrastructure.Ipc;

namespace ControlTime.Service;

public sealed class ServiceStatusProvider(
    MonitoringLoop monitoringLoop,
    IAdminAuthService adminAuthService,
    CompositeActivityMonitor activityMonitor,
    bool enforcementEnabled)
{
    public async Task<ServiceStatus?> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (monitoringLoop.LastState is not { } state)
        {
            return null;
        }

        bool hasPassword = await adminAuthService.HasPasswordAsync(cancellationToken);

        return state.ToServiceStatus(
            adminAuthService.IsAdminMode,
            hasPassword,
            enforcementEnabled,
            activityMonitor.IsUsingReportedActivity());
    }
}
