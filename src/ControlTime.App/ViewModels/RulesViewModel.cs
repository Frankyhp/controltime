using System.Collections.ObjectModel;
using System.Windows.Threading;
using ControlTime.App.Mvvm;
using ControlTime.App.Presentation;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.App.ViewModels;

public sealed class RulesViewModel : ObservableObject
{
    private readonly IUsageRuleStore _store;
    private readonly ITimeGrantStore _grantStore;
    private readonly IAdminAuthService _adminAuthService;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _adminSessionTimer;
    private readonly List<Action> _raiseEditCommandCanExecute = [];

    private bool _initialized;
    private bool _showSetupPanel;
    private bool _showUnlockPanel;
    private bool _showAdminBar;
    private bool _showGrantPanel;
    private bool _isAdminMode;
    private bool _isConfirmingDelete;
    private bool _isNewRule = true;
    private RuleListItem? _selectedRule;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    private string _ruleName = string.Empty;
    private bool _ruleEnabled = true;
    private bool _mondayAllowed;
    private bool _tuesdayAllowed;
    private bool _wednesdayAllowed;
    private bool _thursdayAllowed;
    private bool _fridayAllowed;
    private bool _saturdayAllowed;
    private bool _sundayAllowed;
    private string _startTimeText = "09:00";
    private string _endTimeText = "18:00";
    private bool _unlimitedDailyLimit;
    private string _dailyLimitMinutesText = "120";
    private string _grantMinutesText = "30";

    public RulesViewModel(
        IUsageRuleStore store,
        ITimeGrantStore grantStore,
        IAdminAuthService adminAuthService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(grantStore);
        ArgumentNullException.ThrowIfNull(adminAuthService);

        _store = store;
        _grantStore = grantStore;
        _adminAuthService = adminAuthService;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _adminSessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _adminSessionTimer.Tick += (_, _) => RefreshAdminState();

        NewRuleCommand = new RelayCommand(NewRule, () => CanEdit);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanEdit && SelectedRule is not null);
        CancelDeleteCommand = new RelayCommand(CancelDelete, () => IsConfirmingDelete);
        LockAdminCommand = new RelayCommand(LockAdmin, () => IsAdminMode);
        OpenGrantCommand = new RelayCommand(OpenGrantPanel);
        CancelGrantCommand = new RelayCommand(CancelGrantPanel, () => ShowGrantPanel);

        _raiseEditCommandCanExecute.Add(NewRuleCommand.RaiseCanExecuteChanged);
        _raiseEditCommandCanExecute.Add(SaveCommand.RaiseCanExecuteChanged);
        _raiseEditCommandCanExecute.Add(DeleteCommand.RaiseCanExecuteChanged);
        _raiseEditCommandCanExecute.Add(CancelDeleteCommand.RaiseCanExecuteChanged);
    }

    public ObservableCollection<RuleListItem> Rules { get; } = [];

    public RelayCommand NewRuleCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public RelayCommand LockAdminCommand { get; }
    public RelayCommand OpenGrantCommand { get; }
    public RelayCommand CancelGrantCommand { get; }

    public bool ShowSetupPanel
    {
        get => _showSetupPanel;
        private set
        {
            if (SetField(ref _showSetupPanel, value))
            {
                OnPropertyChanged(nameof(ShowUnlockPanel));
            }
        }
    }

    public bool ShowUnlockPanel
    {
        get => _showUnlockPanel;
        private set => SetField(ref _showUnlockPanel, value);
    }

    public bool ShowAdminBar
    {
        get => _showAdminBar;
        private set => SetField(ref _showAdminBar, value);
    }

    public bool IsAdminMode
    {
        get => _isAdminMode;
        private set
        {
            if (SetField(ref _isAdminMode, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                ShowAdminBar = value;

                if (!value)
                {
                    ShowUnlockPanel = !ShowSetupPanel;
                }

                foreach (var raiseCanExecuteChanged in _raiseEditCommandCanExecute)
                {
                    raiseCanExecuteChanged();
                }
            }
        }
    }

    public bool CanEdit => IsAdminMode;

    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        private set
        {
            if (SetField(ref _isConfirmingDelete, value))
            {
                OnPropertyChanged(nameof(ShowDeleteButton));
                OnPropertyChanged(nameof(ShowDeleteConfirmation));
                CancelDeleteCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowDeleteButton => !IsConfirmingDelete;

    public bool ShowDeleteConfirmation => IsConfirmingDelete;

    public bool IsNewRule
    {
        get => _isNewRule;
        private set => SetField(ref _isNewRule, value);
    }

    public RuleListItem? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetField(ref _selectedRule, value))
            {
                IsConfirmingDelete = false;

                if (value is null)
                {
                    ClearEditor();
                }
                else
                {
                    LoadRuleIntoEditor(value.Rule);
                }

                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string RuleName
    {
        get => _ruleName;
        set => SetField(ref _ruleName, value);
    }

    public bool RuleEnabled
    {
        get => _ruleEnabled;
        set => SetField(ref _ruleEnabled, value);
    }

    public bool MondayAllowed
    {
        get => _mondayAllowed;
        set => SetField(ref _mondayAllowed, value);
    }

    public bool TuesdayAllowed
    {
        get => _tuesdayAllowed;
        set => SetField(ref _tuesdayAllowed, value);
    }

    public bool WednesdayAllowed
    {
        get => _wednesdayAllowed;
        set => SetField(ref _wednesdayAllowed, value);
    }

    public bool ThursdayAllowed
    {
        get => _thursdayAllowed;
        set => SetField(ref _thursdayAllowed, value);
    }

    public bool FridayAllowed
    {
        get => _fridayAllowed;
        set => SetField(ref _fridayAllowed, value);
    }

    public bool SaturdayAllowed
    {
        get => _saturdayAllowed;
        set => SetField(ref _saturdayAllowed, value);
    }

    public bool SundayAllowed
    {
        get => _sundayAllowed;
        set => SetField(ref _sundayAllowed, value);
    }

    public string StartTimeText
    {
        get => _startTimeText;
        set => SetField(ref _startTimeText, value);
    }

    public string EndTimeText
    {
        get => _endTimeText;
        set => SetField(ref _endTimeText, value);
    }

    public bool UnlimitedDailyLimit
    {
        get => _unlimitedDailyLimit;
        set
        {
            if (SetField(ref _unlimitedDailyLimit, value))
            {
                OnPropertyChanged(nameof(IsDailyLimitMinutesEditable));
            }
        }
    }

    public bool IsDailyLimitMinutesEditable => !UnlimitedDailyLimit;

    public string DailyLimitMinutesText
    {
        get => _dailyLimitMinutesText;
        set => SetField(ref _dailyLimitMinutesText, value);
    }

    public bool ShowGrantPanel
    {
        get => _showGrantPanel;
        private set
        {
            if (SetField(ref _showGrantPanel, value))
            {
                OnPropertyChanged(nameof(ShowGrantButton));
                OnPropertyChanged(nameof(ShowGrantInputs));
                CancelGrantCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowGrantButton => !ShowGrantPanel;

    public bool ShowGrantInputs => ShowGrantPanel;

    public string GrantMinutesText
    {
        get => _grantMinutesText;
        set => SetField(ref _grantMinutesText, value);
    }

    public async Task InitializeAsync()
    {
        _adminSessionTimer.Start();

        if (_initialized)
        {
            return;
        }

        _initialized = true;

        ShowSetupPanel = !await _adminAuthService.HasPasswordAsync();
        RefreshAdminState();

        await ReloadRulesAsync();
    }

    public void Stop() => _adminSessionTimer.Stop();

    public async Task SetupPasswordAsync(string password, string confirmation)
    {
        ErrorMessage = string.Empty;

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        var result = await _adminAuthService.SetPasswordAsync(password);

        switch (result)
        {
            case AdminPasswordResult.Success:
                ShowSetupPanel = false;
                RefreshAdminState();
                StatusMessage = "Contraseña de administrador configurada.";
                break;
            case AdminPasswordResult.TooShort:
                ErrorMessage = "La contraseña debe tener al menos 8 caracteres.";
                break;
            case AdminPasswordResult.InvalidCharacters:
                ErrorMessage = "La contraseña no puede consistir solo en espacios en blanco.";
                break;
            case AdminPasswordResult.AlreadyConfigured:
                ErrorMessage = "Ya existe una contraseña de administrador.";
                ShowSetupPanel = false;
                RefreshAdminState();
                break;
        }
    }

    public async Task UnlockAsync(string password)
    {
        ErrorMessage = string.Empty;

        var result = await _adminAuthService.TryEnterAdminModeAsync(password);

        switch (result)
        {
            case AdminAuthenticationResult.Success:
                RefreshAdminState();
                StatusMessage = "Modo administrador activado.";
                break;
            case AdminAuthenticationResult.InvalidPassword:
                ErrorMessage = "Contraseña incorrecta.";
                break;
            case AdminAuthenticationResult.NotConfigured:
                ErrorMessage = string.Empty;
                ShowSetupPanel = true;
                RefreshAdminState();
                break;
        }
    }

    private void RefreshAdminState()
    {
        IsAdminMode = _adminAuthService.IsAdminMode;

        if (ShowSetupPanel)
        {
            ShowUnlockPanel = false;
        }
        else if (!IsAdminMode)
        {
            ShowUnlockPanel = true;
        }
    }

    private async Task ReloadRulesAsync(RuleListItem? selectAfterReload = null)
    {
        var rules = await _store.GetAllAsync();

        Rules.Clear();
        foreach (var rule in rules.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Rules.Add(new RuleListItem(rule));
        }

        SelectedRule = selectAfterReload is null
            ? null
            : Rules.FirstOrDefault(item => item.Rule.Id == selectAfterReload.Rule.Id);
    }

    private void NewRule()
    {
        SelectedRule = null;
        ClearEditor();
        ErrorMessage = string.Empty;
        StatusMessage = "Rellena el formulario y pulsa Guardar.";
    }

    private void ClearEditor()
    {
        IsNewRule = true;
        RuleName = string.Empty;
        RuleEnabled = true;
        SetSelectedDays(new HashSet<DayOfWeek>());
        StartTimeText = "09:00";
        EndTimeText = "18:00";
        UnlimitedDailyLimit = false;
        DailyLimitMinutesText = "120";
    }

    private void LoadRuleIntoEditor(UsageRule rule)
    {
        IsNewRule = false;
        ErrorMessage = string.Empty;
        RuleName = rule.Name;
        RuleEnabled = rule.Enabled;
        SetSelectedDays(rule.AllowedDays);
        StartTimeText = rule.Schedule.Start.ToString("HH\\:mm");
        EndTimeText = rule.Schedule.End.ToString("HH\\:mm");
        UnlimitedDailyLimit = rule.DailyLimit is null;
        DailyLimitMinutesText = rule.DailyLimit is TimeSpan limit
            ? ((int)limit.TotalMinutes).ToString()
            : "120";
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (!TimeOnly.TryParse(StartTimeText, out var start) ||
            !TimeOnly.TryParse(EndTimeText, out var end))
        {
            ErrorMessage = "Introduce horas válidas en formato HH:mm (por ejemplo, 17:30).";
            return;
        }

        TimeSpan? dailyLimit = null;
        if (!UnlimitedDailyLimit)
        {
            if (!int.TryParse(DailyLimitMinutesText, out var minutes))
            {
                ErrorMessage = "Introduce los minutos del límite diario o marca «Sin límite».";
                return;
            }

            dailyLimit = TimeSpan.FromMinutes(minutes);
        }

        var scheduleResult = AllowedSchedule.TryCreate(start, end, out var schedule);
        if (scheduleResult.Failed)
        {
            ErrorMessage = DomainErrorTranslator.Translate(scheduleResult.Error);
            return;
        }

        var days = SelectedDays();

        var ruleResult = SelectedRule is RuleListItem existing
            ? UsageRule.TryCreate(
                existing.Rule.Id, RuleName, RuleEnabled, days, schedule!, dailyLimit, out var updated)
            : UsageRule.TryCreate(
                RuleName, RuleEnabled, days, schedule!, dailyLimit, out updated);

        if (ruleResult.Failed)
        {
            ErrorMessage = DomainErrorTranslator.Translate(ruleResult.Error);
            return;
        }

        var saved = new RuleListItem(updated!);

        if (SelectedRule is RuleListItem previous)
        {
            await _store.UpdateAsync(updated!);
        }
        else
        {
            await _store.AddAsync(updated!);
        }

        await ReloadRulesAsync(saved);
        StatusMessage = "Regla guardada.";
    }

    private async Task DeleteAsync()
    {
        if (SelectedRule is not { } selected)
        {
            return;
        }

        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return;
        }

        await _store.RemoveAsync(selected.Rule.Id);
        IsConfirmingDelete = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Regla eliminada.";
        await ReloadRulesAsync();
    }

    private void CancelDelete()
    {
        IsConfirmingDelete = false;
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private void LockAdmin()
    {
        _adminAuthService.ExitAdminMode();
        RefreshAdminState();
        ErrorMessage = string.Empty;
        StatusMessage = "Modo administrador bloqueado.";
    }

    private void OpenGrantPanel()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        ShowGrantPanel = true;
    }

    private void CancelGrantPanel()
    {
        ShowGrantPanel = false;
    }

    public async Task ConfirmGrantAsync(string password)
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (!int.TryParse(GrantMinutesText, out var minutes) || minutes <= 0)
        {
            ErrorMessage = "Introduce los minutos a regalar (número entero positivo).";
            return;
        }

        var authentication = await _adminAuthService.TryEnterAdminModeAsync(password);

        switch (authentication)
        {
            case AdminAuthenticationResult.InvalidPassword:
                ErrorMessage = "Contraseña incorrecta.";
                return;
            case AdminAuthenticationResult.NotConfigured:
                ShowGrantPanel = false;
                ShowSetupPanel = true;
                RefreshAdminState();
                return;
        }

        var now = _timeProvider.GetUtcNow();

        var grantResult = TimeGrant.TryCreate(now, minutes, out var grant);

        if (grantResult.Failed)
        {
            ErrorMessage = DomainErrorTranslator.Translate(grantResult.Error);
            return;
        }

        await _grantStore.AddAsync(grant!);

        var todayGrants = await _grantStore.GetGrantsForDayAsync(
            DateOnly.FromDateTime(now.LocalDateTime),
            TimeZoneInfo.Local.GetUtcOffset(now));

        var totalToday = todayGrants.Sum(g => g.Minutes);

        ShowGrantPanel = false;
        RefreshAdminState();
        StatusMessage = $"Regalados {minutes} minutos para hoy. Total regalado hoy: {totalToday} minutos.";
    }

    private HashSet<DayOfWeek> SelectedDays()
    {
        var days = new HashSet<DayOfWeek>();

        if (MondayAllowed) days.Add(DayOfWeek.Monday);
        if (TuesdayAllowed) days.Add(DayOfWeek.Tuesday);
        if (WednesdayAllowed) days.Add(DayOfWeek.Wednesday);
        if (ThursdayAllowed) days.Add(DayOfWeek.Thursday);
        if (FridayAllowed) days.Add(DayOfWeek.Friday);
        if (SaturdayAllowed) days.Add(DayOfWeek.Saturday);
        if (SundayAllowed) days.Add(DayOfWeek.Sunday);

        return days;
    }

    private void SetSelectedDays(IReadOnlySet<DayOfWeek> days)
    {
        MondayAllowed = days.Contains(DayOfWeek.Monday);
        TuesdayAllowed = days.Contains(DayOfWeek.Tuesday);
        WednesdayAllowed = days.Contains(DayOfWeek.Wednesday);
        ThursdayAllowed = days.Contains(DayOfWeek.Thursday);
        FridayAllowed = days.Contains(DayOfWeek.Friday);
        SaturdayAllowed = days.Contains(DayOfWeek.Saturday);
        SundayAllowed = days.Contains(DayOfWeek.Sunday);
    }
}
