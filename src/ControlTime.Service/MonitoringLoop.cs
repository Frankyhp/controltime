using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using ControlTime.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlTime.Service;

public sealed class MonitoringLoop : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    public const string EnforcementDisabledVariable = "CONTROLTIME_ENFORCEMENT";

    private readonly MonitoringService _monitoringService;
    private readonly EnforcementService _enforcementService;
    private readonly IDbContextFactory<ControlTimeDbContext> _dbContextFactory;
    private readonly ILogger<MonitoringLoop> _logger;
    private readonly bool _enforcementEnabled;

    private MonitorState? _lastState;

    public MonitoringLoop(
        MonitoringService monitoringService,
        EnforcementService enforcementService,
        IDbContextFactory<ControlTimeDbContext> dbContextFactory,
        ILogger<MonitoringLoop> logger,
        bool enforcementEnabled)
    {
        ArgumentNullException.ThrowIfNull(monitoringService);
        ArgumentNullException.ThrowIfNull(enforcementService);
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _monitoringService = monitoringService;
        _enforcementService = enforcementService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _enforcementEnabled = enforcementEnabled;
    }

    public MonitorState? LastState => _lastState;

    public static bool IsEnforcementEnabled() =>
        !string.Equals(
            Environment.GetEnvironmentVariable(EnforcementDisabledVariable),
            "off",
            StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrepareDatabase();

        if (!_enforcementEnabled)
        {
            _logger.LogWarning(
                "El bloqueo de sesión está desactivado por la variable de entorno {Variable}.",
                EnforcementDisabledVariable);
        }

        _logger.LogInformation(
            "ControlTime Service iniciado. Intervalo de monitoreo: {Interval}s.",
            TickInterval.TotalSeconds);

        using PeriodicTimer timer = new(TickInterval);

        do
        {
            await TickOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            MonitorState state = await _monitoringService.TickAsync(
                DateTimeOffset.Now.Offset,
                cancellationToken);

            _lastState = state;

            if (!_enforcementEnabled)
            {
                return;
            }

            _enforcementService.Process(state);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "El monitoreo del servicio no pudo evaluar el estado.");
        }
    }

    private void PrepareDatabase()
    {
        if (ControlTimeStorage.MigrateLegacyDatabase())
        {
            _logger.LogInformation(
                "La base de datos se movió desde la ubicación de usuario antigua a {Path}.",
                ControlTimeStorage.DatabasePath);
        }

        using ControlTimeDbContext context = _dbContextFactory.CreateDbContext();
        context.Database.Migrate();
    }
}
