using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.Tests;

public class UsageTrackingServiceTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUsageSessionStore _store = new();
    private readonly UsageTrackingService _service;

    public UsageTrackingServiceTests() => _service = new UsageTrackingService(_store);

    private static ActivityStatus Status(
        ActivityState state,
        DateTimeOffset lastInputAt,
        DateTimeOffset timestamp) =>
        new(state, lastInputAt, timestamp);

    private static DateTimeOffset At(int minutes) => Base.AddMinutes(minutes);

    [Fact]
    public async Task Tick_Active_OpensSessionAtTimestamp()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(-1), Base));

        var session = Assert.Single(_store.Sessions);
        Assert.True(session.IsOpen);
        Assert.Equal(Base, session.StartedAt);
    }

    [Fact]
    public async Task Tick_ActiveRepeated_DoesNotDuplicateSessions()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(4), At(5)));
        await _service.TickAsync(Status(ActivityState.Active, At(9), At(10)));

        Assert.Single(_store.Sessions);
    }

    [Fact]
    public async Task Tick_Idle_ClosesOpenSessionAtLastInputNotTimestamp()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await _service.TickAsync(Status(ActivityState.Idle, At(1), At(7)));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(1), session.EndedAt);
    }

    [Fact]
    public async Task Tick_Locked_ClosesOpenSession()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await _service.TickAsync(Status(ActivityState.Locked, At(2), At(30)));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(2), session.EndedAt);
    }

    [Fact]
    public async Task Tick_IdleWithoutSession_DoesNothing()
    {
        await _service.TickAsync(Status(ActivityState.Idle, At(-10), Base));

        Assert.Empty(_store.Sessions);
        Assert.Null(_service.OpenSession);
    }

    [Fact]
    public async Task Tick_WhenActive_ClosesStaleOrphanWithoutUnobservedUsageAndOpensFreshSession()
    {
        UsageSession.TryCreateOpen(At(-30), out var orphan);
        _store.Sessions.Add(orphan!);

        await _service.TickAsync(Status(ActivityState.Active, At(0), Base));

        var stale = Assert.Single(_store.Sessions, s => s.Id == orphan!.Id);
        Assert.False(stale.IsOpen);
        Assert.Equal(stale.StartedAt, stale.EndedAt);
        Assert.Equal(TimeSpan.Zero, stale.Duration);

        var fresh = Assert.Single(_store.Sessions, s => s.IsOpen);
        Assert.NotEqual(orphan!.Id, fresh.Id);
        Assert.Equal(Base, fresh.StartedAt);
        Assert.Equal(fresh.Id, _service.OpenSession!.Id);
    }

    [Fact]
    public async Task FirstTick_IdleWithOrphan_ClosesOrphanWithoutUnobservedUsage()
    {
        UsageSession.TryCreateOpen(At(0), out var orphan);
        _store.Sessions.Add(orphan!);

        await _service.TickAsync(Status(ActivityState.Idle, At(5), At(11)));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(0), session.EndedAt);
        Assert.Equal(TimeSpan.Zero, session.Duration);
    }

    [Fact]
    public async Task FirstTick_StaleOrphanAcrossMidnight_DoesNotInflateTodaysUsage()
    {
        // A session left open yesterday (service killed / machine off) must not
        // count today's usage from midnight up to now.
        var yesterday = Base.AddDays(-1).AddHours(12);
        UsageSession.TryCreateOpen(yesterday, out var orphan);
        _store.Sessions.Add(orphan!);

        await _service.TickAsync(Status(ActivityState.Active, Base, Base));

        var dayStart = new DateTimeOffset(
            DateOnly.FromDateTime(Base.Date).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        var usage = UsageCalculator.GetUsageForDay(
            _store.Sessions,
            dayStart,
            dayStart.AddDays(1),
            asOf: Base);

        Assert.Equal(TimeSpan.Zero, usage);
    }

    [Fact]
    public async Task Close_WithNoUsageSinceStart_ClosesZeroLengthInsteadOfDeleting()
    {
        UsageSession.TryCreateOpen(At(0), out var orphan);
        _store.Sessions.Add(orphan!);

        await _service.TickAsync(Status(ActivityState.Locked, At(-5), At(10)));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(0), session.EndedAt);
        Assert.Equal(TimeSpan.Zero, session.Duration);
        Assert.Null(_service.OpenSession);
    }

    [Fact]
    public async Task BackwardClockJump_WhileSessionOpen_ClosesAtStartWithoutLosingSession()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(0), At(0)));

        await _service.TickAsync(Status(ActivityState.Idle, At(-30), At(-25)));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(0), session.StartedAt);
        Assert.Equal(At(0), session.EndedAt);
        Assert.Equal(TimeSpan.Zero, session.Duration);
    }

    [Fact]
    public async Task BackwardClockJump_KeepsDailyUsageNonNegative()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await _service.TickAsync(Status(ActivityState.Idle, At(-30), At(-25)));

        var dayStart = new DateTimeOffset(
            DateOnly.FromDateTime(Base.Date).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        var usage = UsageCalculator.GetUsageForDay(
            _store.Sessions,
            dayStart,
            dayStart.AddDays(1),
            asOf: At(60));

        Assert.True(usage >= TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, usage);
    }

    [Fact]
    public async Task CloseOpenSessionAsync_ClosesOpenSessionAtGivenInstant()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(0), At(0)));

        await _service.CloseOpenSessionAsync(At(12));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(12), session.EndedAt);
        Assert.Null(_service.OpenSession);
    }

    [Fact]
    public async Task CloseOpenSessionAsync_BeforeStart_ClosesAtStartWithoutDeleting()
    {
        UsageSession.TryCreateOpen(At(0), out var orphan);
        _store.Sessions.Add(orphan!);

        await _service.CloseOpenSessionAsync(At(-15));

        var session = Assert.Single(_store.Sessions);
        Assert.False(session.IsOpen);
        Assert.Equal(At(0), session.EndedAt);
    }

    [Fact]
    public async Task CloseOpenSessionAsync_WithoutOpenSession_DoesNothing()
    {
        await _service.CloseOpenSessionAsync(At(10));

        Assert.Empty(_store.Sessions);
        Assert.Null(_service.OpenSession);
    }

    [Fact]
    public async Task IdleThenActive_ClosesOldSessionAndOpensNewOne()
    {
        await _service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await _service.TickAsync(Status(ActivityState.Idle, At(1), At(7)));
        await _service.TickAsync(Status(ActivityState.Active, At(14), At(15)));

        Assert.Equal(2, _store.Sessions.Count);
        Assert.Single(_store.Sessions, s => !s.IsOpen);
        Assert.Single(_store.Sessions, s => s.IsOpen);
    }

    [Fact]
    public async Task ObservationGap_WithSurvivedOpenSession_ClosesAtLastObservedInstantAndOpensFresh()
    {
        // Reproduction: the machine is suspended (or the process is frozen by a
        // fast-startup shutdown) and later resumes with the same tracking
        // process alive. The unobserved stretch must not become usage.
        var service = CreateServiceWithGapTolerance();
        await service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await service.TickAsync(Status(ActivityState.Active, At(0).AddSeconds(10), At(0).AddSeconds(10)));

        // Process frozen: next observed tick is more than 8 hours later.
        await service.TickAsync(Status(ActivityState.Active, At(485), At(486)));

        var closed = Assert.Single(_store.Sessions, s => !s.IsOpen);
        Assert.Equal(At(0), closed.StartedAt);
        Assert.Equal(At(0).AddSeconds(10), closed.EndedAt);
        Assert.Equal(TimeSpan.FromSeconds(10), closed.Duration);

        var fresh = Assert.Single(_store.Sessions, s => s.IsOpen);
        Assert.Equal(At(486), fresh.StartedAt);
    }

    [Fact]
    public async Task ObservationGap_AcrossMidnight_DoesNotInflateTodaysUsage()
    {
        // Reproduction of the reported bug: an open session survived overnight
        // (frozen process). Today's usage must be only the observed stretch.
        var service = CreateServiceWithGapTolerance();
        var morning = new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);
        await service.TickAsync(Status(ActivityState.Active, morning, morning));
        await service.TickAsync(
            Status(ActivityState.Active, morning.AddSeconds(10), morning.AddSeconds(10)));

        // Machine "shut down" at 08:30 (process survives), back at 20:06 next day.
        var resumed = new DateTimeOffset(2026, 9, 22, 20, 6, 0, TimeSpan.Zero);
        await service.TickAsync(Status(ActivityState.Active, resumed, resumed));

        var dayStart = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

        var usage = UsageCalculator.GetUsageForDay(
            _store.Sessions,
            dayStart,
            dayStart.AddDays(1),
            asOf: resumed);

        Assert.Equal(TimeSpan.Zero, usage);
    }

    [Fact]
    public async Task ObservationGap_WithoutOpenSession_DoesNotOpenSessionDuringGap()
    {
        var service = CreateServiceWithGapTolerance();
        await service.TickAsync(Status(ActivityState.Idle, At(0), At(0)));
        await service.TickAsync(Status(ActivityState.Idle, At(600), At(600)));

        Assert.Empty(_store.Sessions);
    }

    [Fact]
    public async Task IdleAfterGap_ClosesAtObservedBoundaryNotAtClaimedLastInput()
    {
        // The WTS fallback claims LastInputAt == now without real input evidence;
        // a detected gap must close at the last observed tick, not at the claim.
        var service = CreateServiceWithGapTolerance();
        await service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await service.TickAsync(Status(ActivityState.Active, At(0).AddSeconds(10), At(0).AddSeconds(10)));

        // After the gap the monitor claims input at minute 900 while the last
        // observation ended 10 seconds after minute 0.
        await service.TickAsync(Status(ActivityState.Idle, At(900), At(900)));

        var closed = Assert.Single(_store.Sessions);
        Assert.False(closed.IsOpen);
        Assert.Equal(At(0).AddSeconds(10), closed.EndedAt);
    }

    [Fact]
    public async Task GapSmallerThanTolerance_KeepsSessionOpen()
    {
        // A stall smaller than the tolerance is normal jitter and must not
        // split the session.
        var service = CreateServiceWithGapTolerance();
        await service.TickAsync(Status(ActivityState.Active, At(0), At(0)));
        await service.TickAsync(Status(ActivityState.Active, At(0).AddSeconds(10), At(0).AddSeconds(10)));

        var session = Assert.Single(_store.Sessions);
        Assert.True(session.IsOpen);
    }

    private UsageTrackingService CreateServiceWithGapTolerance() => new(_store, observationGapTolerance: TimeSpan.FromSeconds(15));

    private sealed class InMemoryUsageSessionStore : IUsageSessionStore
    {
        public List<UsageSession> Sessions { get; } = [];

        public Task AddAsync(UsageSession session, CancellationToken cancellationToken = default)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UsageSession session, CancellationToken cancellationToken = default)
        {
            if (!Sessions.Any(s => s.Id == session.Id))
            {
                throw new InvalidOperationException($"Usage session '{session.Id}' does not exist.");
            }

            Sessions.RemoveAll(s => s.Id == session.Id);
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UsageSession>> GetSessionsOverlappingAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<UsageSession> matches = Sessions
                .Where(s => s.StartedAt < to && (s.EndedAt is null || s.EndedAt > from))
                .ToList();

            return Task.FromResult(matches);
        }

        public Task<UsageSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.LastOrDefault(s => s.IsOpen));

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (!Sessions.Any(s => s.Id == id))
            {
                throw new InvalidOperationException($"Usage session '{id}' does not exist.");
            }

            Sessions.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }
    }
}
