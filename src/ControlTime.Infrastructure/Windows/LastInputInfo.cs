using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

internal static class LastInputInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputStruct
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputStruct info);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputStruct
        {
            Size = (uint)Marshal.SizeOf<LastInputStruct>(),
        };

        if (!GetLastInputInfo(ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        uint idleMilliseconds = unchecked((uint)(GetTickCount64() - info.Time));
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }
}
