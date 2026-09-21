using System.Windows;
using ControlTime.App.Services;
using ControlTime.App.ViewModels;
using ControlTime.Core;
using ControlTime.Core.Abstractions;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = _services = CreateServices();

        try
        {
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

        services.GetRequiredService<MonitoringHost>().Start();

        MainWindow = services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
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

        services.AddSingleton<UsageTrackingService>();
        services.AddTransient<MonitoringService>();
        services.AddSingleton<MonitoringHost>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RulesViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
