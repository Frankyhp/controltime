using ControlTime.Core.Abstractions;

namespace ControlTime.Core.Services;

public sealed class UsageService
{
    private readonly IUsageSessionStore _sessionStore;
    private readonly TimeProvider _timeProvider;

    public UsageService(IUsageSessionStore sessionStore, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);

        _sessionStore = sessionStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TimeSpan> GetUsageTodayAsync(
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.ToOffset(utcOffset).DateTime);

        return await GetUsageForDayAsync(today, utcOffset, cancellationToken);
    }

    public async Task<TimeSpan> GetUsageForDayAsync(
        DateOnly day,
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset dayStartUtc = new(day.ToDateTime(TimeOnly.MinValue), utcOffset);
        DateTimeOffset dayEndUtc = dayStartUtc.AddDays(1);

        var sessions = await _sessionStore.GetSessionsOverlappingAsync(
            dayStartUtc,
            dayEndUtc,
            cancellationToken);

        return UsageCalculator.GetUsageForDay(sessions, dayStartUtc, dayEndUtc);
    }
}
