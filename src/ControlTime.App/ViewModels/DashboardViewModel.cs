using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using ControlTime.App.Mvvm;
using ControlTime.App.Presentation;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");

    private readonly DashboardService _dashboardService;
    private readonly DispatcherTimer _refreshTimer;

    private bool _initialized;
    private bool _refreshing;
    private string _stateText = "—";
    private Brush _stateBrush = Brushes.Gray;
    private string _reasonText = string.Empty;
    private string _nextAllowedText = string.Empty;
    private string _usageText = "—";
    private string _limitText = "—";
    private string _bonusText = "0:00";
    private string _allowanceText = "—";
    private string _scheduleText = "—";
    private string _remainingText = "—";
    private string _errorMessage = string.Empty;

    public DashboardViewModel(DashboardService dashboardService, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dashboardService);

        _dashboardService = dashboardService;
        TimeProvider = timeProvider ?? TimeProvider.System;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    private TimeProvider TimeProvider { get; }

    public string StateText
    {
        get => _stateText;
        private set => SetField(ref _stateText, value);
    }

    public Brush StateBrush
    {
        get => _stateBrush;
        private set => SetField(ref _stateBrush, value);
    }

    public string ReasonText
    {
        get => _reasonText;
        private set => SetField(ref _reasonText, value);
    }

    public string NextAllowedText
    {
        get => _nextAllowedText;
        private set => SetField(ref _nextAllowedText, value);
    }

    public string UsageText
    {
        get => _usageText;
        private set => SetField(ref _usageText, value);
    }

    public string LimitText
    {
        get => _limitText;
        private set => SetField(ref _limitText, value);
    }

    public string BonusText
    {
        get => _bonusText;
        private set => SetField(ref _bonusText, value);
    }

    public string AllowanceText
    {
        get => _allowanceText;
        private set => SetField(ref _allowanceText, value);
    }

    public string ScheduleText
    {
        get => _scheduleText;
        private set => SetField(ref _scheduleText, value);
    }

    public string RemainingText
    {
        get => _remainingText;
        private set => SetField(ref _remainingText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public async Task InitializeAsync()
    {
        _refreshTimer.Start();

        if (_initialized)
        {
            return;
        }

        _initialized = true;

        await RefreshAsync();
    }

    public void Stop() => _refreshTimer.Stop();

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            var offset = TimeProvider.GetLocalNow().Offset;
            var snapshot = await _dashboardService.GetSnapshotAsync(offset);

            Apply(snapshot);
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = "No se pudo leer el estado: " + ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        (StateText, StateBrush) = snapshot.State switch
        {
            AccessState.Allowed => ("Permitido", new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20))),
            AccessState.Warning => ("Advertencia", new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00))),
            _ => ("Bloqueado", new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C))),
        };

        ReasonText = snapshot.State == AccessState.Blocked
            ? AccessReasonTranslator.Translate(snapshot.Reason)
            : snapshot.State == AccessState.Warning
                ? "Estás a punto de agotar el tiempo de uso de hoy."
                : string.Empty;

        NextAllowedText = snapshot.NextAllowedAt is DateTimeOffset next
            ? "Próximo acceso permitido: " +
              next.ToLocalTime().ToString("dddd d \\d\\e MMMM \\a \\l\\a\\s HH:mm", SpanishCulture)
            : string.Empty;

        LimitText = !snapshot.HasEnabledRules
            ? "—"
            : snapshot.ConfiguredDailyLimit is TimeSpan limit
                ? FormatHoursMinutes(limit)
                : "Sin límite";

        UsageText = FormatHoursMinutes(snapshot.UsageToday);

        BonusText = FormatHoursMinutes(snapshot.BonusToday);

        AllowanceText = !snapshot.HasEnabledRules
            ? "—"
            : snapshot.EffectiveDailyAllowance is TimeSpan allowance
                ? FormatHoursMinutes(allowance)
                : "Sin límite";

        ScheduleText = snapshot.ActiveSchedule is { } schedule
            ? $"{schedule.Start:HH\\:mm} – {schedule.End:HH\\:mm}"
            : "—";

        RemainingText = snapshot.RemainingTime is TimeSpan remaining
            ? FormatHoursMinutes(remaining)
            : "—";
    }

    private static string FormatHoursMinutes(TimeSpan time) =>
        $"{(int)time.TotalHours}:{time.Minutes:00}";
}
