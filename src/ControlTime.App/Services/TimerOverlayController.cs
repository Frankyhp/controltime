using System.Globalization;
using System.Windows;
using ControlTime.App.Views;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using Microsoft.Extensions.Logging;

namespace ControlTime.App.Services;

public sealed class TimerOverlayController : IDisposable
{
    public const string VisibleSettingKey = "TimerOverlayVisible";
    public const string LeftSettingKey = "TimerOverlayLeft";
    public const string TopSettingKey = "TimerOverlayTop";

    private const double ScreenMargin = 24;

    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");

    private readonly TimerOverlayWindow _window;
    private readonly ServiceClientHost _serviceClientHost;
    private readonly TrayIconService _trayIcon;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ILogger<TimerOverlayController> _logger;

    private bool _userWantsVisible;
    private bool _disposed;
    private Task? _pendingSave;
    private double? _storedLeft;
    private double? _storedTop;

    public TimerOverlayController(
        TimerOverlayWindow window,
        ServiceClientHost serviceClientHost,
        TrayIconService trayIcon,
        IAppSettingsStore appSettingsStore,
        ILogger<TimerOverlayController> logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(serviceClientHost);
        ArgumentNullException.ThrowIfNull(trayIcon);
        ArgumentNullException.ThrowIfNull(appSettingsStore);
        ArgumentNullException.ThrowIfNull(logger);

        _window = window;
        _serviceClientHost = serviceClientHost;
        _trayIcon = trayIcon;
        _appSettingsStore = appSettingsStore;
        _logger = logger;

        _window.DragCompleted += OnWindowDragCompleted;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _serviceClientHost.StateChanged += OnStateChanged;
        _serviceClientHost.AvailabilityChanged += OnAvailabilityChanged;

        string? stored = await _appSettingsStore.GetAsync(VisibleSettingKey, cancellationToken);

        _userWantsVisible = string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase);
        _trayIcon.SetTimerOverlayPreference(_userWantsVisible);

        (string? leftValue, string? topValue) = await ReadStoredPositionValuesAsync(cancellationToken);

        if (double.TryParse(leftValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double left)
            && double.TryParse(topValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
        {
            _storedLeft = left;
            _storedTop = top;
        }

        Apply(_serviceClientHost.LastState, _serviceClientHost.ServiceAvailable);
    }

    public async Task ToggleAsync()
    {
        if (_disposed)
        {
            return;
        }

        _userWantsVisible = !_userWantsVisible;

        _trayIcon.SetTimerOverlayPreference(_userWantsVisible);

        await SaveSettingAsync(VisibleSettingKey, _userWantsVisible ? "true" : "false");

        Apply(_serviceClientHost.LastState, _serviceClientHost.ServiceAvailable);
    }

    public void Shutdown()
    {
        SavePositionIfMoved();

        try
        {
            _pendingSave?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _serviceClientHost.StateChanged -= OnStateChanged;
        _serviceClientHost.AvailabilityChanged -= OnAvailabilityChanged;

        if (_window.IsVisible)
        {
            _window.Hide();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.DragCompleted -= OnWindowDragCompleted;
    }

    private void OnStateChanged(MonitorState state) => Apply(state, _serviceClientHost.ServiceAvailable);

    private void OnAvailabilityChanged(bool available) => Apply(_serviceClientHost.LastState, available);

    private void Apply(MonitorState? state, bool serviceAvailable)
    {
        if (_disposed)
        {
            return;
        }

        _window.SetRemainingText(FormatRemaining(state?.Snapshot));

        // El overlay solo se muestra con el Servicio disponible: sin él no
        // hay datos fiables que presentar.
        bool shouldShow = _userWantsVisible && serviceAvailable;

        if (shouldShow && !_window.IsVisible)
        {
            ShowWindow();
        }
        else if (!shouldShow && _window.IsVisible)
        {
            SavePositionIfMoved();
            _window.Hide();
        }
    }

    private void ShowWindow()
    {
        bool hasStoredPosition = _storedLeft is not null && _storedTop is not null;

        if (hasStoredPosition)
        {
            _window.Left = _storedLeft!.Value;
            _window.Top = _storedTop!.Value;
        }

        _window.Show();

        // El tamaño real no se conoce hasta el primer layout (SizeToContent):
        // se reposiciona/ajusta después de renderizar.
        _window.Dispatcher.BeginInvoke(
            () =>
            {
                if (!hasStoredPosition)
                {
                    Rect workArea = SystemParameters.WorkArea;

                    _window.Left = workArea.Right - _window.ActualWidth - ScreenMargin;
                    _window.Top = workArea.Top + ScreenMargin;
                }

                ClampToVirtualScreen();
            },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ClampToVirtualScreen()
    {
        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        _window.Left = Math.Clamp(_window.Left, virtualLeft, Math.Max(virtualLeft, virtualRight - _window.ActualWidth));
        _window.Top = Math.Clamp(_window.Top, virtualTop, Math.Max(virtualTop, virtualBottom - _window.ActualHeight));
    }

    private async Task<(string? Left, string? Top)> ReadStoredPositionValuesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            string? left = await _appSettingsStore.GetAsync(LeftSettingKey, cancellationToken);
            string? top = await _appSettingsStore.GetAsync(TopSettingKey, cancellationToken);

            return (left, top);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer la posición guardada del timer en pantalla.");

            return (null, null);
        }
    }

    private void OnWindowDragCompleted() => SavePosition();

    private void SavePositionIfMoved()
    {
        if (_window.MovedSinceLastSave)
        {
            SavePosition();
        }
    }

    private void SavePosition()
    {
        if (_pendingSave is { IsCompleted: false })
        {
            return;
        }

        _pendingSave = SavePositionAsync();
    }

    private async Task SavePositionAsync()
    {
        try
        {
            await SaveSettingAsync(LeftSettingKey, _window.Left.ToString(CultureInfo.InvariantCulture));
            await SaveSettingAsync(TopSettingKey, _window.Top.ToString(CultureInfo.InvariantCulture));

            _window.MarkPositionSaved();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar la posición del timer en pantalla.");
        }
        finally
        {
            if (_window.MovedSinceLastSave)
            {
                SavePosition();
            }
        }
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        try
        {
            await _appSettingsStore.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar el ajuste del timer en pantalla '{Key}'.", key);
        }
    }

    private static string FormatRemaining(DashboardSnapshot? snapshot) =>
        snapshot?.RemainingTime is TimeSpan remaining
            ? string.Format(SpanishCulture, "{0}:{1:00}", (int)remaining.TotalHours, remaining.Minutes)
            : "—";
}
