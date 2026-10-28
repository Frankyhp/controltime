using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using Microsoft.Extensions.Logging;

namespace ControlTime.Core.Services;

public sealed class UsageTrackingService(
    IUsageSessionStore sessionStore,
    ILogger<UsageTrackingService>? logger = null,
    TimeSpan? observationGapTolerance = null)
{
    private readonly IUsageSessionStore _sessionStore = sessionStore
        ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly ILogger<UsageTrackingService>? _logger = logger;

    // La tolerancia solo tiene significado para el huésped que conoce el
    // intervalo de su bucle de sondeo: null desactiva la detección de huecos.
    private readonly TimeSpan? _observationGapTolerance =
        observationGapTolerance is TimeSpan tolerance && tolerance <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(
                nameof(observationGapTolerance),
                "The observation gap tolerance must be positive.")
            : observationGapTolerance;

    private UsageSession? _openSession;
    private bool _loaded;
    private bool _ticking;
    private DateTimeOffset? _lastObservedAt;

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

        // Hueco de observación: el proceso no emitió ticks durante un tiempo muy
        // superior al intervalo de sondeo (suspensión o congelación del proceso
        // sobreviviendo al apagado del equipo). Todo el tramo desde el último
        // tick observado carece de evidencia de uso real y no puede convertirse
        // en uso del día; la sesión abierta se cierra en el último instante
        // observado y, si la actividad sigue siendo real, se abre otra en el
        // instante presente.
        if (_observationGapTolerance is TimeSpan tolerance
            && _lastObservedAt is DateTimeOffset previous
            && status.Timestamp > previous
            && status.Timestamp - previous > tolerance
            && _openSession is UsageSession stale)
        {
            await CloseAtBoundaryAsync(stale, previous, cancellationToken);
        }

        _lastObservedAt = status.Timestamp;

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

    private async Task CloseAtBoundaryAsync(
        UsageSession open,
        DateTimeOffset boundary,
        CancellationToken cancellationToken)
    {
        _openSession = null;

        DateTimeOffset closeAt = open.StartedAt > boundary ? open.StartedAt : boundary;

        if (open.TryClose(closeAt, out UsageSession? closed).Success && closed is not null)
        {
            await _sessionStore.UpdateAsync(closed, cancellationToken);

            _logger?.LogWarning(
                "Hueco de observación detectado (último tick observado en {Boundary:O}, tolerancia {Tolerance}). " +
                "La sesión de uso {SessionId} se cierra en el último instante observado; el tramo posterior " +
                "sin monitorizar no se computa como uso.",
                boundary,
                _observationGapTolerance,
                open.Id);
        }
        else
        {
            _logger?.LogError(
                "No se pudo cerrar la sesión de uso {SessionId} en el hueco de observación; se descarta para no dejar datos inválidos.",
                open.Id);

            await _sessionStore.DeleteAsync(open.Id, cancellationToken);
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
