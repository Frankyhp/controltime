using System.Windows;
using ControlTime.App.Services;
using ControlTime.App.ViewModels;
using ControlTime.App.Views;
using ControlTime.Core;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using ControlTime.Infrastructure;
using ControlTime.Infrastructure.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlTime.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private MonitoringHost? _monitoringHost;
    private Action<MonitorState>? _trayStateChangedHandler;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = _services = CreateServices();

        try
        {
            ControlTimeStorage.MigrateLegacyDatabase();

            using ControlTimeDbContext context = services
                .GetRequiredService<IDbContextFactory<ControlTimeDbContext>>()
                .CreateDbContext();

            context.Database.Migrate();
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(App))
                .LogCritical(ex, "No se pudo preparar la base de datos de ControlTime.");

            MessageBox.Show(
                "No se pudo preparar la base de datos de ControlTime.\n\n" + ex.Message,
                "ControlTime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        MonitoringHost monitoringHost = services.GetRequiredService<MonitoringHost>();
        _monitoringHost = monitoringHost;

        WarningNotifier warningNotifier = services.GetRequiredService<WarningNotifier>();

        ILogger<WarningNotifier> notifierLogger =
            services.GetRequiredService<ILogger<WarningNotifier>>();

        monitoringHost.StateChanged += state =>
        {
            try
            {
                warningNotifier.Process(state);
            }
            catch (Exception ex)
            {
                notifierLogger.LogError(ex, "No se pudo mostrar el aviso de tiempo restante.");
            }
        };

        // Desde la fase 17a el tracking de uso y el enforcement viven en
        // ControlTime.Service (Windows Service). La App solo presenta estado.

        TrayIconService trayIcon = services.GetRequiredService<TrayIconService>();

        ILogger<TrayIconService> trayLogger =
            services.GetRequiredService<ILogger<TrayIconService>>();

        trayIcon.ExitRequested += HandleTrayExit;

        _trayStateChangedHandler = state =>
        {
            try
            {
                trayIcon.UpdateState(state);
            }
            catch (Exception ex)
            {
                trayLogger.LogError(ex, "No se pudo actualizar el icono de la bandeja.");
            }
        };

        monitoringHost.StateChanged += _trayStateChangedHandler;

        _ = trayIcon.SyncAutoStartFromSettingsAsync();

        monitoringHost.Start();

        MainWindow = services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_monitoringHost is not null && _trayStateChangedHandler is not null)
        {
            _monitoringHost.StateChanged -= _trayStateChangedHandler;
        }

        _services?.Dispose();
        base.OnExit(e);
    }

    private void HandleTrayExit()
    {
        if (_services?.GetRequiredService<IAdminAuthService>() is not { } authService)
        {
            return;
        }

        MainWindow? window = _services.GetService<MainWindow>();

        var prompt = new AdminExitWindow(authService)
        {
            Owner = window is { IsVisible: true } ? window : null,
        };

        if (prompt.ShowDialog() != true)
        {
            return;
        }

        window?.CloseForShutdown();
        Shutdown();
    }

    private const string EnforcementDisabledVariable = "CONTROLTIME_ENFORCEMENT";

    private static bool IsEnforcementEnabled() =>
        !string.Equals(
            Environment.GetEnvironmentVariable(EnforcementDisabledVariable),
            "off",
            StringComparison.OrdinalIgnoreCase);

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug());

        ControlTimeStorage.EnsureDatabaseDirectory();

        services.AddDbContextFactory<ControlTimeDbContext>(options =>
            options.UseSqlite(ControlTimeStorage.GetConnectionString()));

        services.AddTransient<IUsageRuleStore, SqliteUsageRuleStore>();
        services.AddTransient<IAppSettingsStore, SqliteAppSettingsStore>();
        services.AddTransient<ITimeGrantStore, SqliteTimeGrantStore>();
        services.AddTransient<IUsageSessionStore, SqliteUsageSessionStore>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAdminAuthService>(provider => new AdminAuthService(
            provider.GetRequiredService<IAppSettingsStore>(),
            provider.GetRequiredService<IPasswordHasher>()));

        services.AddSingleton<IActivityMonitor>(provider => new WindowsActivityMonitor(
            logger: provider.GetService<ILogger<WindowsActivityMonitor>>()));

        services.AddSingleton<RuleEngine>(_ => new RuleEngine());
        services.AddTransient<UsageService>();
        services.AddTransient<DashboardService>(provider => new DashboardService(
            provider.GetRequiredService<IUsageRuleStore>(),
            provider.GetRequiredService<ITimeGrantStore>(),
            provider.GetRequiredService<UsageService>(),
            provider.GetRequiredService<RuleEngine>()));

        services.AddTransient<MonitoringService>(provider => new MonitoringService(
            provider.GetRequiredService<IActivityMonitor>(),
            provider.GetRequiredService<DashboardService>()));
        services.AddSingleton<MonitoringHost>();

        services.AddSingleton<WarningEvaluator>();
        services.AddSingleton<WarningNotifier>(provider => new WarningNotifier(
            provider.GetRequiredService<WarningEvaluator>(),
            provider.GetRequiredService<INotifier>()));

        services.AddSingleton<IAutostartRegistrar, WindowsAutoStartRegistry>();
        services.AddSingleton<TrayIconService>(provider => new TrayIconService(
            provider.GetRequiredService<IAutostartRegistrar>(),
            provider.GetRequiredService<IAppSettingsStore>(),
            provider.GetRequiredService<MainWindow>(),
            provider.GetRequiredService<ILogger<TrayIconService>>()));
        services.AddSingleton<INotifier>(
            provider => provider.GetRequiredService<TrayIconService>());

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RulesViewModel>();
        services.AddTransient<SettingsViewModel>(provider => new SettingsViewModel(
            provider.GetRequiredService<IAdminAuthService>(),
            provider.GetRequiredService<IAppSettingsStore>(),
            provider.GetRequiredService<IAutostartRegistrar>(),
            provider.GetRequiredService<ILogger<SettingsViewModel>>(),
            IsEnforcementEnabled()));
        services.AddTransient<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
