using System.ComponentModel;
using System.Runtime.InteropServices;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Windows;

public sealed class WtsConsoleSessionMonitor : IActivityMonitor
{
    private readonly ILogger<WtsConsoleSessionMonitor>? _logger;

    public WtsConsoleSessionMonitor(ILogger<WtsConsoleSessionMonitor>? logger = null)
    {
        _logger = logger;
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
                ? new ActivityStatus(ActivityState.Active, now, now)
                : Locked(now);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "No se pudo consultar la sesión consola de Windows.");
            return Locked(now);
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
