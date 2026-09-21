using System.Windows.Threading;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace ControlTime.App.Services;

public sealed class UsageTrackingHost : IDisposable
{
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(5);

    private readonly IActivityMonitor _activityMonitor;
    private readonly UsageTrackingService _trackingService;
    private readonly ILogger<UsageTrackingHost> _logger;
    private readonly DispatcherTimer _timer;

    private bool _started;

    public UsageTrackingHost(
        IActivityMonitor activityMonitor,
        UsageTrackingService trackingService,
        ILogger<UsageTrackingHost> logger,
        TimeSpan? tickInterval = null)
    {
        ArgumentNullException.ThrowIfNull(activityMonitor);
        ArgumentNullException.ThrowIfNull(trackingService);
        ArgumentNullException.ThrowIfNull(logger);

        _activityMonitor = activityMonitor;
        _trackingService = trackingService;
        _logger = logger;

        _timer = new DispatcherTimer
        {
            Interval = tickInterval ?? DefaultTickInterval,
        };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _timer.Start();
        _ = TickAsync();
    }

    public void Dispose() => _timer.Stop();

    private async Task TickAsync()
    {
        try
        {
            await _trackingService.TickAsync(_activityMonitor.GetCurrentStatus());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "El seguimiento de uso no pudo registrar el estado de actividad.");
        }
    }
}
