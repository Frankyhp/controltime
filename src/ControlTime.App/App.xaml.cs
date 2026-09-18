using System.Windows;
using ControlTime.App.ViewModels;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Services;
using ControlTime.Infrastructure;
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
            services.GetRequiredService<ControlTimeDbContext>().Database.Migrate();
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

        services.AddSingleton(_ => new ControlTimeDbContext(
            new DbContextOptionsBuilder<ControlTimeDbContext>()
                .UseSqlite(ControlTimeStorage.GetConnectionString())
                .Options));

        services.AddTransient<IUsageRuleStore, SqliteUsageRuleStore>();
        services.AddTransient<IAppSettingsStore, SqliteAppSettingsStore>();
        services.AddTransient<ITimeGrantStore, SqliteTimeGrantStore>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAdminAuthService>(provider => new AdminAuthService(
            provider.GetRequiredService<IAppSettingsStore>(),
            provider.GetRequiredService<IPasswordHasher>()));

        services.AddTransient<RulesViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
