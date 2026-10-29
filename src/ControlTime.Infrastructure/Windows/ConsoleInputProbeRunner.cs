using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

/// <summary>
/// Secuencia validada en el spike de la Fase 24 para medir la última entrada real
/// de la sesión consola desde el Servicio (sesión 0, LocalSystem):
/// WTSQueryUserToken → DuplicateTokenEx(primary) → LoadUserProfile →
/// CreateEnvironmentBlock → CreateProcessAsUserW (lpDesktop = NULL) de un ayudante
/// efímero que publica el FILETIME UTC en una sección compartida con DACL propia.
/// Cualquier fallo devuelve null con un mensaje de error: nunca lanza excepciones.
/// </summary>
internal static class ConsoleInputProbeRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private const uint MappingSize = 64;

    public static (DateTimeOffset? LastInputAt, string? Error) TryReadLastInput(
        uint sessionId,
        TimeSpan timeout)
    {
        IntPtr section = IntPtr.Zero;
        IntPtr view = IntPtr.Zero;
        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        bool profileLoaded = false;
        InputProbeApi.ProfileInfo profile = default;
        InputProbeApi.ProcessInformation processInfo = default;
        bool processStarted = false;

        string mapName =
            $@"Global\ControlTime.InputProbe-{Environment.ProcessId}-{Guid.NewGuid():N}";

        try
        {
            section = InputProbeApi.CreateSecureMapping(
                mapName,
                WindowsNamedPipes.SystemAdminsInteractiveSddl,
                MappingSize);
            view = MapWriteView(section);
            Marshal.WriteInt64(view, 0);

            if (!WtsApi.WTSQueryUserToken(sessionId, out userToken))
            {
                return Failure("WTSQueryUserToken");
            }

            if (!InputProbeApi.DuplicateTokenEx(
                    userToken,
                    InputProbeApi.MaximumAllowed,
                    IntPtr.Zero,
                    InputProbeApi.SecurityImpersonation,
                    InputProbeApi.TokenTypePrimary,
                    out primaryToken))
            {
                return Failure("DuplicateTokenEx");
            }

            profile.dwSize = Marshal.SizeOf<InputProbeApi.ProfileInfo>();
            profile.lpUserName = WtsApi.TryGetUserName(sessionId);

            if (profile.lpUserName is null)
            {
                return (null, "No se pudo obtener el nombre de usuario de la sesión consola.");
            }

            if (!InputProbeApi.LoadUserProfile(primaryToken, ref profile))
            {
                return Failure("LoadUserProfile");
            }

            profileLoaded = true;

            if (!InputProbeApi.CreateEnvironmentBlock(out environment, primaryToken, false))
            {
                return Failure("CreateEnvironmentBlock");
            }

            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                return (null, "No se pudo resolver la ruta del ejecutable del Servicio.");
            }

            var startupInfo = new InputProbeApi.StartupInfoW
            {
                cb = Marshal.SizeOf<InputProbeApi.StartupInfoW>(),
                // lpDesktop se deja NULL a propósito (spike Fase 24).
            };

            string commandLine =
                $"\"{executablePath}\" {UserInputProbeChild.ProbeModeArgument} {mapName}";

            if (!InputProbeApi.CreateProcessAsUserW(
                    primaryToken,
                    applicationName: null,
                    commandLine: commandLine,
                    processAttributes: IntPtr.Zero,
                    threadAttributes: IntPtr.Zero,
                    inheritHandles: false,
                    InputProbeApi.CreateNoWindow | InputProbeApi.CreateUnicodeEnvironment,
                    environment,
                    Path.GetDirectoryName(executablePath),
                    ref startupInfo,
                    out processInfo))
            {
                return Failure("CreateProcessAsUserW");
            }

            processStarted = true;

            uint waitResult = InputProbeApi.WaitForSingleObject(
                processInfo.hProcess,
                (uint)Math.Clamp(timeout.TotalMilliseconds, 1, uint.MaxValue - 1));

            if (waitResult != InputProbeApi.WaitObject0)
            {
                InputProbeApi.TerminateProcess(processInfo.hProcess, 1);
                return (null,
                    $"El ayudante de la sonda de entrada no respondió en {timeout.TotalSeconds:0.#} s.");
            }

            long lastInputFileTime = Marshal.ReadInt64(view);

            if (lastInputFileTime <= 0)
            {
                return (null, "El ayudante de la sonda de entrada no publicó un valor válido.");
            }

            return (DateTimeOffset.FromFileTime(lastInputFileTime), null);
        }
        catch (Exception ex)
        {
            return (null, $"Fallo la sonda de entrada del Servicio: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (profileLoaded)
            {
                InputProbeApi.UnloadUserProfile(primaryToken, profile.hProfile);
            }

            if (environment != IntPtr.Zero)
            {
                InputProbeApi.DestroyEnvironmentBlock(environment);
            }

            if (processStarted)
            {
                if (processInfo.hThread != IntPtr.Zero)
                {
                    InputProbeApi.CloseHandle(processInfo.hThread);
                }

                if (processInfo.hProcess != IntPtr.Zero)
                {
                    InputProbeApi.CloseHandle(processInfo.hProcess);
                }
            }

            if (primaryToken != IntPtr.Zero)
            {
                InputProbeApi.CloseHandle(primaryToken);
            }

            if (userToken != IntPtr.Zero)
            {
                InputProbeApi.CloseHandle(userToken);
            }

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

    private static IntPtr MapWriteView(IntPtr section)
    {
        IntPtr view = InputProbeApi.MapViewOfFile(
            section,
            InputProbeApi.FileMapWrite,
            0,
            0,
            new UIntPtr(MappingSize));

        if (view == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return view;
    }

    private static (DateTimeOffset?, string) Failure(string api) =>
        (null, $"{api} devolvió fallo ({new Win32Exception(Marshal.GetLastWin32Error()).Message}).");
}
