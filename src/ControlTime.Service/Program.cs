using ControlTime.Core;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Services;
using ControlTime.Infrastructure;
using ControlTime.Infrastructure.Ipc;
using ControlTime.Infrastructure.Logging;
using ControlTime.Infrastructure.Windows;
using ControlTime.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Modo ayudante efímero de la sonda de entrada (Fase 24): lanzado por el propio
// Servicio en la sesión del usuario, mide GetLastInputInfo y publica el resultado
// en memoria compartida. Sale antes de construir el host: jamás abre la base de
// datos ni escribe logs.
if (args.Length == 2
    && string.Equals(args[0], UserInputProbeChild.ProbeModeArgument, StringComparison.Ordinal))
{
    return UserInputProbeChild.Run(args[1]);
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ControlTimeService";
});

builder.Logging.AddControlTimeFileLogs();

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

// Sonda de entrada real de la sesión consola (Fase 24): solo se consulta desde
// el fallback WTS, que el CompositeActivityMonitor ya usa únicamente cuando no
// hay informe fresco del App (prioridad IPC de la Fase 18 intacta).
builder.Services.AddSingleton(provider => new ConsoleSessionInputProbe(
    logger: provider.GetService<ILogger<ConsoleSessionInputProbe>>()));

builder.Services.AddSingleton<CompositeActivityMonitor>(provider =>
{
    ConsoleSessionInputProbe inputProbe = provider.GetRequiredService<ConsoleSessionInputProbe>();

    return new CompositeActivityMonitor(
        new WtsConsoleSessionMonitor(
            provider.GetService<ILogger<WtsConsoleSessionMonitor>>(),
            sessionId => inputProbe.TryGetLastInputAt(sessionId, out DateTimeOffset lastInputAt)
                ? lastInputAt
                : null,
            WindowsActivityMonitor.DefaultIdleThreshold),
        provider.GetRequiredService<ActivityReportSink>());
});

builder.Services.AddSingleton<IActivityMonitor>(
    provider => provider.GetRequiredService<CompositeActivityMonitor>());

builder.Services.AddSingleton<RuleEngine>(_ => new RuleEngine());
builder.Services.AddTransient<UsageService>();
builder.Services.AddTransient<DashboardService>(provider => new DashboardService(
    provider.GetRequiredService<IUsageRuleStore>(),
    provider.GetRequiredService<ITimeGrantStore>(),
    provider.GetRequiredService<UsageService>(),
    provider.GetRequiredService<RuleEngine>()));

builder.Services.AddSingleton<UsageTrackingService>(provider => new UsageTrackingService(
    provider.GetRequiredService<IUsageSessionStore>(),
    provider.GetService<ILogger<UsageTrackingService>>(),
    // El bucle de monitoreo emite un tick por segundo: cualquier hueco entre
    // ticks muy superior a ese intervalo significa que el proceso estuvo
    // congelado (suspensión/apagado con apagado rápido) y ese tramo no puede
    // computarse como uso.
    observationGapTolerance: TimeSpan.FromSeconds(15)));
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
    provider.GetRequiredService<UsageTrackingService>(),
    provider.GetRequiredService<IDbContextFactory<ControlTimeDbContext>>(),
    provider.GetRequiredService<ILogger<MonitoringLoop>>(),
    MonitoringLoop.IsEnforcementEnabled()));
builder.Services.AddHostedService(
    provider => provider.GetRequiredService<MonitoringLoop>());

builder.Services.AddSingleton(provider => new ServiceStatusProvider(
    provider.GetRequiredService<MonitoringLoop>(),
    provider.GetRequiredService<IAdminAuthService>(),
    provider.GetRequiredService<CompositeActivityMonitor>(),
    MonitoringLoop.IsEnforcementEnabled()));

builder.Services.AddSingleton(provider => new ControlTimePipeServer(
    provider.GetRequiredService<IAdminAuthService>(),
    provider.GetRequiredService<ActivityReportSink>(),
    provider.GetRequiredService<ServiceStatusProvider>().GetStatusAsync,
    provider.GetService<ILogger<ControlTimePipeServer>>()));
builder.Services.AddHostedService<PipeServerHost>();

await builder.Build().RunAsync();
return 0;
