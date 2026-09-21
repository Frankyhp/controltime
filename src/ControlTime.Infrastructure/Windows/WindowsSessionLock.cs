using System.ComponentModel;
using System.Runtime.InteropServices;
using ControlTime.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Windows;

public sealed class WindowsSessionLock : ISessionLock
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    private readonly ILogger<WindowsSessionLock>? _logger;

    public WindowsSessionLock(ILogger<WindowsSessionLock>? logger = null)
    {
        _logger = logger;
    }

    public bool TryLock()
    {
        try
        {
            if (LockWorkStation())
            {
                _logger?.LogInformation("Sesión de Windows bloqueada por ControlTime.");
                return true;
            }

            _logger?.LogError(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "LockWorkStation devolvió fallo al bloquear la sesión.");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "No se pudo bloquear la sesión de Windows.");
            return false;
        }
    }
}
