using System.Windows.Threading;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace ControlTime.App.Services;

public sealed class MonitoringHost : IDisposable
{
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(1);

    private readonly MonitoringService _monitoringService;
    private readonly ILogger<MonitoringHost> _logger;
    private readonly TimeSpan _tickInterval;
    private readonly CancellationTokenSource _cancellation = new();

    private Dispatcher? _dispatcher;
    private Task? _loop;
    private bool _started;

    public MonitoringHost(
        MonitoringService monitoringService,
        ILogger<MonitoringHost> logger,
        TimeSpan? tickInterval = null)
    {
        ArgumentNullException.ThrowIfNull(monitoringService);
        ArgumentNullException.ThrowIfNull(logger);

        _monitoringService = monitoringService;
        _logger = logger;
        _tickInterval = tickInterval ?? DefaultTickInterval;
    }

    public event Action<MonitorState>? StateChanged;

    public event Action<Exception>? TickFailed;

    public MonitorState? LastState { get; private set; }

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
        using PeriodicTimer timer = new(_tickInterval);

        await TickOnceAsync();

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

            await TickOnceAsync();
        }
    }

    private async Task TickOnceAsync()
    {
        try
        {
            MonitorState state = await _monitoringService.TickAsync(
                DateTimeOffset.Now.Offset,
                _cancellation.Token);

            LastState = state;

            _dispatcher?.BeginInvoke(() => StateChanged?.Invoke(state));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "El monitoreo en segundo plano no pudo evaluar el estado.");

            _dispatcher?.BeginInvoke(() => TickFailed?.Invoke(ex));
        }
    }
}
