using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

/// <summary>
/// Primitivos Win32 de la sonda de entrada de usuario (Fase 24): token de usuario
/// (WTS), perfil y entorno, lanzamiento del proceso ayudante en la sesión del usuario
/// y memoria compartida pagefile-backed para devolver el resultado.
/// </summary>
internal static class InputProbeApi
{
    public const uint MaximumAllowed = 0x02000000;
    public const int SecurityImpersonation = 2;
    public const int TokenTypePrimary = 1;

    public const uint PageReadwrite = 0x00000004;
    public const uint FileMapWrite = 0x00000002;
    public const uint WaitObject0 = 0x00000000;
    public const uint WaitTimeout = 0x00000102;

    public const uint CreateNoWindow = 0x08000100;
    public const uint CreateUnicodeEnvironment = 0x00000400;

    private const int SddlRevision = 1;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfoW
    {
        public int cb;
        public string? lpReserved;
        // Debe permanecer NULL: pasar "Winsta0\Default" desde la sesión 0 produce
        // 0xC0000142 en cualquier proceso con imports GUI (ver docs/spike-phase-24-input-probe.md).
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ProfileInfo
    {
        public int dwSize;
        public int dwFlags;
        public string? lpUserName;
        public string? lpProfilePath;
        public string? lpDefaultPath;
        public string? lpServerName;
        public string? lpPolicyPath;
        public IntPtr hProfile;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string securityDescriptor,
        int revision,
        out IntPtr securityDescriptorHandle,
        out int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileMappingW(
        IntPtr file,
        ref SecurityAttributes securityAttributes,
        uint protect,
        uint maximumSizeHigh,
        uint maximumSizeLow,
        string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenFileMappingW(
        uint desiredAccess,
        bool inheritHandle,
        string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr MapViewOfFile(
        IntPtr mapping,
        uint desiredAccess,
        uint fileOffsetHigh,
        uint fileOffsetLow,
        UIntPtr numberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UnmapViewOfFile(IntPtr view);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FlushViewOfFile(IntPtr view, UIntPtr numberOfBytesToFlush);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr handle, uint millisecondsTimeout);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint maximumAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LoadUserProfile(IntPtr token, ref ProfileInfo profileInfo);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool UnloadUserProfile(IntPtr token, IntPtr profile);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(
        out IntPtr environment,
        IntPtr token,
        bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUserW(
        IntPtr token,
        string? applicationName,
        string? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoW startupInfo,
        out ProcessInformation processInformation);

    /// <summary>
    /// Crea una sección pagefile-backed con DACL explícita (solo el creador y los
    /// usuarios autenticados enumerados en la SDDL acceden a ella).
    /// </summary>
    public static IntPtr CreateSecureMapping(string name, string sddl, uint size)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                sddl,
                SddlRevision,
                out IntPtr securityDescriptor,
                out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = false,
            };

            IntPtr mapping = CreateFileMappingW(
                InvalidHandleValue,
                ref attributes,
                PageReadwrite,
                0,
                size,
                name);

            if (mapping != IntPtr.Zero)
            {
                return mapping;
            }

            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            LocalFree(securityDescriptor);
        }
    }
}
