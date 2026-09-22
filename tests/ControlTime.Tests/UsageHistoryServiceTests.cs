using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.Tests;

public class UsageHistoryServiceTests
{
    // 2026-09-23 is a Wednesday. Local time (UTC+2) is 14:00 the same day.
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly DateOnly Yesterday = new(2026, 9, 22);
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Sunday = new(2026, 9, 20);

    [Fact]
    public async Task UsageIsSplitAcrossDaysAtMidnight()
    {
        UsageSession session = Session(
            LocalTime(2026, 9, 21, 23, 30),
            LocalTime(2026, 9, 22, 0, 30));

        UsageHistory history = await GetHistoryAsync(session);

        Assert.Equal(TimeSpan.FromMinutes(30), EntryOn(history, Monday).Usage);
        Assert.Equal(TimeSpan.FromMinutes(30), EntryOn(history, Yesterday).Usage);
    }

    [Fact]
    public async Task OpenSessionCountsUpToNowForToday()
    {
        UsageSession open = OpenSession(LocalTime(2026, 9, 23, 13, 0));

        UsageHistory history = await GetHistoryAsync(open);

        Assert.Equal(TimeSpan.FromHours(1), EntryOn(history, Today).Usage);
    }

    [Fact]
    public async Task BonusIsAggregatedPerDay()
    {
        var todayGrant = Grant(LocalTime(2026, 9, 23, 10, 0), 30);
        var yesterdayGrant = Grant(LocalTime(2026, 9, 22, 20, 0), 15);
        var anotherTodayGrant = Grant(LocalTime(2026, 9, 23, 11, 0), 5);

        UsageHistory history = await GetHistoryAsync(
            grants: [todayGrant, yesterdayGrant, anotherTodayGrant]);

        Assert.Equal(TimeSpan.FromMinutes(35), EntryOn(history, Today).BonusGranted);
        Assert.Equal(TimeSpan.FromMinutes(15), EntryOn(history, Yesterday).BonusGranted);
        Assert.Equal(TimeSpan.Zero, EntryOn(history, Monday).BonusGranted);
    }

    [Fact]
    public async Task GrantsOutsideRangeAreIgnored()
    {
        var olderGrant = Grant(LocalTime(2026, 9, 1, 10, 0), 45);

        UsageHistory history = await GetHistoryAsync(grants: [olderGrant]);

        Assert.All(history.Days, day => Assert.Equal(TimeSpan.Zero, day.BonusGranted));
    }

    [Fact]
    public async Task DaysCoverRangeMostRecentFirstIncludingEmptyDays()
    {
        UsageHistory history = await GetHistoryAsync();

        Assert.Equal(7, history.Days.Count);
        Assert.Equal(Today, history.Days[0].Date);
        Assert.Equal(Today.AddDays(-6), history.Days[^1].Date);
        Assert.Equal(TimeSpan.Zero, EntryOn(history, Sunday).Usage);
        Assert.Equal(history.Days[0], history.Today);
    }

    [Fact]
    public async Task CurrentWeekUsageSumsFromMonday()
    {
        // Week (from Monday): 30 m + 30 m + 30 m on Monday, 30 m on Tuesday,
        // 1 h today. The 30 m before Monday (Sunday night) is excluded.
        UsageSession crossingIntoMonday = Session(
            LocalTime(2026, 9, 20, 23, 0),
            LocalTime(2026, 9, 21, 0, 30));
        UsageSession crossingIntoTuesday = Session(
            LocalTime(2026, 9, 21, 23, 30),
            LocalTime(2026, 9, 22, 0, 30));
        UsageSession todaySession = Session(
            LocalTime(2026, 9, 23, 10, 0),
            LocalTime(2026, 9, 23, 11, 0));

        UsageHistory history = await GetHistoryAsync(
            crossingIntoMonday, crossingIntoTuesday, todaySession);

        Assert.Equal(TimeSpan.FromMinutes(150), history.CurrentWeekUsage);
    }

    [Fact]
    public async Task SessionsOutsideRangeDoNotAppear()
    {
        UsageSession old = Session(
            LocalTime(2026, 8, 1, 10, 0),
            LocalTime(2026, 8, 1, 11, 0));

        UsageHistory history = await GetHistoryAsync(old);

        Assert.Empty(history.Sessions);
    }

    private static async Task<UsageHistory> GetHistoryAsync(
        params UsageSession[] sessions)
    {
        var historyService = new UsageHistoryService(
            new FakeUsageSessionStore(sessions),
            new FakeTimeGrantStore(),
            new FixedTimeProvider(BaseTime));

        return await historyService.GetHistoryAsync(dayCount: 7, utcOffset: Offset);
    }

    private static async Task<UsageHistory> GetHistoryAsync(TimeGrant[] grants)
    {
        var historyService = new UsageHistoryService(
            new FakeUsageSessionStore(),
            new FakeTimeGrantStore(grants),
            new FixedTimeProvider(BaseTime));

        return await historyService.GetHistoryAsync(dayCount: 7, utcOffset: Offset);
    }

    private static UsageHistoryEntry EntryOn(UsageHistory history, DateOnly date) =>
        history.Days.Single(d => d.Date == date);

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, Offset);

    private static UsageSession Session(DateTimeOffset start, DateTimeOffset end)
    {
        UsageSession.TryCreate(start, end, out UsageSession? session);
        return session!;
    }

    private static UsageSession OpenSession(DateTimeOffset start)
    {
        UsageSession.TryCreateOpen(start, out UsageSession? session);
        return session!;
    }

    private static TimeGrant Grant(DateTimeOffset grantedAt, int minutes)
    {
        TimeGrant.TryCreate(grantedAt, minutes, out TimeGrant? grant);
        return grant!;
    }
}
