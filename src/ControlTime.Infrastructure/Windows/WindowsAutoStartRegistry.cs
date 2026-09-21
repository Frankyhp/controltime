using ControlTime.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ControlTime.Infrastructure.Windows;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartRegistry(ILogger<WindowsAutoStartRegistry>? logger = null) : IAutostartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ControlTime";

    private readonly ILogger<WindowsAutoStartRegistry>? _logger = logger;

    public void Enable()
    {
        string? executablePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executablePath))
        {
            throw new InvalidOperationException(
                "No se pudo determinar la ruta del ejecutable de ControlTime.");
        }

        using RegistryKey key = OpenRunKey(writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\"");

        _logger?.LogInformation(
            "Arranque automático activado: {ValueName} -> {ExecutablePath}.",
            ValueName,
            executablePath);
    }

    public void Disable()
    {
        using RegistryKey key = OpenRunKey(writable: true);

        if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger?.LogInformation("Arranque automático desactivado.");
        }
    }

    public bool IsEnabled()
    {
        using RegistryKey key = OpenRunKey(writable: false);
        return key.GetValue(ValueName) is not null;
    }

    private static RegistryKey OpenRunKey(bool writable)
    {
        RegistryKey? baseKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable);

        return baseKey
            ?? throw new InvalidOperationException(
                $"No se pudo abrir la clave de registro '{RunKeyPath}'.");
    }
}
