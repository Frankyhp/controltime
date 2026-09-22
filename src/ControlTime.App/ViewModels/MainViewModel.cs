using ControlTime.App.Mvvm;

namespace ControlTime.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly RulesViewModel _rules;
    private readonly SettingsViewModel _settings;
    private object _current;

    public MainViewModel(DashboardViewModel dashboard, RulesViewModel rules, SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(settings);

        _dashboard = dashboard;
        _rules = rules;
        _settings = settings;
        _current = dashboard;

        ShowDashboardCommand = new RelayCommand(() => Current = _dashboard);
        ShowRulesCommand = new RelayCommand(() => Current = _rules);
        ShowSettingsCommand = new RelayCommand(() => Current = _settings);
    }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowRulesCommand { get; }
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
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsDashboardSelected => Current is DashboardViewModel;

    public bool IsRulesSelected => Current is RulesViewModel;

    public bool IsSettingsSelected => Current is SettingsViewModel;
}
