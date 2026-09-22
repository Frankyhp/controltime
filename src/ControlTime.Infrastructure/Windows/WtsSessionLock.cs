using System.ComponentModel;
using System.Runtime.InteropServices;
using ControlTime.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Windows;

public sealed class WtsSessionLock : ISessionLock
{
    private readonly ILogger<WtsSessionLock>? _logger;

    public WtsSessionLock(ILogger<WtsSessionLock>? logger = null)
    {
        _logger = logger;
    }

    public bool TryLock()
    {
        try
        {
            uint sessionId = WtsApi.WTSGetActiveConsoleSessionId();

            if (sessionId == WtsApi.NoActiveConsoleSession)
            {
                _logger?.LogInformation(
                    "No hay sesión consola activa que bloquear; nada que hacer.");
                return true;
            }

            if (WtsApi.TryGetConnectState(
                    sessionId,
                    out WtsApi.ConnectStateClass state)
                && state is WtsApi.ConnectStateClass.Disconnected
                    or WtsApi.ConnectStateClass.Down
                    or WtsApi.ConnectStateClass.Init)
            {
                return true;
            }

            if (WtsApi.WTSDisconnectSession(
                    WtsApi.CurrentServerHandle,
                    sessionId,
                    false))
            {
                _logger?.LogInformation(
                    "Sesión de Windows bloqueada por ControlTime (sesión {SessionId} desconectada).",
                    sessionId);
                return true;
            }

            _logger?.LogError(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "WTSDisconnectSession devolvió fallo al bloquear la sesión {SessionId}.",
                sessionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "No se pudo bloquear la sesión de Windows.");
            return false;
        }
    }
}
