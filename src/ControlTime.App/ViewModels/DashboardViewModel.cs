using System.Globalization;
using System.Windows.Media;
using ControlTime.App.Mvvm;
using ControlTime.App.Presentation;
using ControlTime.App.Services;
using ControlTime.Core.Domain;

namespace ControlTime.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");

    private readonly ServiceClientHost _serviceClientHost;

    private bool _subscribed;
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
    private string _activityText = "—";
    private Brush _activityBrush = Brushes.Gray;
    private string _errorMessage = string.Empty;

    public DashboardViewModel(ServiceClientHost serviceClientHost)
    {
        ArgumentNullException.ThrowIfNull(serviceClientHost);

        _serviceClientHost = serviceClientHost;
    }

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

    public string ActivityText
    {
        get => _activityText;
        private set => SetField(ref _activityText, value);
    }

    public Brush ActivityBrush
    {
        get => _activityBrush;
        private set => SetField(ref _activityBrush, value);
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

    public void Initialize()
    {
        if (!_subscribed)
        {
            _serviceClientHost.StateChanged += OnStateChanged;
            _serviceClientHost.TickFailed += OnTickFailed;
            _serviceClientHost.AvailabilityChanged += OnAvailabilityChanged;
            _subscribed = true;
        }

        if (_serviceClientHost.LastState is { } state)
        {
            Apply(state);
        }

        if (!_serviceClientHost.ServiceAvailable)
        {
            ErrorMessage = "El servicio de ControlTime no está disponible. " +
                "Inicie el servicio ControlTimeService para controlar el uso del equipo.";
        }
    }

    public void Stop()
    {
        if (!_subscribed)
        {
            return;
        }

        _serviceClientHost.StateChanged -= OnStateChanged;
        _serviceClientHost.TickFailed -= OnTickFailed;
        _serviceClientHost.AvailabilityChanged -= OnAvailabilityChanged;
        _subscribed = false;
    }

    private void OnStateChanged(MonitorState state)
    {
        Apply(state);
        ErrorMessage = string.Empty;
    }

    private void OnTickFailed(Exception exception)
    {
        ErrorMessage = "No se pudo leer el estado: " + exception.Message;
    }

    private void OnAvailabilityChanged(bool available)
    {
        ErrorMessage = available
            ? string.Empty
            : "El servicio de ControlTime no está disponible. " +
              "Inicie el servicio ControlTimeService para controlar el uso del equipo.";
    }

    private void Apply(MonitorState state)
    {
        ApplyActivity(state.Activity);
        ApplySnapshot(state.Snapshot);
    }

    private void ApplyActivity(ActivityStatus status)
    {
        ActivityText = ActivityStateTranslator.Translate(status.State);
        ActivityBrush = status.State switch
        {
            ActivityState.Active => new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20)),
            ActivityState.Idle => new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x80)),
        };
    }

    private void ApplySnapshot(DashboardSnapshot snapshot)
    {
        (StateText, StateBrush) = snapshot.State switch
        {
            AccessState.Allowed => ("Permitido", new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20))),
            AccessState.Warning => ("Advertencia", new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00))),
            _ => ("Bloqueado", new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C))),
        };

        ReasonText = snapshot.State switch
        {
            AccessState.Blocked => AccessReasonTranslator.Translate(snapshot.Reason),
            AccessState.Warning when snapshot.RemainingTime is TimeSpan warningRemaining =>
                $"Te quedan {FormatHoursMinutes(warningRemaining)} del tiempo permitido de hoy.",
            AccessState.Warning => "Estás a punto de agotar el tiempo de uso de hoy.",
            _ => string.Empty,
        };

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
