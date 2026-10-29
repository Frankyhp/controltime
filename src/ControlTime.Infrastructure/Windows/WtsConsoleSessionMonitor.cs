using System.ComponentModel;
using System.Runtime.InteropServices;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Windows;

public sealed class WtsConsoleSessionMonitor : IActivityMonitor
{
    private readonly ILogger<WtsConsoleSessionMonitor>? _logger;
    private readonly Func<uint, DateTimeOffset?>? _probedLastInputAt;
    private readonly TimeSpan _idleThreshold;

    /// <param name="probedLastInputAt">
    /// Sonda de última entrada real (Fase 24, opcional). Cuando devuelve un valor,
    /// la sesión consola activa se clasifica como Active/Idle con
    /// <see cref="ActivityEvaluator"/> en lugar de reportar Active optimista.
    /// Si devuelve null (sonda indisponible o sin resultado fresco) se mantiene el
    /// comportamiento optimista: Active con LastInputAt = ahora.
    /// </param>
    public WtsConsoleSessionMonitor(
        ILogger<WtsConsoleSessionMonitor>? logger = null,
        Func<uint, DateTimeOffset?>? probedLastInputAt = null,
        TimeSpan? idleThreshold = null)
    {
        _logger = logger;
        _probedLastInputAt = probedLastInputAt;
        _idleThreshold = idleThreshold ?? WindowsActivityMonitor.DefaultIdleThreshold;

        if (_idleThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleThreshold),
                "The idle threshold must be positive.");
        }
    }

    public ActivityStatus GetCurrentStatus()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        try
        {
            uint sessionId = WtsApi.WTSGetActiveConsoleSessionId();

            if (sessionId == WtsApi.NoActiveConsoleSession)
            {
                return Locked(now);
            }

            if (!WtsApi.TryGetConnectState(sessionId, out WtsApi.ConnectStateClass state))
            {
                LogFailure("WTSQuerySessionInformation");
                return Locked(now);
            }

            bool isActive = state is WtsApi.ConnectStateClass.Active
                or WtsApi.ConnectStateClass.Connected;

            return isActive
                ? EvaluateActive(now, ProbeLastInputAt(sessionId), _idleThreshold)
                : Locked(now);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "No se pudo consultar la sesión consola de Windows.");
            return Locked(now);
        }
    }

    /// <summary>
    /// Con sonda: Active/Idle según la última entrada medida. Sin sonda (o con la
    /// sonda caída): comportamiento optimista histórico, Active con LastInputAt = ahora.
    /// </summary>
    public static ActivityStatus EvaluateActive(
        DateTimeOffset now,
        DateTimeOffset? probedLastInputAt,
        TimeSpan idleThreshold)
    {
        if (probedLastInputAt is not { } lastInputAt)
        {
            return new ActivityStatus(ActivityState.Active, now, now);
        }

        // Un reloj adelantado en el ayudante no puede producir una entrada futura.
        return ActivityEvaluator.Evaluate(
            now,
            lastInputAt > now ? now : lastInputAt,
            isLocked: false,
            idleThreshold);
    }

    private DateTimeOffset? ProbeLastInputAt(uint sessionId)
    {
        if (_probedLastInputAt is null)
        {
            return null;
        }

        try
        {
            return _probedLastInputAt(sessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "La sonda de entrada de usuario falló al clasificar la actividad.");
            return null;
        }
    }

    private static ActivityStatus Locked(DateTimeOffset now) =>
        new(ActivityState.Locked, now, now);

    private void LogFailure(string api)
    {
        _logger?.LogError(
            new Win32Exception(Marshal.GetLastWin32Error()),
            "{Api} devolvió fallo al consultar la sesión consola de Windows.",
            api);
    }
}
