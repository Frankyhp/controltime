using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Logging;

public sealed partial class ControlTimeFileLoggerProvider : ILoggerProvider
{
    public const long MaxFileBytes = 10L * 1024 * 1024;

    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(14);

    private static readonly Regex LogFileNameRegex = LogFileNameRegexCore();

    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly TimeSpan _retention;

    private DateOnly _currentDate;
    private string _currentPath = string.Empty;

    public ControlTimeFileLoggerProvider(string? directory = null, TimeSpan? retention = null)
    {
        _directory = directory ?? ControlTimeStorage.LogDirectory;
        _filePrefix = "controltime-" + (Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "ControlTime");
        _retention = retention ?? DefaultRetention;

        if (_retention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => Cleanup(DateOnly.FromDateTime(DateTime.Now));

    internal bool IsLoggingEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    internal void Write<TState>(
        LogLevel logLevel,
        string category,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} {logLevel.ToString().ToUpperInvariant()} {category}: {message}");

        if (eventId.Id != 0)
        {
            line += $" [evt {eventId.Id}]";
        }

        if (exception is not null)
        {
            line += Environment.NewLine + "    " + exception;
        }

        lock (_sync)
        {
            try
            {
                DateOnly today = DateOnly.FromDateTime(now.DateTime);

                if (today != _currentDate || _currentPath.Length == 0)
                {
                    _currentDate = today;
                    Cleanup(today);
                }

                if (_currentPath.Length == 0 || LengthAtOrOverCap(_currentPath))
                {
                    _currentPath = ResolveWritablePath(today);
                }

                Directory.CreateDirectory(_directory);
                File.AppendAllText(_currentPath, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Logging must never take the process down nor throw back into callers.
            }
        }
    }

    private static bool LengthAtOrOverCap(string path)
    {
        try
        {
            return new FileInfo(path).Length >= MaxFileBytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private string ResolveWritablePath(DateOnly date)
    {
        string stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        for (int sequence = 1; ; sequence++)
        {
            string candidate = sequence == 1
                ? Path.Combine(_directory, $"{_filePrefix}-{stamp}.log")
                : Path.Combine(_directory, $"{_filePrefix}-{stamp}.{sequence}.log");

            if (!LengthAtOrOverCap(candidate))
            {
                return candidate;
            }
        }
    }

    private void Cleanup(DateOnly today)
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DateOnly cutoff = today.AddDays(-(int)_retention.TotalDays);

            foreach (string file in Directory.EnumerateFiles(_directory, "controltime-*.log"))
            {
                if (TryGetFileDate(Path.GetFileName(file), out DateOnly fileDate) && fileDate < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
        catch (IOException)
        {
        }
    }

    private static bool TryGetFileDate(string fileName, out DateOnly date)
    {
        date = default;

        Match match = LogFileNameRegex.Match(fileName);

        return match.Success
            && DateOnly.TryParseExact(
                match.Groups[1].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }

    [GeneratedRegex(@"^controltime-.*-(\d{8})(\.\d+)?\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex LogFileNameRegexCore();

    private sealed class FileLogger(
        ControlTimeFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsLoggingEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, eventId, state, exception, formatter);
            }
        }
    }
}
