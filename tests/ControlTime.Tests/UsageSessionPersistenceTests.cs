using Microsoft.Data.Sqlite;

namespace ControlTime.Tests;

public sealed class UsageSessionPersistenceTests : IDisposable
{
    private readonly string _databasePath;

    public UsageSessionPersistenceTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"controltime-session-tests-{Guid.NewGuid():N}.db");
    }

    private TestDbContextFactory CreateFactory() => new(_databasePath);

    private async Task<IReadOnlyList<UsageSession>> GetDaySessionsAsync(TestDbContextFactory factory)
        => await new SqliteUsageSessionStore(factory).GetSessionsOverlappingAsync(
            new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

    private static UsageSession Closed(
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        var result = UsageSession.TryCreate(startedAt, endedAt, out var session);
        Assert.True(result.Success, result.Error);

        return session!;
    }

    private static DateTimeOffset June(int day, int hour) =>
        new(2026, 6, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddSession_RoundTripsOpenAndClosedFromNewContext()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var closed = Closed(June(8, 9), June(8, 11));
        UsageSession.TryCreateOpen(June(8, 15), out var open);

        var store = new SqliteUsageSessionStore(factory);
        await store.AddAsync(closed);
        await store.AddAsync(open!);

        var stored = await GetDaySessionsAsync(factory);

        Assert.Multiple(
            () => Assert.Equal(2, stored.Count),
            () => Assert.Equal(closed.Duration, stored.Single(s => s.Id == closed.Id).Duration),
            () => Assert.True(stored.Single(s => s.Id == open!.Id).IsOpen));
    }

    [Fact]
    public async Task UpdateSession_PersistsClose()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        UsageSession.TryCreateOpen(June(8, 20), out var open);

        var store = new SqliteUsageSessionStore(factory);
        await store.AddAsync(open!);

        var closeResult = open!.TryClose(June(8, 21).AddMinutes(15), out var closed);
        Assert.True(closeResult.Success, closeResult.Error);

        await store.UpdateAsync(closed!);

        var stored = await GetDaySessionsAsync(factory);

        var loaded = Assert.Single(stored);
        Assert.False(loaded.IsOpen);
        Assert.Equal(TimeSpan.FromMinutes(75), loaded.Duration);
    }

    [Fact]
    public async Task UpdateSession_ZeroLengthClose_RoundTripsFromNewContext()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        UsageSession.TryCreateOpen(June(8, 9), out var open);

        var store = new SqliteUsageSessionStore(factory);
        await store.AddAsync(open!);

        var closeResult = open!.TryClose(June(8, 9), out var closed);
        Assert.True(closeResult.Success, closeResult.Error);

        await store.UpdateAsync(closed!);

        var stored = await GetDaySessionsAsync(factory);

        var loaded = Assert.Single(stored);
        Assert.False(loaded.IsOpen);
        Assert.Equal(TimeSpan.Zero, loaded.Duration);
    }

    [Fact]
    public async Task UpdateSession_UnknownId_Throws()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageSessionStore(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateAsync(Closed(June(8, 9), June(8, 10))));
    }

    [Fact]
    public async Task GetSessionsOverlapping_IncludesSessionsStartedBeforeWindow()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageSessionStore(factory);

        var crossingMidnight = Closed(June(7, 23), June(8, 1));
        var inside = Closed(June(8, 9), June(8, 10));
        var beforeWindow = Closed(June(7, 8), June(7, 9));
        var afterWindow = Closed(June(9, 8), June(9, 9));

        await store.AddAsync(crossingMidnight);
        await store.AddAsync(inside);
        await store.AddAsync(beforeWindow);
        await store.AddAsync(afterWindow);

        var stored = await GetDaySessionsAsync(factory);

        Assert.Multiple(
            () => Assert.Equal(2, stored.Count),
            () => Assert.Contains(stored, s => s.Id == crossingMidnight.Id),
            () => Assert.Contains(stored, s => s.Id == inside.Id));
    }

    [Fact]
    public async Task GetSessionsOverlapping_SessionEndingExactlyAtWindowStart_Excluded()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageSessionStore(factory);
        await store.AddAsync(Closed(June(7, 22), June(8, 0)));

        var stored = await GetDaySessionsAsync(factory);

        Assert.Empty(stored);
    }

    [Fact]
    public async Task GetOpenSession_NoOpenSessions_ReturnsNull()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageSessionStore(factory);
        await store.AddAsync(Closed(June(8, 9), June(8, 10)));

        Assert.Null(await new SqliteUsageSessionStore(factory).GetOpenSessionAsync());
    }

    [Fact]
    public async Task GetOpenSession_ReturnsLatestOpenSession()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageSessionStore(factory);

        UsageSession.TryCreateOpen(June(8, 9), out var earlier);
        UsageSession.TryCreateOpen(June(8, 14), out var later);

        await store.AddAsync(Closed(June(8, 7), June(8, 8)));
        await store.AddAsync(earlier!);
        await store.AddAsync(later!);

        var open = await new SqliteUsageSessionStore(factory).GetOpenSessionAsync();

        Assert.Equal(later!.Id, open!.Id);
    }

    [Fact]
    public async Task DeleteSession_RemovesSessionAndUnknownIdThrows()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        UsageSession.TryCreateOpen(June(8, 9), out var open);

        var store = new SqliteUsageSessionStore(factory);
        await store.AddAsync(open!);
        await store.DeleteAsync(open!.Id);

        var readerStore = new SqliteUsageSessionStore(factory);

        Assert.Null(await readerStore.GetOpenSessionAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => readerStore.DeleteAsync(open.Id));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // Temp file left behind is acceptable.
        }
    }
}
