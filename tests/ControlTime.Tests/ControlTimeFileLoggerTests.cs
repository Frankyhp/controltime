using ControlTime.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace ControlTime.Tests;

public sealed class ControlTimeFileLoggerTests : IDisposable
{
    private readonly string _directory;

    public ControlTimeFileLoggerTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"controltime-logs-{Guid.NewGuid():N}");
    }

    [Fact]
    public void Log_Information_WritesLineToDailyFile()
    {
        using var provider = new ControlTimeFileLoggerProvider(_directory);
        ILogger logger = provider.CreateLogger("ControlTime.Tests.Sample");

        logger.LogInformation("Mensaje de prueba {Value}", 42);
        logger.LogCritical(new InvalidOperationException("fallo de ejemplo"), "Fallo de prueba");

        string[] files = Directory.GetFiles(_directory, "controltime-*.log");
        var file = Assert.Single(files);
        string content = File.ReadAllText(file);

        Assert.Contains("INFORMATION ControlTime.Tests.Sample: Mensaje de prueba 42", content);
        Assert.Contains("CRITICAL ControlTime.Tests.Sample: Fallo de prueba", content);
        Assert.Contains("InvalidOperationException: fallo de ejemplo", content);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    public void IsEnabled_BelowInformation_ReturnsFalse(LogLevel level)
    {
        using var provider = new ControlTimeFileLoggerProvider(_directory);
        ILogger logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(level));

        logger.Log(level, "no debe escribirse");

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void IsEnabled_InformationAndAbove_ReturnsTrue()
    {
        using var provider = new ControlTimeFileLoggerProvider(_directory);
        ILogger logger = provider.CreateLogger("Test");

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Log_OldRetentionFiles_AreDeletedOnWrite()
    {
        Directory.CreateDirectory(_directory);

        string stale = Path.Combine(_directory, "controltime-stale-20000101.log");
        File.WriteAllText(stale, "antiguo");

        using (var provider = new ControlTimeFileLoggerProvider(_directory))
        {
            provider.CreateLogger("Test").LogInformation("refresco");
        }

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void Log_RecentFiles_AreRetained()
    {
        Directory.CreateDirectory(_directory);

        DateOnly yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        string fresh = Path.Combine(
            _directory,
            $"controltime-stale-{yesterday:yyyyMMdd}.log");
        File.WriteAllText(fresh, "reciente");

        using (var provider = new ControlTimeFileLoggerProvider(_directory))
        {
            provider.CreateLogger("Test").LogInformation("refresco");
        }

        Assert.True(File.Exists(fresh));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
