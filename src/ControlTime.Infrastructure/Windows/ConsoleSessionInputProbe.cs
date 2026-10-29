using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Windows;

/// <summary>
/// Sonda de última entrada real del usuario de la sesión consola (Fase 24).
/// El sondeo (lanzamiento del ayudante) cuesta ~1-2 s, así que nunca se hace en
/// el tick del monitoreo: <see cref="TryGetLastInputAt"/> sirve el último resultado
/// fresco y, cuando caduca, lanza una sonda en segundo plano (a lo sumo una en
/// curso, como máximo una por <paramref name="probeInterval"/>). Sin resultado
/// fresco devuelve false y el llamador mantiene el comportamiento optimista.
/// </summary>
public sealed class ConsoleSessionInputProbe
{
    public static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultResultLifetime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan FailureLogThrottle = TimeSpan.FromMinutes(1);

    private readonly Func<uint, DateTimeOffset?> _probe;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _probeInterval;
    private readonly TimeSpan _resultLifetime;
    private readonly ILogger<ConsoleSessionInputProbe>? _logger;

    private readonly object _gate = new();
    private uint? _cachedSessionId;
    private DateTimeOffset _cachedLastInputAt;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastProbeStartedAt = DateTimeOffset.MinValue;
    private bool _probeInFlight;
    private DateTimeOffset _lastFailureLoggedAt = DateTimeOffset.MinValue;

    public ConsoleSessionInputProbe(
        Func<uint, DateTimeOffset?>? probe = null,
        TimeProvider? timeProvider = null,
        TimeSpan? probeInterval = null,
        TimeSpan? resultLifetime = null,
        ILogger<ConsoleSessionInputProbe>? logger = null)
    {
        _probe = probe ?? ProbeNative;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _probeInterval = probeInterval ?? DefaultProbeInterval;
        _resultLifetime = resultLifetime ?? DefaultResultLifetime;
        _logger = logger;

        if (_probeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(probeInterval));
        }

        if (_resultLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(resultLifetime));
        }
    }

    /// <summary>
    /// Devuelve la última entrada medida para <paramref name="sessionId"/> si hay
    /// un resultado de sonda fresco; en caso contrario programa una sonda en
    /// segundo plano (respetando el intervalo) y devuelve false.
    /// </summary>
    public bool TryGetLastInputAt(uint sessionId, out DateTimeOffset lastInputAt)
    {
        lastInputAt = default;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool startProbe = false;

        lock (_gate)
        {
            if (_cachedSessionId == sessionId
                && _cachedAt != DateTimeOffset.MinValue
                && now - _cachedAt <= _resultLifetime)
            {
                lastInputAt = _cachedLastInputAt;
                return true;
            }

            if (!_probeInFlight && now - _lastProbeStartedAt >= _probeInterval)
            {
                _lastProbeStartedAt = now;
                _probeInFlight = true;
                startProbe = true;
            }
        }

        if (startProbe)
        {
            _ = Task.Run(() => ProbeOnce(sessionId));
        }

        return false;
    }

    /// <summary>
    /// Ejecuta la sonda de forma síncrona y actualiza la caché. En producción se
    /// invoca desde el segundo plano; es público para poder probar la lógica de
    /// caché de forma determinista.
    /// </summary>
    public void ProbeOnce(uint sessionId)
    {
        DateTimeOffset? result = null;
        Exception? failure = null;

        try
        {
            result = _probe(sessionId);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool logFailure = false;

        lock (_gate)
        {
            _probeInFlight = false;

            if (result is { } value)
            {
                _cachedSessionId = sessionId;
                _cachedLastInputAt = value;
                _cachedAt = now;
            }
            else if (now - _lastFailureLoggedAt >= FailureLogThrottle)
            {
                _lastFailureLoggedAt = now;
                logFailure = true;
            }
        }

        if (logFailure)
        {
            if (failure is not null)
            {
                _logger?.LogError(
                    failure,
                    "La sonda de entrada de usuario del Servicio falló (se registra como máximo una vez por minuto).");
            }
            else
            {
                _logger?.LogError(
                    "La sonda de entrada de usuario del Servicio no obtuvo un valor (se registra como máximo una vez por minuto).");
            }
        }
    }

    private static DateTimeOffset? ProbeNative(uint sessionId)
    {
        (DateTimeOffset? lastInputAt, string? error) =
            ConsoleInputProbeRunner.TryReadLastInput(sessionId, ConsoleInputProbeRunner.DefaultTimeout);

        if (lastInputAt is null)
        {
            throw new InvalidOperationException(error ?? "Sonda de entrada sin resultado.");
        }

        return lastInputAt;
    }
}
