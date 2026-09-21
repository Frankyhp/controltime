using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

internal static class SessionLockProbe
{
    private const uint MaximumAllowed = 0x02000000;
    private const int DesktopNameInformation = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformation(
        IntPtr handle,
        int index,
        IntPtr info,
        uint length,
        out uint lengthRequired);

    public static bool IsLocked()
    {
        IntPtr desktop = OpenInputDesktop(0, false, MaximumAllowed);
        if (desktop == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            string name = GetDesktopName(desktop);
            return name.Equals("Winlogon", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Disconnect", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    private static string GetDesktopName(IntPtr desktop)
    {
        IntPtr buffer = Marshal.AllocHGlobal(512);
        try
        {
            if (!GetUserObjectInformation(desktop, DesktopNameInformation, buffer, 512, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
