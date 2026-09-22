using System.Windows.Threading;
using ControlTime.App.Mvvm;
using ControlTime.App.Services;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace ControlTime.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IAdminAuthService _adminAuthService;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly IAutostartRegistrar _autostartRegistrar;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly bool _isEnforcementEnabled;
    private readonly DispatcherTimer _adminSessionTimer;
    private readonly List<Action> _raiseCommandCanExecute = [];

    private bool _initialized;
    private bool _showSetupPanel;
    private bool _showUnlockPanel;
    private bool _showAdminBar;
    private bool _isAdminMode;
    private bool _autoStartEnabled;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public SettingsViewModel(
        IAdminAuthService adminAuthService,
        IAppSettingsStore appSettingsStore,
        IAutostartRegistrar autostartRegistrar,
        ILogger<SettingsViewModel> logger,
        bool isEnforcementEnabled)
    {
        ArgumentNullException.ThrowIfNull(adminAuthService);
        ArgumentNullException.ThrowIfNull(appSettingsStore);
        ArgumentNullException.ThrowIfNull(autostartRegistrar);
        ArgumentNullException.ThrowIfNull(logger);

        _adminAuthService = adminAuthService;
        _appSettingsStore = appSettingsStore;
        _autostartRegistrar = autostartRegistrar;
        _logger = logger;
        _isEnforcementEnabled = isEnforcementEnabled;

        _adminSessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _adminSessionTimer.Tick += (_, _) => RefreshAdminState();

        LockAdminCommand = new RelayCommand(LockAdmin, () => IsAdminMode);
        ToggleAutoStartCommand = new RelayCommand(ToggleAutoStart, () => CanEdit);

        _raiseCommandCanExecute.Add(LockAdminCommand.RaiseCanExecuteChanged);
        _raiseCommandCanExecute.Add(ToggleAutoStartCommand.RaiseCanExecuteChanged);
    }

    public RelayCommand LockAdminCommand { get; }
    public RelayCommand ToggleAutoStartCommand { get; }

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

                foreach (var raiseCanExecuteChanged in _raiseCommandCanExecute)
                {
                    raiseCanExecuteChanged();
                }
            }
        }
    }

    public bool CanEdit => IsAdminMode;

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

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        private set => SetField(ref _autoStartEnabled, value);
    }

    public string EnforcementStatusText => _isEnforcementEnabled
        ? "activo"
        : "desactivado por la variable de entorno CONTROLTIME_ENFORCEMENT";

    public async Task InitializeAsync()
    {
        _adminSessionTimer.Start();

        if (_initialized)
        {
            RefreshAutoStartState();
            return;
        }

        _initialized = true;

        ShowSetupPanel = !await _adminAuthService.HasPasswordAsync();
        RefreshAdminState();
        RefreshAutoStartState();
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
            case AdminAuthenticationResult.LockedOut:
                ErrorMessage = "Demasiados intentos. Espera unos segundos.";
                break;
            case AdminAuthenticationResult.NotConfigured:
                ErrorMessage = string.Empty;
                ShowSetupPanel = true;
                RefreshAdminState();
                break;
        }
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, string confirmation)
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas nuevas no coinciden.";
            return;
        }

        var result = await _adminAuthService.ChangePasswordAsync(currentPassword, newPassword);

        switch (result)
        {
            case AdminPasswordChangeResult.Success:
                RefreshAdminState();
                StatusMessage = "Contraseña de administrador actualizada.";
                break;
            case AdminPasswordChangeResult.InvalidPassword:
                ErrorMessage = "La contraseña actual no es correcta.";
                break;
            case AdminPasswordChangeResult.TooShort:
                ErrorMessage = "La nueva contraseña debe tener al menos 8 caracteres.";
                break;
            case AdminPasswordChangeResult.InvalidCharacters:
                ErrorMessage = "La nueva contraseña no puede consistir solo en espacios en blanco.";
                break;
            case AdminPasswordChangeResult.LockedOut:
                ErrorMessage = "Demasiados intentos. Espera unos segundos.";
                break;
            case AdminPasswordChangeResult.NotConfigured:
                ErrorMessage = string.Empty;
                ShowSetupPanel = true;
                RefreshAdminState();
                break;
        }
    }

    private void ToggleAutoStart()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        bool desired = !AutoStartEnabled;

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

            AutoStartEnabled = desired;
            _ = PersistAutoStartSettingAsync(desired);

            StatusMessage = desired
                ? "ControlTime se iniciará con Windows."
                : "ControlTime ya no se iniciará con Windows.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo actualizar el arranque automático.");
            ErrorMessage = "No se pudo actualizar el arranque automático.";
            RefreshAutoStartState();
        }
    }

    private async Task PersistAutoStartSettingAsync(bool desired)
    {
        try
        {
            await _appSettingsStore.SetAsync(
                TrayIconService.AutoStartSettingKey,
                desired ? "true" : "false");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar la preferencia de arranque.");
            ErrorMessage = "No se pudo guardar la preferencia de arranque.";
        }
    }

    private void RefreshAutoStartState()
    {
        try
        {
            AutoStartEnabled = _autostartRegistrar.IsEnabled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer el arranque automático de Windows.");
            AutoStartEnabled = false;
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

    private void LockAdmin()
    {
        _adminAuthService.ExitAdminMode();
        RefreshAdminState();
        ErrorMessage = string.Empty;
        StatusMessage = "Modo administrador bloqueado.";
    }
}
