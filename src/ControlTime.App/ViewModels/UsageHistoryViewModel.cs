using System.Collections.ObjectModel;
using System.Globalization;
using ControlTime.App.Mvvm;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.App.ViewModels;

public sealed class UsageHistoryViewModel : ObservableObject
{
    public const int VisibleDays = 14;

    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");

    private readonly UsageHistoryService _historyService;

    private IReadOnlyList<UsageSession> _sessions = [];
    private TimeSpan _utcOffset;
    private UsageDayItem? _selectedDay;
    private string _weekUsageText = "—";
    private string _errorMessage = string.Empty;

    public UsageHistoryViewModel(UsageHistoryService historyService)
    {
        ArgumentNullException.ThrowIfNull(historyService);

        _historyService = historyService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<UsageDayItem> Days { get; } = [];

    public ObservableCollection<UsageSessionItem> SelectedDaySessions { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public string WeekUsageText
    {
        get => _weekUsageText;
        private set => SetField(ref _weekUsageText, value);
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

    public UsageDayItem? SelectedDay
    {
        get => _selectedDay;
        set
        {
            if (SetField(ref _selectedDay, value))
            {
                PopulateSelectedDaySessions();
            }
        }
    }

    public Task InitializeAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        ErrorMessage = string.Empty;

        try
        {
            _utcOffset = DateTimeOffset.Now.Offset;

            UsageHistory history = await _historyService.GetHistoryAsync(VisibleDays, _utcOffset);

            _sessions = history.Sessions;

            DateOnly today = DateOnly.FromDateTime(history.AsOf.ToOffset(_utcOffset).DateTime);

            Days.Clear();

            foreach (UsageHistoryEntry entry in history.Days)
            {
                Days.Add(new UsageDayItem(
                    entry.Date,
                    LabelFor(entry.Date, today),
                    FormatTime(entry.Usage),
                    FormatTime(entry.BonusGranted),
                    entry.BonusGranted > TimeSpan.Zero));
            }

            WeekUsageText = FormatTime(history.CurrentWeekUsage);

            SelectedDay = Days.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = "No se pudo cargar el historial de uso: " + ex.Message;
        }
    }

    private void PopulateSelectedDaySessions()
    {
        SelectedDaySessions.Clear();

        if (_selectedDay is not { } day)
        {
            return;
        }

        DateTimeOffset dayStart = new(day.Date.ToDateTime(TimeOnly.MinValue), _utcOffset);
        DateTimeOffset dayEnd = dayStart.AddDays(1);

        foreach (UsageSession session in _sessions
                     .Where(s => s.StartedAt < dayEnd && (s.EndedAt is null || s.EndedAt > dayStart))
                     .OrderBy(s => s.StartedAt))
        {
            SelectedDaySessions.Add(UsageSessionItem.Create(session));
        }
    }

    private static string LabelFor(DateOnly date, DateOnly today) => date switch
    {
        _ when date == today => "Hoy",
        _ when date == today.AddDays(-1) => "Ayer",
        _ => date.ToString("dddd, d 'de' MMMM", SpanishCulture),
    };

    private static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalHours}:{time.Minutes:00}";
}

public sealed class UsageDayItem(
    DateOnly date,
    string label,
    string usageText,
    string bonusText,
    bool hasBonus)
{
    public DateOnly Date { get; } = date;

    public string Label { get; } = label;

    public string UsageText { get; } = usageText;

    public string BonusText { get; } = bonusText;

    public bool HasBonus { get; } = hasBonus;
}

public sealed class UsageSessionItem(string periodText, string durationText)
{
    public string PeriodText { get; } = periodText;

    public string DurationText { get; } = durationText;

    public static UsageSessionItem Create(UsageSession session)
    {
        string start = session.StartedAt.ToLocalTime().ToString("HH:mm");

        if (session.EndedAt is DateTimeOffset endedAt)
        {
            return new UsageSessionItem(
                $"{start} – {endedAt.ToLocalTime():HH:mm}",
                $"{(int)session.Duration!.Value.TotalHours}:{session.Duration.Value.Minutes:00}");
        }

        return new UsageSessionItem($"{start} – en curso", "—");
    }
}
