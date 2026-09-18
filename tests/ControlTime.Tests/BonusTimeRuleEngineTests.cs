namespace ControlTime.Tests;

public class BonusTimeRuleEngineTests
{
    private static readonly DateTimeOffset Monday = new(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);

    private static readonly DayOfWeek[] AllDays = (DayOfWeek[])Enum.GetValues(typeof(DayOfWeek));

    private static DateTimeOffset At(DayOfWeek day, int hour, int minute = 0)
    {
        return Monday.AddDays(((int)day - (int)DayOfWeek.Monday + 7) % 7)
            .AddHours(hour)
            .AddMinutes(minute);
    }

    private static UsageRule CreateRule(
        string name = "Rule",
        bool enabled = true,
        DayOfWeek[]? days = null,
        int startHour = 9,
        int endHour = 21,
        int? limitMinutes = null)
    {
        var scheduleResult = AllowedSchedule.TryCreate(
            new TimeOnly(startHour, 0),
            new TimeOnly(endHour, 0),
            out var schedule);
        Assert.True(scheduleResult.Success, scheduleResult.Error);

        var ruleResult = UsageRule.TryCreate(
            name,
            enabled,
            new HashSet<DayOfWeek>(days ?? AllDays),
            schedule!,
            limitMinutes is int m ? TimeSpan.FromMinutes(m) : null,
            out var rule);
        Assert.True(ruleResult.Success, ruleResult.Error);

        return rule!;
    }

    [Fact]
    public void Evaluate_ZeroBonus_LimitReachedStillBlocked()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(120),
            [rule],
            TimeSpan.Zero);

        Assert.Equal(AccessState.Blocked, result.State);
    }

    [Fact]
    public void Evaluate_BonusExtendsEffectiveAllowance()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(130),
            [rule],
            TimeSpan.FromMinutes(30));

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Equal(TimeSpan.FromMinutes(20), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_MultipleGrantsSumIntoAllowance()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);
        var bonus = TimeSpan.FromMinutes(30) + TimeSpan.FromMinutes(15);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(135),
            [rule],
            bonus);

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Equal(TimeSpan.FromMinutes(30), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_UsageExactlyAtLimitPlusBonus_Blocked()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(150),
            [rule],
            TimeSpan.FromMinutes(30));

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal("The daily usage limit has been reached.", result.Reason);
    }

    [Fact]
    public void Evaluate_WarningThresholdMeasuredAgainstAllowancePlusBonus()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(146),
            [rule],
            TimeSpan.FromMinutes(30));

        Assert.Equal(AccessState.Warning, result.State);
        Assert.Equal(TimeSpan.FromMinutes(4), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_UnlimitedRule_BonusDoesNotProduceRemainingTime()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: null);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromHours(9),
            [rule],
            TimeSpan.FromMinutes(30));

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Null(result.RemainingTime);
    }

    [Fact]
    public void Evaluate_BonusDoesNotBypassForbiddenDay()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(days: [DayOfWeek.Monday], limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Tuesday, 12),
            TimeSpan.Zero,
            [rule],
            TimeSpan.FromMinutes(120));

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal("Today is not an allowed day.", result.Reason);
    }

    [Fact]
    public void Evaluate_BonusDoesNotBypassScheduleWindow()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(startHour: 9, endHour: 21, limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 8),
            TimeSpan.Zero,
            [rule],
            TimeSpan.FromMinutes(120));

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal("The current time is outside the allowed schedule.", result.Reason);
    }

    [Fact]
    public void Evaluate_NegativeBonus_Throws()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.Zero,
            [rule],
            TimeSpan.FromMinutes(-1)));
    }
}
