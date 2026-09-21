namespace ControlTime.Tests;

public class WarningNotifierTests
{
    private sealed class LocalTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingNotifier : INotifier
    {
        public List<UsageWarning> Warnings { get; } = [];

        public void Notify(UsageWarning warning) => Warnings.Add(warning);
    }

    private static readonly DateTimeOffset BaseDay =
        new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);

    private readonly LocalTimeProvider _clock = new(BaseDay);
    private readonly RecordingNotifier _notifier = new();
    private readonly WarningNotifier _notifierService;

    public WarningNotifierTests()
    {
        _notifierService = new WarningNotifier(new WarningEvaluator(), _notifier, _clock);
    }

    private static MonitorState State(AccessState state, TimeSpan? remaining) =>
        new(
            new ActivityStatus(ActivityState.Active, BaseDay, BaseDay),
            new DashboardSnapshot(
                state,
                null,
                remaining,
                null,
                TimeSpan.FromHours(2),
                TimeSpan.Zero,
                null,
                true,
                TimeSpan.FromHours(1)));

    private void Process(AccessState state, TimeSpan? remaining) =>
        _notifierService.Process(State(state, remaining));

    [Fact]
    public void NotifiesOnceWhenThresholdCrossed()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(16));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(12));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(10));

        Assert.Single(_notifier.Warnings);
        Assert.Equal(15, _notifier.Warnings[0].Threshold.Minutes);
    }

    [Fact]
    public void NotifiesEachThresholdOnce()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(14));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(4));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(1));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(30));

        Assert.Equal(3, _notifier.Warnings.Count);
        Assert.Equal([15, 5, 1], _notifier.Warnings.Select(w => w.Threshold.Minutes));
    }

    [Fact]
    public void MultipleThresholdsCrossedInOneTick_NotifyOnlyMostUrgent()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(16));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(4));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(2));

        Assert.Single(_notifier.Warnings);
        Assert.Equal(5, _notifier.Warnings[0].Threshold.Minutes);
    }

    [Fact]
    public void MidnightResetsDailyDeduplication()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));
        Assert.Single(_notifier.Warnings);

        _clock.Now = BaseDay.AddDays(1).AddHours(-9);
        Process(AccessState.Allowed, TimeSpan.FromMinutes(20));

        _clock.Now = BaseDay.AddDays(1);
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));

        Assert.Equal(2, _notifier.Warnings.Count);
    }

    [Fact]
    public void BonusRaisingRemainingAboveThresholdReArmsIt()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(20));
        Process(AccessState.Allowed, TimeSpan.FromMinutes(14));

        Assert.Equal(2, _notifier.Warnings.Count);
        Assert.Equal([15, 15], _notifier.Warnings.Select(w => w.Threshold.Minutes));
    }

    [Fact]
    public void BlockedStateDoesNotNotifyOrReset()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));
        Process(AccessState.Blocked, null);
        Process(AccessState.Allowed, TimeSpan.FromMinutes(14));

        Assert.Single(_notifier.Warnings);
    }

    [Fact]
    public void UnlimitedAllowedClearsDeduplication()
    {
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));
        Process(AccessState.Allowed, null);
        Process(AccessState.Allowed, TimeSpan.FromMinutes(15));

        Assert.Equal(2, _notifier.Warnings.Count);
    }

    [Fact]
    public void WarningCarriesRemainingAndEffectiveAllowance()
    {
        _notifierService.Process(new MonitorState(
            new ActivityStatus(ActivityState.Active, BaseDay, BaseDay),
            new DashboardSnapshot(
                AccessState.Warning,
                null,
                TimeSpan.FromMinutes(3),
                null,
                TimeSpan.FromHours(2),
                TimeSpan.FromMinutes(30),
                null,
                true,
                TimeSpan.FromHours(2))));

        UsageWarning warning = Assert.Single(_notifier.Warnings);
        Assert.Equal(5, warning.Threshold.Minutes);
        Assert.Equal(TimeSpan.FromMinutes(3), warning.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(150), warning.EffectiveDailyAllowance);
    }
}
