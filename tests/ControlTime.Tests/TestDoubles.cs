namespace ControlTime.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeUsageRuleStore(params UsageRule[] rules) : IUsageRuleStore
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

internal sealed class FakeTimeGrantStore(params TimeGrant[] grants) : ITimeGrantStore
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

    public Task<IReadOnlyList<TimeGrant>> GetGrantsInRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var matches = _grants
            .Where(g => g.GrantedAt >= from && g.GrantedAt < to)
            .ToList();

        return Task.FromResult<IReadOnlyList<TimeGrant>>(matches);
    }
}

internal sealed class FakeUsageSessionStore(params UsageSession[] sessions) : IUsageSessionStore
{
    private readonly List<UsageSession> _sessions = [.. sessions];

    public Task AddAsync(UsageSession session, CancellationToken cancellationToken = default)
    {
        _sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UsageSession session, CancellationToken cancellationToken = default)
    {
        _sessions.RemoveAll(s => s.Id == session.Id);
        _sessions.Add(session);
        return Task.CompletedTask;
    }

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

    public Task<UsageSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions.LastOrDefault(s => s.IsOpen));

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _sessions.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }

    public IReadOnlyList<UsageSession> Sessions => _sessions;
}

internal sealed class FakeActivityMonitor(ActivityStatus? initial = null) : IActivityMonitor
{
    private ActivityStatus? _status = initial;

    public int ProbeCount { get; private set; }

    public ActivityStatus? CurrentStatus => _status;

    public void SetStatus(ActivityStatus status) => _status = status;

    public ActivityStatus GetCurrentStatus()
    {
        ProbeCount++;

        return _status ?? throw new InvalidOperationException("No fake activity status configured.");
    }
}
