using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Logging;

public static class ControlTimeLoggingExtensions
{
    public static ILoggingBuilder AddControlTimeFileLogs(
        this ILoggingBuilder builder,
        string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (directory is null)
        {
            ControlTimeStorage.EnsureLogDirectory();
        }

        return builder.AddProvider(new ControlTimeFileLoggerProvider(directory));
    }
}
