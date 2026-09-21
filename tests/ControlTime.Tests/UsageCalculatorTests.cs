using ControlTime.Core.Services;

namespace ControlTime.Tests;

public class UsageCalculatorTests
{
    private static readonly DateTimeOffset MondayStart = new(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset MondayEnd = new(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Monday(int hour, int minute = 0) =>
        MondayStart.AddHours(hour).AddMinutes(minute);

    private static UsageSession Session(int startHour, int startMinute, int endHour, int endMinute)
    {
        var result = UsageSession.TryCreate(
            Monday(startHour, startMinute),
            Monday(endHour, endMinute),
            out var session);
        Assert.True(result.Success, result.Error);

        return session!;
    }

    [Fact]
    public void GetUsageForDay_NoSessions_ReturnsZero()
    {
        var usage = UsageCalculator.GetUsageForDay([], MondayStart, MondayEnd);

        Assert.Equal(TimeSpan.Zero, usage);
    }

    [Fact]
    public void GetUsageForDay_SessionEntirelyInside_CountsFullDuration()
    {
        var usage = UsageCalculator.GetUsageForDay([Session(9, 0, 10, 30)], MondayStart, MondayEnd);

        Assert.Equal(TimeSpan.FromMinutes(90), usage);
    }

    [Fact]
    public void GetUsageForDay_SessionsOutsideDay_ReturnsZero()
    {
        var previousDay = UsageSession.TryCreate(
            MondayStart.AddMinutes(-60), MondayStart.AddMinutes(-15), out var before);
        Assert.True(previousDay.Success);

        var nextDay = UsageSession.TryCreate(
            MondayEnd, MondayEnd.AddMinutes(60), out var after);
        Assert.True(nextDay.Success);

        var usage = UsageCalculator.GetUsageForDay([before!, after!], MondayStart, MondayEnd);

        Assert.Equal(TimeSpan.Zero, usage);
    }

    [Fact]
    public void GetUsageForDay_MultipleSessions_SumsDurations()
    {
        var usage = UsageCalculator.GetUsageForDay(
            [Session(9, 0, 10, 0), Session(12, 30, 13, 45)],
            MondayStart,
            MondayEnd);

        Assert.Equal(TimeSpan.FromMinutes(135), usage);
    }

    [Fact]
    public void GetUsageForDay_MidnightCrossing_SplitsAcrossBothDays()
    {
        var session = Session(23, 30, 24, 30);

        var mondayUsage = UsageCalculator.GetUsageForDay([session], MondayStart, MondayEnd);
        var tuesdayUsage = UsageCalculator.GetUsageForDay([session], MondayEnd, MondayEnd.AddDays(1));

        Assert.Equal(TimeSpan.FromMinutes(30), mondayUsage);
        Assert.Equal(TimeSpan.FromMinutes(30), tuesdayUsage);
    }

    [Fact]
    public void GetUsageForDay_MultiDaySession_ClampedToEachDay()
    {
        var result = UsageSession.TryCreate(
            Monday(22, 0),
            MondayStart.AddDays(2).AddHours(1),
            out var session);
        Assert.True(result.Success, result.Error);

        var day1 = UsageCalculator.GetUsageForDay([session!], MondayStart, MondayEnd);
        var day2 = UsageCalculator.GetUsageForDay([session!], MondayEnd, MondayEnd.AddDays(1));
        var day3 = UsageCalculator.GetUsageForDay([session!], MondayEnd.AddDays(1), MondayEnd.AddDays(2));

        Assert.Equal(TimeSpan.FromHours(2), day1);
        Assert.Equal(TimeSpan.FromHours(24), day2);
        Assert.Equal(TimeSpan.FromHours(1), day3);
    }

    [Fact]
    public void GetUsageForDay_PartialOverlapsOnBothEdges_CountsOnlyInsideWindow()
    {
        var result = UsageSession.TryCreate(
            MondayStart.AddMinutes(-30),
            MondayEnd.AddMinutes(30),
            out var session);
        Assert.True(result.Success, result.Error);

        var usage = UsageCalculator.GetUsageForDay([session!], MondayStart, MondayEnd);

        Assert.Equal(TimeSpan.FromHours(24), usage);
    }

    [Fact]
    public void GetUsageForDay_OpenSession_Ignored()
    {
        UsageSession.TryCreateOpen(Monday(9, 0), out var open);

        var usage = UsageCalculator.GetUsageForDay([open!], MondayStart, MondayEnd);

        Assert.Equal(TimeSpan.Zero, usage);
    }

    [Fact]
    public void GetUsageForDay_InvertedWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UsageCalculator.GetUsageForDay([], MondayEnd, MondayStart));
    }
}
