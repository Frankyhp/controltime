using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Forms;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using Microsoft.Extensions.Logging;

namespace ControlTime.App.Services;

public sealed class TrayIconService : INotifier, IDisposable
{
    public const string AutoStartSettingKey = "AutoStartEnabled";

    private const int BalloonTimeoutMilliseconds = 10_000;
    private const int TooltipMaxLength = 63;

    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");

    private readonly IAutostartRegistrar _autostartRegistrar;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly MainWindow _mainWindow;
    private readonly ILogger<TrayIconService> _logger;

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _stateItem = new("Estado: —") { Enabled = false };
    private readonly ToolStripMenuItem _remainingItem = new("Tiempo restante: —") { Enabled = false };
    private readonly ToolStripMenuItem _autoStartItem = new("Iniciar con Windows");

    private bool _disposed;

    public TrayIconService(
        IAutostartRegistrar autostartRegistrar,
        IAppSettingsStore appSettingsStore,
        MainWindow mainWindow,
        ILogger<TrayIconService> logger)
    {
        ArgumentNullException.ThrowIfNull(autostartRegistrar);
        ArgumentNullException.ThrowIfNull(appSettingsStore);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(logger);

        _autostartRegistrar = autostartRegistrar;
        _appSettingsStore = appSettingsStore;
        _mainWindow = mainWindow;
        _logger = logger;

        var menu = new ContextMenuStrip();

        ToolStripMenuItem openItem = new("Abrir ControlTime");
        openItem.Click += (_, _) => ShowMainWindow();

        ToolStripMenuItem exitItem = new("Salir de ControlTime");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _autoStartItem.Click += OnToggleAutoStartClick;

        _autoStartItem.Checked = SafeIsAutoStartEnabled();

        menu.Items.AddRange([
            openItem,
            new ToolStripSeparator(),
            _stateItem,
            _remainingItem,
            new ToolStripSeparator(),
            _autoStartItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ControlTime",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public event Action? ExitRequested;

    public void UpdateState(MonitorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_disposed)
        {
            return;
        }

        DashboardSnapshot snapshot = state.Snapshot;

        string stateText = snapshot.State switch
        {
            AccessState.Allowed => "Permitido",
            AccessState.Warning => "Advertencia",
            _ => "Bloqueado",
        };

        string remainingText = snapshot.RemainingTime is TimeSpan remaining
            ? FormatHoursMinutes(remaining)
            : "—";

        _stateItem.Text = "Estado: " + stateText;
        _remainingItem.Text = "Tiempo restante: " + remainingText;
        _notifyIcon.Text = Truncate($"ControlTime — {stateText}, quedan {remainingText}");
    }

    public async Task SyncAutoStartFromSettingsAsync(CancellationToken cancellationToken = default)
    {
        string? stored = await _appSettingsStore.GetAsync(AutoStartSettingKey, cancellationToken);

        bool desired = string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (desired && !_autostartRegistrar.IsEnabled())
            {
                _autostartRegistrar.Enable();
            }
            else if (!desired && _autostartRegistrar.IsEnabled())
            {
                _autostartRegistrar.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo sincronizar el arranque automático con Windows.");
        }

        _autoStartItem.Checked = SafeIsAutoStartEnabled();
    }

    public void Notify(UsageWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        if (_disposed)
        {
            return;
        }

        string remaining = FormatHoursMinutes(warning.Remaining);

        string title = warning.Threshold.Minutes switch
        {
            >= 60 => "ControlTime — tiempo de uso casi agotado",
            _ => $"ControlTime — te quedan {remaining} de uso",
        };

        string message = warning.EffectiveDailyAllowance is TimeSpan allowance
            ? $"Solo te quedan {remaining} del tiempo permitido de hoy ({FormatHoursMinutes(allowance)})."
            : $"Solo te quedan {remaining} de tu tiempo de uso.";

        _notifyIcon.ShowBalloonTip(BalloonTimeoutMilliseconds, title, message, ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Dispose();
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private async void OnToggleAutoStartClick(object? sender, EventArgs e)
    {
        bool desired = !_autoStartItem.Checked;

        try
        {
            if (desired)
            {
                _autostartRegistrar.Enable();
            }
            else
            {
                _autostartRegistrar.Disable();
            }

            await _appSettingsStore.SetAsync(AutoStartSettingKey, desired ? "true" : "false");

            _autoStartItem.Checked = desired;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo actualizar el arranque automático.");
            _autoStartItem.Checked = SafeIsAutoStartEnabled();
        }
    }

    private bool SafeIsAutoStartEnabled()
    {
        try
        {
            return _autostartRegistrar.IsEnabled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer el arranque automático de Windows.");
            return false;
        }
    }

    private static string FormatHoursMinutes(TimeSpan time) =>
        string.Format(
            SpanishCulture,
            "{0}:{1:00}",
            (int)time.TotalHours,
            time.Minutes);

    private static string Truncate(string text) =>
        text.Length <= TooltipMaxLength ? text : text[..TooltipMaxLength];
}
