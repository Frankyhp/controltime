namespace ControlTime.Tests;

public class UsageRuleTests
{
    private static AllowedSchedule ValidSchedule()
    {
        var result = AllowedSchedule.TryCreate(new TimeOnly(17, 0), new TimeOnly(21, 0), out var schedule);
        Assert.True(result.Success, result.Error);
        return schedule!;
    }

    private static IReadOnlySet<DayOfWeek> Weekdays() =>
        new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday };

    [Fact]
    public void TryCreate_ValidRule_Succeeds()
    {
        var result = UsageRule.TryCreate(
            "School nights",
            enabled: true,
            Weekdays(),
            ValidSchedule(),
            TimeSpan.FromMinutes(120),
            out var rule);

        Assert.True(result.Success);
        Assert.NotNull(rule);
        Assert.Equal("School nights", rule!.Name);
        Assert.True(rule.Enabled);
        Assert.Equal(2, rule.AllowedDays.Count);
        Assert.Equal(TimeSpan.FromMinutes(120), rule.DailyLimit);
    }

    [Fact]
    public void TryCreate_DisabledRule_Succeeds()
    {
        var result = UsageRule.TryCreate(
            "Disabled rule",
            enabled: false,
            Weekdays(),
            ValidSchedule(),
            TimeSpan.FromMinutes(60),
            out var rule);

        Assert.True(result.Success);
        Assert.NotNull(rule);
        Assert.False(rule!.Enabled);
    }

    [Fact]
    public void TryCreate_NullDailyLimit_SucceedsAsUnlimited()
    {
        var result = UsageRule.TryCreate(
            "Unlimited",
            enabled: true,
            Weekdays(),
            ValidSchedule(),
            dailyLimit: null,
            out var rule);

        Assert.True(result.Success);
        Assert.Null(rule!.DailyLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-120)]
    public void TryCreate_NonPositiveDailyLimit_Fails(int minutes)
    {
        var result = UsageRule.TryCreate(
            "Bad limit",
            enabled: true,
            Weekdays(),
            ValidSchedule(),
            TimeSpan.FromMinutes(minutes),
            out var rule);

        Assert.True(result.Failed);
        Assert.Null(rule);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_BlankName_Fails(string name)
    {
        var result = UsageRule.TryCreate(
            name,
            enabled: true,
            Weekdays(),
            ValidSchedule(),
            TimeSpan.FromMinutes(60),
            out var rule);

        Assert.True(result.Failed);
        Assert.Null(rule);
    }

    [Fact]
    public void TryCreate_EmptyAllowedDays_Fails()
    {
        var result = UsageRule.TryCreate(
            "No days",
            enabled: true,
            new HashSet<DayOfWeek>(),
            ValidSchedule(),
            TimeSpan.FromMinutes(60),
            out var rule);

        Assert.True(result.Failed);
        Assert.Null(rule);
    }

    [Fact]
    public void TryCreate_TrimsName()
    {
        var result = UsageRule.TryCreate(
            "  Weekends  ",
            enabled: true,
            new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday },
            ValidSchedule(),
            TimeSpan.FromMinutes(60),
            out var rule);

        Assert.True(result.Success);
        Assert.Equal("Weekends", rule!.Name);
    }

    [Fact]
    public void TryCreate_MinutePrecisionDailyLimit_Succeeds()
    {
        var result = UsageRule.TryCreate(
            "One second limit",
            enabled: true,
            Weekdays(),
            ValidSchedule(),
            TimeSpan.FromSeconds(1),
            out var rule);

        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(1), rule!.DailyLimit);
    }
}
