namespace ControlTime.Tests;

public class RuleEngineTests
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
    public void Evaluate_EmptyRuleSet_Blocked()
    {
        var engine = new RuleEngine();

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.Zero, []);

        Assert.Equal(AccessState.Blocked, result.State);
    }

    [Fact]
    public void Evaluate_AllRulesDisabled_Blocked()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(enabled: false);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
    }

    [Fact]
    public void Evaluate_InsideScheduleBelowLimit_AllowedWithRemaining()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(30), [rule]);

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Equal(TimeSpan.FromMinutes(90), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_ExactlyAtScheduleStart_Allowed()
    {
        var engine = new RuleEngine();
        var rule = CreateRule();

        var result = engine.Evaluate(At(DayOfWeek.Monday, 9), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Allowed, result.State);
    }

    [Fact]
    public void Evaluate_ExactlyAtScheduleEnd_BlockedWithNextAllowedTomorrow()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(startHour: 9, endHour: 21);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 21), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal(At(DayOfWeek.Tuesday, 9), result.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_OneMinuteBeforeScheduleStart_BlockedNextAllowedSameDay()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(startHour: 9, endHour: 21);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 8, 59), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal(At(DayOfWeek.Monday, 9), result.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_OneMinuteBeforeScheduleEnd_Allowed()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(startHour: 9, endHour: 21);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 20, 59), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Allowed, result.State);
    }

    [Fact]
    public void Evaluate_ForbiddenDay_BlockedNextAllowedNextAllowedDay()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(days: [DayOfWeek.Tuesday], startHour: 17, endHour: 21);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal(At(DayOfWeek.Tuesday, 17), result.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_FridayForbiddenMondayOnly_NextAllowedWrapsWeekend()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(days: [DayOfWeek.Monday], startHour: 9, endHour: 21);

        var result = engine.Evaluate(At(DayOfWeek.Friday, 12), TimeSpan.Zero, [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal(At(DayOfWeek.Monday, 9).AddDays(7), result.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_DailyLimitNotReached_Allowed()
    {
        var engine = new RuleEngine(warningThreshold: TimeSpan.Zero);
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(119), [rule]);

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Equal(TimeSpan.FromMinutes(1), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_DailyLimitReachedExactly_Blocked()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(120), [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
    }

    [Fact]
    public void Evaluate_DailyLimitExceeded_Blocked()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(180), [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
    }

    [Fact]
    public void Evaluate_LimitReachedInsideSchedule_NextAllowedStartsAfterMidnight()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120, startHour: 8, endHour: 22);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(120), [rule]);

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal(At(DayOfWeek.Tuesday, 8), result.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_RemainingBelowDefaultThreshold_Warning()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(119), [rule]);

        Assert.Equal(AccessState.Warning, result.State);
        Assert.Equal(TimeSpan.FromMinutes(1), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_RemainingExactlyAtThreshold_Warning()
    {
        var engine = new RuleEngine(warningThreshold: TimeSpan.FromMinutes(5));
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(115), [rule]);

        Assert.Equal(AccessState.Warning, result.State);
    }

    [Fact]
    public void Evaluate_RemainingJustAboveThreshold_Allowed()
    {
        var engine = new RuleEngine(warningThreshold: TimeSpan.FromMinutes(5));
        var rule = CreateRule(limitMinutes: 120);

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(115).Subtract(TimeSpan.FromSeconds(1)),
            [rule]);

        Assert.Equal(AccessState.Allowed, result.State);
    }

    [Fact]
    public void Evaluate_UnlimitedRuleInsideSchedule_AllowedWithoutRemaining()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(limitMinutes: null);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromHours(10), [rule]);

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Null(result.RemainingTime);
    }

    [Fact]
    public void Evaluate_UnlimitedRuleSatisfied_LimitedRuleStateIrrelevant()
    {
        var engine = new RuleEngine();
        var limited = CreateRule(name: "Limited", limitMinutes: 120);
        var unlimited = CreateRule(name: "Unlimited");

        var result = engine.Evaluate(
            At(DayOfWeek.Monday, 12),
            TimeSpan.FromMinutes(119),
            [limited, unlimited]);

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Null(result.RemainingTime);
    }

    [Fact]
    public void Evaluate_MultipleSatisfiedRules_RemainingIsMostPermissive()
    {
        var engine = new RuleEngine(warningThreshold: TimeSpan.Zero);
        var tight = CreateRule(name: "Tight", limitMinutes: 30);
        var loose = CreateRule(name: "Loose", limitMinutes: 120);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(20), [tight, loose]);

        Assert.Equal(AccessState.Allowed, result.State);
        Assert.Equal(TimeSpan.FromMinutes(100), result.RemainingTime);
    }

    [Fact]
    public void Evaluate_OneRuleForbidsNowOtherAllows_Allowed()
    {
        var engine = new RuleEngine();
        var weekdayOnly = CreateRule(name: "Weekday", days: [DayOfWeek.Monday]);
        var weekendRule = CreateRule(name: "Weekend", days: [DayOfWeek.Saturday]);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.Zero, [weekdayOnly, weekendRule]);

        Assert.Equal(AccessState.Allowed, result.State);
    }

    [Fact]
    public void Evaluate_MultipleBlockedRules_NextAllowedIsEarliest()
    {
        var engine = new RuleEngine();
        var evening = CreateRule(name: "Evening", startHour: 18, endHour: 22);
        var morning = CreateRule(name: "Morning", startHour: 7, endHour: 8);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.Zero, [evening, morning]);

        Assert.Equal(AccessState.Blocked, result.State);
        Assert.Equal(At(DayOfWeek.Monday, 18), result.NextAllowedAt);
    }

    [Fact]
    public void Evaluate_DisabledRuleWouldHaveBlockedEnabledRuleStateUnaffected()
    {
        var engine = new RuleEngine();
        var active = CreateRule(name: "Active", limitMinutes: 120);
        var disabledLimitReached = CreateRule(name: "Disabled", enabled: false, limitMinutes: 1);

        var result = engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromHours(1), [active, disabledLimitReached]);

        Assert.Equal(AccessState.Allowed, result.State);
    }

    [Fact]
    public void Evaluate_MidnightSchedule_StartInclusiveEndExclusive()
    {
        var engine = new RuleEngine();
        var rule = CreateRule(startHour: 0, endHour: 6);

        Assert.Equal(AccessState.Allowed, engine.Evaluate(At(DayOfWeek.Wednesday, 0), TimeSpan.Zero, [rule]).State);
        Assert.Equal(
            AccessState.Allowed,
            engine.Evaluate(At(DayOfWeek.Wednesday, 5, 59), TimeSpan.Zero, [rule]).State);
        Assert.Equal(
            AccessState.Blocked,
            engine.Evaluate(At(DayOfWeek.Wednesday, 6), TimeSpan.Zero, [rule]).State);
    }

    [Fact]
    public void Evaluate_NegativeUsage_Throws()
    {
        var engine = new RuleEngine();
        var rule = CreateRule();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.FromMinutes(-1), [rule]));
    }

    [Fact]
    public void Evaluate_NullRules_Throws()
    {
        var engine = new RuleEngine();

        Assert.Throws<ArgumentNullException>(
            () => engine.Evaluate(At(DayOfWeek.Monday, 12), TimeSpan.Zero, null!));
    }

    [Fact]
    public void Constructor_NegativeWarningThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuleEngine(TimeSpan.FromMinutes(-1)));
    }
}
