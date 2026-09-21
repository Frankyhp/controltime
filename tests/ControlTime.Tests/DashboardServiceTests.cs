namespace ControlTime.Tests;

public class DashboardServiceTests
{
    private static readonly DateTimeOffset Monday = new(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);

    private static readonly DayOfWeek[] AllDays = (DayOfWeek[])Enum.GetValues(typeof(DayOfWeek));

    private static readonly TimeSpan Utc = TimeSpan.Zero;

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
        int endHour = 18,
        int? limitMinutes = 120)
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

    private static TimeGrant CreateGrant(DateTimeOffset grantedAt, int minutes)
    {
        var result = TimeGrant.TryCreate(grantedAt, minutes, out var grant);
        Assert.True(result.Success, result.Error);

        return grant!;
    }

    private static DashboardService CreateService(
        DateTimeOffset now,
        params UsageRule[] rules)
    {
        return CreateService(now, [], rules);
    }

    private static DashboardService CreateService(
        DateTimeOffset now,
        UsageSession[] sessions,
        params UsageRule[] rules)
    {
        return new DashboardService(
            new FakeUsageRuleStore(rules),
            new FakeTimeGrantStore(),
            new UsageService(new FakeUsageSessionStore(sessions), new FixedTimeProvider(now)),
            new RuleEngine(),
            new FixedTimeProvider(now));
    }

    [Fact]
    public async Task GetSnapshot_NoEnabledRules_BlockedWithoutLimit()
    {
        var service = CreateService(At(DayOfWeek.Monday, 12), CreateRule(enabled: false));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.Equal(AccessState.Blocked, snapshot.State);
        Assert.False(snapshot.HasEnabledRules);
        Assert.Null(snapshot.ConfiguredDailyLimit);
        Assert.Null(snapshot.EffectiveDailyAllowance);
    }

    [Fact]
    public async Task GetSnapshot_InsideSchedule_ExposesLimitBonusAndAllowance()
    {
        var now = At(DayOfWeek.Monday, 12);

        ITimeGrantStore grantStore = new FakeTimeGrantStore(
            CreateGrant(now.AddMinutes(-30), 20),
            CreateGrant(now.AddMinutes(-10), 10));
        var service = new DashboardService(
            new FakeUsageRuleStore(CreateRule(limitMinutes: 120)),
            grantStore,
            new UsageService(new FakeUsageSessionStore(), new FixedTimeProvider(now)),
            new RuleEngine(),
            new FixedTimeProvider(now));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.Equal(AccessState.Allowed, snapshot.State);
        Assert.True(snapshot.HasEnabledRules);
        Assert.Equal(TimeSpan.FromMinutes(120), snapshot.ConfiguredDailyLimit);
        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.BonusToday);
        Assert.Equal(TimeSpan.FromMinutes(150), snapshot.EffectiveDailyAllowance);
        Assert.Equal(TimeSpan.FromMinutes(150), snapshot.RemainingTime);
    }

    [Fact]
    public async Task GetSnapshot_ActiveSchedule_IsScheduleOfSatisfiedRule()
    {
        var service = CreateService(At(DayOfWeek.Monday, 12), CreateRule(startHour: 9, endHour: 18));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.NotNull(snapshot.ActiveSchedule);
        Assert.Equal(new TimeOnly(9, 0), snapshot.ActiveSchedule!.Start);
        Assert.Equal(new TimeOnly(18, 0), snapshot.ActiveSchedule.End);
    }

    [Fact]
    public async Task GetSnapshot_OutsideSchedule_ResolvesNextAllowedSchedule()
    {
        var service = CreateService(At(DayOfWeek.Monday, 22), CreateRule(startHour: 9, endHour: 18));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.Equal(AccessState.Blocked, snapshot.State);
        Assert.NotNull(snapshot.NextAllowedAt);
        Assert.NotNull(snapshot.ActiveSchedule);
        Assert.Equal(new TimeOnly(9, 0), snapshot.ActiveSchedule!.Start);
    }

    [Fact]
    public async Task GetSnapshot_UnlimitedRule_HasNoConfiguredLimit()
    {
        var service = CreateService(At(DayOfWeek.Monday, 12), CreateRule(limitMinutes: null));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.Equal(AccessState.Allowed, snapshot.State);
        Assert.True(snapshot.HasEnabledRules);
        Assert.Null(snapshot.ConfiguredDailyLimit);
        Assert.Null(snapshot.EffectiveDailyAllowance);
    }

    [Fact]
    public async Task GetSnapshot_GrantsFromOtherDays_IgnoredInBonus()
    {
        var now = At(DayOfWeek.Monday, 12);

        ITimeGrantStore grantStore = new FakeTimeGrantStore(
            CreateGrant(now.AddDays(-1), 45));

        var service = new DashboardService(
            new FakeUsageRuleStore(CreateRule(limitMinutes: 120)),
            grantStore,
            new UsageService(new FakeUsageSessionStore(), new FixedTimeProvider(now)),
            new RuleEngine(),
            new FixedTimeProvider(now));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.Equal(TimeSpan.Zero, snapshot.BonusToday);
        Assert.Equal(TimeSpan.FromMinutes(120), snapshot.EffectiveDailyAllowance);
    }

    [Fact]
    public async Task GetSnapshot_UsageToday_ReducesRemainingTime()
    {
        var now = At(DayOfWeek.Monday, 12);

        var used = CreateSession(now.AddHours(-2), now.AddHours(-1));

        var service = CreateService(
            now,
            [used],
            CreateRule(limitMinutes: 120, startHour: 9, endHour: 18));

        var snapshot = await service.GetSnapshotAsync(Utc);

        Assert.Equal(TimeSpan.FromHours(1), snapshot.UsageToday);
        Assert.Equal(AccessState.Allowed, snapshot.State);
        Assert.Equal(TimeSpan.FromMinutes(60), snapshot.RemainingTime);
    }

    private static UsageSession CreateSession(DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        var result = UsageSession.TryCreate(startedAt, endedAt, out var session);
        Assert.True(result.Success, result.Error);

        return session!;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeUsageRuleStore(params UsageRule[] rules) : IUsageRuleStore
    {
        private readonly List<UsageRule> _rules = [.. rules];

        public Task<IReadOnlyList<UsageRule>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UsageRule>>(_rules.ToList());

        public Task AddAsync(UsageRule rule, CancellationToken cancellationToken = default)
        {
            _rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UsageRule rule, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_rules.RemoveAll(r => r.Id == id) > 0);
    }

    private sealed class FakeTimeGrantStore(params TimeGrant[] grants) : ITimeGrantStore
    {
        private readonly List<TimeGrant> _grants = [.. grants];

        public Task AddAsync(TimeGrant grant, CancellationToken cancellationToken = default)
        {
            _grants.Add(grant);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TimeGrant>> GetGrantsForDayAsync(
            DateOnly day,
            TimeSpan utcOffset,
            CancellationToken cancellationToken = default)
        {
            var dayStartUtc = new DateTimeOffset(
                day.Year, day.Month, day.Day, 0, 0, 0, utcOffset).UtcDateTime;
            var dayEndUtc = dayStartUtc.AddDays(1);

            var matches = _grants
                .Where(g => g.GrantedAt.UtcDateTime >= dayStartUtc && g.GrantedAt.UtcDateTime < dayEndUtc)
                .ToList();

            return Task.FromResult<IReadOnlyList<TimeGrant>>(matches);
        }
    }

    private sealed class FakeUsageSessionStore(params UsageSession[] sessions) : IUsageSessionStore
    {
        private readonly List<UsageSession> _sessions = [.. sessions];

        public Task AddAsync(UsageSession session, CancellationToken cancellationToken = default)
        {
            _sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UsageSession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<UsageSession>> GetSessionsOverlappingAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            var matches = _sessions
                .Where(s => s.StartedAt < to && (s.EndedAt is null || s.EndedAt > from))
                .ToList();

            return Task.FromResult<IReadOnlyList<UsageSession>>(matches);
        }
    }
}
