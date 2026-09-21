using ControlTime.App.Mvvm;

namespace ControlTime.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly RulesViewModel _rules;
    private object _current;

    public MainViewModel(DashboardViewModel dashboard, RulesViewModel rules)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(rules);

        _dashboard = dashboard;
        _rules = rules;
        _current = dashboard;

        ShowDashboardCommand = new RelayCommand(() => Current = _dashboard);
        ShowRulesCommand = new RelayCommand(() => Current = _rules);
    }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowRulesCommand { get; }

    public object Current
    {
        get => _current;
        private set
        {
            if (SetField(ref _current, value))
            {
                OnPropertyChanged(nameof(IsDashboardSelected));
                OnPropertyChanged(nameof(IsRulesSelected));
            }
        }
    }

    public bool IsDashboardSelected => Current is DashboardViewModel;

    public bool IsRulesSelected => Current is RulesViewModel;
}
