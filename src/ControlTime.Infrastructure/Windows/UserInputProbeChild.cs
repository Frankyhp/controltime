using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

/// <summary>
/// Modo ayudante efímero del propio ejecutable del Servicio
/// (<c>ControlTime.Service.exe --probe-input &lt;mapName&gt;</c>): mide la última
/// entrada real de la sesión del usuario con <see cref="GetLastInputInfo"/> y
/// publica el FILETIME UTC en la sección compartida creada por el Servicio.
/// Jamás abre la base de datos ni toca el registro ni el host genérico.
/// </summary>
public static class UserInputProbeChild
{
    public const string ProbeModeArgument = "--probe-input";

    /// <summary>Código de salida 0 si el valor quedó publicado.</summary>
    public static int Run(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return 1;
        }

        if (!mapName.StartsWith(@"Global\", StringComparison.OrdinalIgnoreCase))
        {
            mapName = $@"Global\{mapName}";
        }

        IntPtr section = IntPtr.Zero;
        IntPtr view = IntPtr.Zero;

        try
        {
            // GetLastInputInfo se consulta en el contexto del token del usuario:
            // devuelve la entrada REAL de su sesión aunque el proceso heredara la
            // window station del Servicio (spike Fase 24).
            DateTimeOffset lastInputAt = DateTimeOffset.UtcNow - LastInputInfo.GetIdleTime();
            long lastInputFileTime = lastInputAt.ToFileTime();

            section = InputProbeApi.OpenFileMappingW(InputProbeApi.FileMapWrite, false, mapName);
            if (section == IntPtr.Zero)
            {
                return 2;
            }

            view = InputProbeApi.MapViewOfFile(
                section,
                InputProbeApi.FileMapWrite,
                0,
                0,
                new UIntPtr(sizeof(long)));
            if (view == IntPtr.Zero)
            {
                return 3;
            }

            Marshal.WriteInt64(view, lastInputFileTime);
            InputProbeApi.FlushViewOfFile(view, UIntPtr.Zero);
            return 0;
        }
        catch
        {
            return 4;
        }
        finally
        {
            if (view != IntPtr.Zero)
            {
                InputProbeApi.UnmapViewOfFile(view);
            }

            if (section != IntPtr.Zero)
            {
                InputProbeApi.CloseHandle(section);
            }
        }
    }
}
