namespace ControlTime.Infrastructure.Windows;

public static class WindowsConsoleSessionId
{
    public static int? TryGet()
    {
        try
        {
            uint sessionId = WtsApi.WTSGetActiveConsoleSessionId();

            return sessionId == WtsApi.NoActiveConsoleSession
                ? null
                : (int)sessionId;
        }
        catch
        {
            return null;
        }
    }
}
