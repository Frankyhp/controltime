using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

internal static class WindowsNamedPipes
{
    public const string SystemAdminsInteractiveSddl = "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GWGR;;;IU)";

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private const int FileFlagOverlapped = unchecked((int)0x40000000);
    private const int PipeAccessDuplex = 0x00000003;
    private const int PipeTypeByte = 0x00000000;
    private const int PipeWait = 0x00000000;
    private const int PipeUnlimitedInstances = 255;
    private const int SddlRevision = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
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
    private static extern IntPtr CreateNamedPipeW(
        string pipeName,
        int openMode,
        int pipeMode,
        int maxInstances,
        int outBufferSize,
        int inBufferSize,
        int defaultTimeOut,
        ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    public static bool TryGetClientProcessId(SafeHandle handle, out int processId)
    {
        processId = 0;

        if (handle.IsInvalid ||
            !GetNamedPipeClientProcessId(handle.DangerousGetHandle(), out uint clientProcessId))
        {
            return false;
        }

        processId = (int)clientProcessId;
        return true;
    }

    public static IntPtr CreatePipe(string pipeName, string sddl)
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

            IntPtr handle = CreateNamedPipeW(
                $@"\\.\pipe\{pipeName}",
                PipeAccessDuplex | FileFlagOverlapped,
                PipeTypeByte | PipeWait,
                PipeUnlimitedInstances,
                0,
                0,
                0,
                ref attributes);

            if (handle != InvalidHandleValue)
            {
                return handle;
            }

            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            LocalFree(securityDescriptor);
        }
    }
}
