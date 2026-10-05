using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using Microsoft.Extensions.Logging;

namespace ControlTime.Core.Services;

public sealed class UsageTrackingService(
    IUsageSessionStore sessionStore,
    ILogger<UsageTrackingService>? logger = null)
{
    private readonly IUsageSessionStore _sessionStore = sessionStore
        ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly ILogger<UsageTrackingService>? _logger = logger;

    private UsageSession? _openSession;
    private bool _loaded;
    private bool _ticking;

    public UsageSession? OpenSession => _openSession;

    public async Task TickAsync(ActivityStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            await TrackAsync(status, cancellationToken);
        }
        finally
        {
            _ticking = false;
        }
    }

    public async Task CloseOpenSessionAsync(
        DateTimeOffset closeAt,
        CancellationToken cancellationToken = default)
    {
        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            if (!_loaded)
            {
                _openSession = await _sessionStore.GetOpenSessionAsync(cancellationToken);
                _loaded = true;

                if (_openSession is UsageSession orphan)
                {
                    // La sesión no la ha observado este proceso: el tramo final
                    // es desconocido y no puede convertirse en uso.
                    await DiscardUnobservedTailAsync(orphan, cancellationToken);
                    return;
                }
            }

            if (_openSession is UsageSession open)
            {
                _openSession = null;
                await CloseAsync(open, closeAt, "la parada del seguimiento", cancellationToken);
            }
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task TrackAsync(ActivityStatus status, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _openSession = await _sessionStore.GetOpenSessionAsync(cancellationToken);
            _loaded = true;

            if (_openSession is UsageSession orphan)
            {
                // Una sesión huérfana tiene un tramo final sin observar
                // (parada brusca del servicio, apagado o fallo del equipo):
                // conservarla convertría en uso real tiempo que no se ha
                // monitorizado, inflando el uso del día hasta la hora actual.
                await DiscardUnobservedTailAsync(orphan, cancellationToken);
            }
        }

        if (status.State == ActivityState.Active)
        {
            if (_openSession is null)
            {
                UsageSession.TryCreateOpen(status.Timestamp, out UsageSession? session);

                _openSession = session!;
                await _sessionStore.AddAsync(session!, cancellationToken);
            }

            return;
        }

        if (_openSession is UsageSession open)
        {
            _openSession = null;
            await CloseAsync(open, status.LastInputAt, "la inactividad", cancellationToken);
        }
    }

    private async Task DiscardUnobservedTailAsync(
        UsageSession orphan,
        CancellationToken cancellationToken)
    {
        _openSession = null;

        if (orphan.TryClose(orphan.StartedAt, out UsageSession? closed).Success && closed is not null)
        {
            await _sessionStore.UpdateAsync(closed, cancellationToken);

            _logger?.LogWarning(
                "La sesión de uso huérfana {SessionId}, iniciada en {StartedAt:O}, se cierra en su propio inicio " +
                "sin computar el tramo final no monitorizado tras la interrupción del seguimiento.",
                orphan.Id,
                orphan.StartedAt);
        }
        else
        {
            _logger?.LogError(
                "No se pudo cerrar la sesión de uso huérfana {SessionId}; se descarta para no dejar datos inválidos.",
                orphan.Id);

            await _sessionStore.DeleteAsync(orphan.Id, cancellationToken);
        }
    }

    private async Task CloseAsync(
        UsageSession open,
        DateTimeOffset lastInputAt,
        string reason,
        CancellationToken cancellationToken)
    {
        DateTimeOffset closeAt = open.StartedAt > lastInputAt ? open.StartedAt : lastInputAt;

        if (closeAt != lastInputAt)
        {
            _logger?.LogWarning(
                "La última actividad conocida ({LastInput:O}) es anterior al inicio de la sesión de uso {SessionId} ({StartedAt:O}); " +
                "posible retroceso del reloj del sistema. La sesión se cierra en su propio inicio sin computar uso.",
                lastInputAt,
                open.Id,
                open.StartedAt);
        }

        if (open.TryClose(closeAt, out UsageSession? closed).Success && closed is not null)
        {
            await _sessionStore.UpdateAsync(closed, cancellationToken);

            _logger?.LogInformation(
                "Sesión de uso {SessionId} cerrada por {Reason} en {ClosedAt:O} (duracion {Duration}).",
                open.Id,
                reason,
                closed.EndedAt,
                closed.Duration);
        }
        else
        {
            _logger?.LogError(
                "No se pudo cerrar la sesión de uso {SessionId}; se descarta para no dejar datos inválidos.",
                open.Id);

            await _sessionStore.DeleteAsync(open.Id, cancellationToken);
        }
    }
}
