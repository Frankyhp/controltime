using ControlTime.Core;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Services;
using ControlTime.Infrastructure;
using ControlTime.Infrastructure.Ipc;
using ControlTime.Infrastructure.Windows;
using ControlTime.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ControlTimeService";
});

ControlTimeStorage.EnsureDatabaseDirectory();

builder.Services.AddDbContextFactory<ControlTimeDbContext>(options =>
    options.UseSqlite(ControlTimeStorage.GetConnectionString()));

builder.Services.AddTransient<IUsageRuleStore, SqliteUsageRuleStore>();
builder.Services.AddTransient<IAppSettingsStore, SqliteAppSettingsStore>();
builder.Services.AddTransient<ITimeGrantStore, SqliteTimeGrantStore>();
builder.Services.AddTransient<IUsageSessionStore, SqliteUsageSessionStore>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<AdminAuthService>(provider => new AdminAuthService(
    provider.GetRequiredService<IAppSettingsStore>(),
    provider.GetRequiredService<IPasswordHasher>()));
builder.Services.AddSingleton<IAdminAuthService>(
    provider => provider.GetRequiredService<AdminAuthService>());

builder.Services.AddSingleton<ActivityReportSink>();

builder.Services.AddSingleton<IActivityMonitor>(provider => new CompositeActivityMonitor(
    new WtsConsoleSessionMonitor(
        provider.GetService<ILogger<WtsConsoleSessionMonitor>>()),
    provider.GetRequiredService<ActivityReportSink>()));

builder.Services.AddSingleton<RuleEngine>(_ => new RuleEngine());
builder.Services.AddTransient<UsageService>();
builder.Services.AddTransient<DashboardService>(provider => new DashboardService(
    provider.GetRequiredService<IUsageRuleStore>(),
    provider.GetRequiredService<ITimeGrantStore>(),
    provider.GetRequiredService<UsageService>(),
    provider.GetRequiredService<RuleEngine>()));

builder.Services.AddSingleton<UsageTrackingService>();
builder.Services.AddTransient<MonitoringService>(provider => new MonitoringService(
    provider.GetRequiredService<IActivityMonitor>(),
    provider.GetRequiredService<DashboardService>(),
    provider.GetRequiredService<UsageTrackingService>()));

builder.Services.AddSingleton<ISessionLock>(provider => new WtsSessionLock(
    provider.GetService<ILogger<WtsSessionLock>>()));
builder.Services.AddSingleton<EnforcementService>();

builder.Services.AddSingleton(provider => new MonitoringLoop(
    provider.GetRequiredService<MonitoringService>(),
    provider.GetRequiredService<EnforcementService>(),
    provider.GetRequiredService<IDbContextFactory<ControlTimeDbContext>>(),
    provider.GetRequiredService<ILogger<MonitoringLoop>>(),
    MonitoringLoop.IsEnforcementEnabled()));
builder.Services.AddHostedService(
    provider => provider.GetRequiredService<MonitoringLoop>());

builder.Services.AddSingleton(provider => new ServiceStatusProvider(
    provider.GetRequiredService<MonitoringLoop>(),
    provider.GetRequiredService<IAdminAuthService>(),
    provider.GetRequiredService<ActivityReportSink>(),
    MonitoringLoop.IsEnforcementEnabled()));

builder.Services.AddSingleton(provider => new ControlTimePipeServer(
    provider.GetRequiredService<IAdminAuthService>(),
    provider.GetRequiredService<ActivityReportSink>(),
    provider.GetRequiredService<ServiceStatusProvider>().GetStatusAsync,
    provider.GetService<ILogger<ControlTimePipeServer>>()));
builder.Services.AddHostedService<PipeServerHost>();

await builder.Build().RunAsync();
