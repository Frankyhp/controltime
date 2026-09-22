using System.Windows.Threading;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Contracts;
using ControlTime.Core.Domain;
using ControlTime.Infrastructure.Ipc;
using Microsoft.Extensions.Logging;

namespace ControlTime.App.Services;

public sealed class ServiceClientHost : IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly IControlTimeGateway _gateway;
    private readonly GatewayAdminAuthService _adminAuthService;
    private readonly IActivityMonitor _activityMonitor;
    private readonly ILogger<ServiceClientHost> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cancellation = new();

    private Dispatcher? _dispatcher;
    private Task? _loop;
    private bool _started;
    private bool? _serviceAvailable;

    public ServiceClientHost(
        IControlTimeGateway gateway,
        GatewayAdminAuthService adminAuthService,
        IActivityMonitor activityMonitor,
        ILogger<ServiceClientHost> logger,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(adminAuthService);
        ArgumentNullException.ThrowIfNull(activityMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _gateway = gateway;
        _adminAuthService = adminAuthService;
        _activityMonitor = activityMonitor;
        _logger = logger;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public event Action<MonitorState>? StateChanged;

    public event Action<Exception>? TickFailed;

    public event Action<bool>? AvailabilityChanged;

    public MonitorState? LastState { get; private set; }

    public bool ServiceAvailable => _serviceAvailable ?? false;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _loop = Task.Run(RunLoopAsync);
    }

    public void Dispose()
    {
        _cancellation.Cancel();

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cancellation.Dispose();
    }

    private async Task RunLoopAsync()
    {
        using PeriodicTimer timer = new(_pollInterval);

        await PollOnceAsync();

        while (true)
        {
            bool signaled;

            try
            {
                signaled = await timer.WaitForNextTickAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!signaled)
            {
                return;
            }

            await PollOnceAsync();
        }
    }

    private async Task PollOnceAsync()
    {
        try
        {
            ActivityStatus activity = _activityMonitor.GetCurrentStatus();

            await _gateway.ReportActivityAsync(activity, _cancellation.Token);

            ServiceStatus? status = await _gateway.GetStatusAsync(_cancellation.Token);

            if (status is null)
            {
                OnUnavailable();
                return;
            }

            MonitorState state = status.ToMonitorState();

            _adminAuthService.UpdateStatus(status);
            LastState = state;

            if (_serviceAvailable != true)
            {
                _serviceAvailable = true;
                _dispatcher?.BeginInvoke(() => AvailabilityChanged?.Invoke(true));
            }

            _dispatcher?.BeginInvoke(() => StateChanged?.Invoke(state));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo consultar el estado del servicio de ControlTime.");

            OnUnavailable();

            _dispatcher?.BeginInvoke(() => TickFailed?.Invoke(ex));
        }
    }

    private void OnUnavailable()
    {
        if (_serviceAvailable == false)
        {
            return;
        }

        _serviceAvailable = false;
        _dispatcher?.BeginInvoke(() => AvailabilityChanged?.Invoke(false));
    }
}
