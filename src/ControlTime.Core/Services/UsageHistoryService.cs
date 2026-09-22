using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class UsageHistoryService
{
    public const int MaximumDayCount = 90;

    private readonly IUsageSessionStore _sessionStore;
    private readonly ITimeGrantStore _grantStore;
    private readonly TimeProvider _timeProvider;

    public UsageHistoryService(
        IUsageSessionStore sessionStore,
        ITimeGrantStore grantStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(grantStore);

        _sessionStore = sessionStore;
        _grantStore = grantStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<UsageHistory> GetHistoryAsync(
        int dayCount,
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dayCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dayCount, MaximumDayCount);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.ToOffset(utcOffset).DateTime);
        DateOnly firstDay = today.AddDays(-(dayCount - 1));

        DateTimeOffset rangeStart = new(firstDay.ToDateTime(TimeOnly.MinValue), utcOffset);
        DateTimeOffset rangeEnd = rangeStart.AddDays(dayCount);

        IReadOnlyList<UsageSession> sessions = await _sessionStore.GetSessionsOverlappingAsync(
            rangeStart,
            rangeEnd,
            cancellationToken);

        IReadOnlyList<TimeGrant> grants = await _grantStore.GetGrantsInRangeAsync(
            rangeStart,
            rangeEnd,
            cancellationToken);

        List<UsageHistoryEntry> days = [];

        for (int offsetFromToday = 0; offsetFromToday < dayCount; offsetFromToday++)
        {
            DateOnly day = today.AddDays(-offsetFromToday);

            DateTimeOffset dayStart = new(day.ToDateTime(TimeOnly.MinValue), utcOffset);
            DateTimeOffset dayEnd = dayStart.AddDays(1);

            TimeSpan usage = UsageCalculator.GetUsageForDay(sessions, dayStart, dayEnd, asOf: now);

            TimeSpan bonus = TimeSpan.FromTicks(
                grants
                    .Where(g => g.GrantedAt >= dayStart && g.GrantedAt < dayEnd)
                    .Sum(g => g.Amount.Ticks));

            days.Add(new UsageHistoryEntry(day, usage, bonus));
        }

        DateOnly weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        TimeSpan currentWeekUsage = TimeSpan.FromTicks(
            days.Where(d => d.Date >= weekStart).Sum(d => d.Usage.Ticks));

        return new UsageHistory(days, currentWeekUsage, sessions, now);
    }
}
