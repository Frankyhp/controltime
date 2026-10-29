using System.Runtime.InteropServices;

namespace ControlTime.Infrastructure.Windows;

internal static class WtsApi
{
    public static readonly IntPtr CurrentServerHandle = IntPtr.Zero;

    public const uint NoActiveConsoleSession = 0xFFFFFFFF;

    public enum ConnectStateClass
    {
        Active = 0,
        Connected = 1,
        ConnectQuery = 2,
        Shadow = 3,
        Disconnected = 4,
        Idle = 5,
        Listen = 6,
        Reset = 7,
        Down = 8,
        Init = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public uint SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessionsW(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSDisconnectSession(
        IntPtr hServer,
        uint sessionId,
        bool bWait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr hServer,
        uint sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    private const int WtsUserName = 5;

    public static string? TryGetUserName(uint sessionId)
    {
        if (!WTSQuerySessionInformationW(
                CurrentServerHandle,
                sessionId,
                WtsUserName,
                out IntPtr buffer,
                out _))
        {
            return null;
        }

        try
        {
            string userName = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            return userName.Length > 0 ? userName : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public static bool TryGetConnectState(uint sessionId, out ConnectStateClass connectState)
    {
        connectState = ConnectStateClass.Init;

        if (!WTSEnumerateSessionsW(
                CurrentServerHandle,
                0,
                1,
                out IntPtr sessions,
                out int count))
        {
            return false;
        }

        try
        {
            int size = Marshal.SizeOf<WtsSessionInfo>();

            for (int i = 0; i < count; i++)
            {
                WtsSessionInfo info = Marshal.PtrToStructure<WtsSessionInfo>(
                    sessions + i * size);

                if (info.SessionId == sessionId)
                {
                    connectState = (ConnectStateClass)info.State;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            WTSFreeMemory(sessions);
        }
    }
}
