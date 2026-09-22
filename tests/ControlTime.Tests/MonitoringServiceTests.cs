namespace ControlTime.Tests;

public class MonitoringServiceTests
{
    private static readonly DateTimeOffset Monday = new(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);

    private static readonly DayOfWeek[] AllDays = (DayOfWeek[])Enum.GetValues(typeof(DayOfWeek));

    private static readonly TimeSpan Utc = TimeSpan.Zero;

    private static DateTimeOffset At(int hour, int minute = 0) => Monday.AddHours(hour).AddMinutes(minute);

    private static ActivityStatus Status(ActivityState state, DateTimeOffset lastInputAt, DateTimeOffset timestamp)
        => new(state, lastInputAt, timestamp);

    private static UsageRule CreateRule(int limitMinutes = 120)
    {
        var scheduleResult = AllowedSchedule.TryCreate(
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            out var schedule);
        Assert.True(scheduleResult.Success, scheduleResult.Error);

        var ruleResult = UsageRule.TryCreate(
            "Rule",
            true,
            new HashSet<DayOfWeek>(AllDays),
            schedule!,
            TimeSpan.FromMinutes(limitMinutes),
            out var rule);
        Assert.True(ruleResult.Success, ruleResult.Error);

        return rule!;
    }

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset now, params UsageRule[] rules)
        {
            SessionStore = new FakeUsageSessionStore();
            RuleStore = new FakeUsageRuleStore(rules);
            GrantStore = new FakeTimeGrantStore();
            Monitor = new FakeActivityMonitor();

            TimeProvider = new FixedTimeProvider(now);

            DashboardService = new DashboardService(
                RuleStore,
                GrantStore,
                new UsageService(SessionStore, TimeProvider),
                new RuleEngine(),
                TimeProvider);

            Service = new MonitoringService(
                Monitor,
                DashboardService,
                new UsageTrackingService(SessionStore));
        }

        public FixedTimeProvider TimeProvider { get; }

        public FakeUsageSessionStore SessionStore { get; }

        public FakeUsageRuleStore RuleStore { get; }

        public FakeTimeGrantStore GrantStore { get; }

        public FakeActivityMonitor Monitor { get; }

        public DashboardService DashboardService { get; }

        public MonitoringService Service { get; }
    }

    [Fact]
    public async Task Tick_WithoutTrackingService_ReturnsSnapshotWithoutWritingSessions()
    {
        var fixture = new Fixture(At(12), CreateRule());
        fixture.Monitor.SetStatus(Status(ActivityState.Active, At(12), At(12)));

        var serviceWithoutTracking = new MonitoringService(
            fixture.Monitor,
            fixture.DashboardService);

        var state = await serviceWithoutTracking.TickAsync(Utc);

        Assert.Equal(ActivityState.Active, state.Activity.State);
        Assert.Equal(AccessState.Allowed, state.Snapshot.State);
        Assert.Empty(fixture.SessionStore.Sessions);
    }

    [Fact]
    public async Task Tick_Active_OpensSessionAndPublishesActivityAndSnapshot()
    {
        var fixture = new Fixture(At(12), CreateRule());
        var status = Status(ActivityState.Active, At(12), At(12));
        fixture.Monitor.SetStatus(status);

        var state = await fixture.Service.TickAsync(Utc);

        Assert.Same(status, state.Activity);
        Assert.Equal(AccessState.Allowed, state.Snapshot.State);
        Assert.Equal(TimeSpan.FromMinutes(120), state.Snapshot.ConfiguredDailyLimit);

        var session = Assert.Single(fixture.SessionStore.Sessions);
        Assert.True(session.IsOpen);
        Assert.Equal(At(12), session.StartedAt);
    }

    [Fact]
    public async Task Tick_IdleAfterActive_ClosesSessionAtLastInputAndCountsUsage()
    {
        var fixture = new Fixture(At(12), CreateRule());

        fixture.Monitor.SetStatus(Status(ActivityState.Active, At(12), At(12)));
        await fixture.Service.TickAsync(Utc);

        fixture.TimeProvider.Now = At(12, 10);

        var idle = Status(ActivityState.Idle, At(12, 5), At(12, 10));
        fixture.Monitor.SetStatus(idle);

        var state = await fixture.Service.TickAsync(Utc);

        Assert.Same(idle, state.Activity);
        Assert.Equal(ActivityState.Idle, state.Activity.State);

        var session = Assert.Single(fixture.SessionStore.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(TimeSpan.FromMinutes(5), session.Duration);
        Assert.Equal(TimeSpan.FromMinutes(5), state.Snapshot.UsageToday);
    }

    [Fact]
    public async Task Tick_UsesExactlyOneActivityProbePerTick()
    {
        var fixture = new Fixture(At(12), CreateRule());
        fixture.Monitor.SetStatus(Status(ActivityState.Active, At(12), At(12)));

        await fixture.Service.TickAsync(Utc);
        await fixture.Service.TickAsync(Utc);

        Assert.Equal(2, fixture.Monitor.ProbeCount);
    }

    [Fact]
    public async Task Tick_RepeatedActive_DoesNotDuplicateSessions()
    {
        var fixture = new Fixture(At(12), CreateRule());
        fixture.Monitor.SetStatus(Status(ActivityState.Active, At(12), At(12)));

        await fixture.Service.TickAsync(Utc);
        await fixture.Service.TickAsync(Utc);
        await fixture.Service.TickAsync(Utc);

        Assert.Single(fixture.SessionStore.Sessions);
    }

    [Fact]
    public async Task Tick_PropagatesUtcOffsetToSnapshot()
    {
        var offset = TimeSpan.FromHours(2);
        var now = new DateTimeOffset(2026, 6, 8, 1, 0, 0, offset);

        var fixture = new Fixture(now, CreateRule());

        var grantResult = TimeGrant.TryCreate(now.AddMinutes(-10), 20, out var grant);
        Assert.True(grantResult.Success, grantResult.Error);
        await fixture.GrantStore.AddAsync(grant!);

        fixture.Monitor.SetStatus(Status(ActivityState.Active, now, now));

        var state = await fixture.Service.TickAsync(offset);

        Assert.Equal(TimeSpan.FromMinutes(20), state.Snapshot.BonusToday);
        Assert.Equal(TimeSpan.FromMinutes(140), state.Snapshot.EffectiveDailyAllowance);
    }
}
