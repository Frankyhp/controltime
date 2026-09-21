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

    private ControlTimeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ControlTimeDbContext>()
            .UseSqlite(ControlTimeStorage.GetConnectionString(_databasePath))
            .Options;

        return new ControlTimeDbContext(options);
    }

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
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var closed = Closed(June(8, 9), June(8, 11));
        UsageSession.TryCreateOpen(
            new DateTimeOffset(2026, 6, 8, 15, 0, 0, TimeSpan.Zero),
            out var open);

        var store = new SqliteUsageSessionStore(context);
        await store.AddAsync(closed);
        await store.AddAsync(open!);

        using var reader = CreateContext();
        var stored = await new SqliteUsageSessionStore(reader).GetSessionsOverlappingAsync(
            new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        Assert.Multiple(
            () => Assert.Equal(2, stored.Count),
            () => Assert.Equal(closed.Duration, stored.Single(s => s.Id == closed.Id).Duration),
            () => Assert.True(stored.Single(s => s.Id == open!.Id).IsOpen));
    }

    [Fact]
    public async Task UpdateSession_PersistsClose()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        UsageSession.TryCreateOpen(
            new DateTimeOffset(2026, 6, 8, 20, 0, 0, TimeSpan.Zero),
            out var open);

        var store = new SqliteUsageSessionStore(context);
        await store.AddAsync(open!);

        var closeResult = open!.TryClose(
            new DateTimeOffset(2026, 6, 8, 21, 15, 0, TimeSpan.Zero),
            out var closed);
        Assert.True(closeResult.Success, closeResult.Error);

        await store.UpdateAsync(closed!);

        using var reader = CreateContext();
        var stored = await new SqliteUsageSessionStore(reader).GetSessionsOverlappingAsync(
            new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        var loaded = Assert.Single(stored);
        Assert.False(loaded.IsOpen);
        Assert.Equal(TimeSpan.FromMinutes(75), loaded.Duration);
    }

    [Fact]
    public async Task UpdateSession_UnknownId_Throws()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var store = new SqliteUsageSessionStore(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateAsync(Closed(June(8, 9), June(8, 10))));
    }

    [Fact]
    public async Task GetSessionsOverlapping_IncludesSessionsStartedBeforeWindow()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var store = new SqliteUsageSessionStore(context);

        var crossingMidnight = Closed(June(7, 23), June(8, 1));
        var inside = Closed(June(8, 9), June(8, 10));
        var beforeWindow = Closed(June(7, 8), June(7, 9));
        var afterWindow = Closed(June(9, 8), June(9, 9));

        await store.AddAsync(crossingMidnight);
        await store.AddAsync(inside);
        await store.AddAsync(beforeWindow);
        await store.AddAsync(afterWindow);

        using var reader = CreateContext();
        var stored = await new SqliteUsageSessionStore(reader).GetSessionsOverlappingAsync(
            new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        Assert.Multiple(
            () => Assert.Equal(2, stored.Count),
            () => Assert.Contains(stored, s => s.Id == crossingMidnight.Id),
            () => Assert.Contains(stored, s => s.Id == inside.Id));
    }

    [Fact]
    public async Task GetSessionsOverlapping_SessionEndingExactlyAtWindowStart_Excluded()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var store = new SqliteUsageSessionStore(context);
        await store.AddAsync(Closed(June(7, 22), June(8, 0)));

        using var reader = CreateContext();
        var stored = await new SqliteUsageSessionStore(reader).GetSessionsOverlappingAsync(
            new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(stored);
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
