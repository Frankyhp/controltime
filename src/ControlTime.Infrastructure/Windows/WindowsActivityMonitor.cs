using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Windows;

public sealed class WindowsActivityMonitor : IActivityMonitor
{
    public static readonly TimeSpan DefaultIdleThreshold = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _idleThreshold;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WindowsActivityMonitor>? _logger;

    public WindowsActivityMonitor(
        TimeSpan? idleThreshold = null,
        TimeProvider? timeProvider = null,
        ILogger<WindowsActivityMonitor>? logger = null)
    {
        _idleThreshold = idleThreshold ?? DefaultIdleThreshold;
        if (_idleThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleThreshold),
                "The idle threshold must be positive.");
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public ActivityStatus GetCurrentStatus()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            bool isLocked = SessionLockProbe.IsLocked();
            DateTimeOffset lastInputAt = now - LastInputInfo.GetIdleTime();

            return ActivityEvaluator.Evaluate(now, lastInputAt, isLocked, _idleThreshold);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "No se pudo detectar la actividad de Windows.");
            throw;
        }
    }
}
