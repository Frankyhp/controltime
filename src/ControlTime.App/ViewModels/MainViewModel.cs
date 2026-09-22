using ControlTime.App.Mvvm;

namespace ControlTime.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly RulesViewModel _rules;
    private readonly UsageHistoryViewModel _history;
    private readonly SettingsViewModel _settings;
    private object _current;

    public MainViewModel(
        DashboardViewModel dashboard,
        RulesViewModel rules,
        UsageHistoryViewModel history,
        SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(settings);

        _dashboard = dashboard;
        _rules = rules;
        _history = history;
        _settings = settings;
        _current = dashboard;

        ShowDashboardCommand = new RelayCommand(() => Current = _dashboard);
        ShowRulesCommand = new RelayCommand(() => Current = _rules);
        ShowHistoryCommand = new RelayCommand(() => Current = _history);
        ShowSettingsCommand = new RelayCommand(() => Current = _settings);
    }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowRulesCommand { get; }
    public RelayCommand ShowHistoryCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    public object Current
    {
        get => _current;
        private set
        {
            if (SetField(ref _current, value))
            {
                OnPropertyChanged(nameof(IsDashboardSelected));
                OnPropertyChanged(nameof(IsRulesSelected));
                OnPropertyChanged(nameof(IsHistorySelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsDashboardSelected => Current is DashboardViewModel;

    public bool IsRulesSelected => Current is RulesViewModel;

    public bool IsHistorySelected => Current is UsageHistoryViewModel;

    public bool IsSettingsSelected => Current is SettingsViewModel;
}
