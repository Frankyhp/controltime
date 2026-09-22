using System.Windows;
using ControlTime.App.Services;
using ControlTime.App.ViewModels;
using ControlTime.App.Views;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using ControlTime.Infrastructure;
using ControlTime.Infrastructure.Ipc;
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
    private ServiceClientHost? _serviceClientHost;
    private Action<MonitorState>? _trayStateChangedHandler;
    private Action<bool>? _trayAvailabilityHandler;

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

        ServiceClientHost serviceClientHost = services.GetRequiredService<ServiceClientHost>();
        _serviceClientHost = serviceClientHost;

        WarningNotifier warningNotifier = services.GetRequiredService<WarningNotifier>();

        ILogger<WarningNotifier> notifierLogger =
            services.GetRequiredService<ILogger<WarningNotifier>>();

        serviceClientHost.StateChanged += state =>
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

        // Desde la fase 17/17b, el tracking de uso, la autenticación de
        // administrador y el enforcement viven en ControlTime.Service.
        // La App presenta estado, reporta actividad local y gestiona la UI.

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

        serviceClientHost.StateChanged += _trayStateChangedHandler;

        _trayAvailabilityHandler = available =>
        {
            try
            {
                trayIcon.UpdateAvailability(available);
            }
            catch (Exception ex)
            {
                trayLogger.LogError(ex, "No se pudo actualizar el icono de la bandeja.");
            }
        };

        serviceClientHost.AvailabilityChanged += _trayAvailabilityHandler;

        _ = trayIcon.SyncAutoStartFromSettingsAsync();

        serviceClientHost.Start();

        MainWindow = services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceClientHost is not null)
        {
            if (_trayStateChangedHandler is not null)
            {
                _serviceClientHost.StateChanged -= _trayStateChangedHandler;
            }

            if (_trayAvailabilityHandler is not null)
            {
                _serviceClientHost.AvailabilityChanged -= _trayAvailabilityHandler;
            }
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

        services.AddSingleton<IActivityMonitor>(provider => new WindowsActivityMonitor(
            logger: provider.GetService<ILogger<WindowsActivityMonitor>>()));

        services.AddSingleton<ControlTimePipeClient>();
        services.AddSingleton<IControlTimeGateway>(
            provider => provider.GetRequiredService<ControlTimePipeClient>());
        services.AddSingleton<GatewayAdminAuthService>();
        services.AddSingleton<IAdminAuthService>(
            provider => provider.GetRequiredService<GatewayAdminAuthService>());
        services.AddSingleton<ServiceClientHost>();

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
            provider.GetRequiredService<IControlTimeGateway>(),
            provider.GetRequiredService<IAppSettingsStore>(),
            provider.GetRequiredService<IAutostartRegistrar>(),
            provider.GetRequiredService<ILogger<SettingsViewModel>>()));
        services.AddTransient<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
