using Microsoft.Data.Sqlite;

namespace ControlTime.Tests;

public sealed class TimeGrantPersistenceTests : IDisposable
{
    private readonly string _databasePath;

    public TimeGrantPersistenceTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"controltime-grant-tests-{Guid.NewGuid():N}.db");
    }

    private TestDbContextFactory CreateFactory() => new(_databasePath);

    private static TimeGrant CreateGrant(int minutes, DateTimeOffset grantedAt)
    {
        var result = TimeGrant.TryCreate(grantedAt, minutes, out var grant);
        Assert.True(result.Success, result.Error);
        return grant!;
    }

    [Fact]
    public async Task AddGrant_RoundTripsFromNewContext()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var grantedAt = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var grant = CreateGrant(30, grantedAt);

        await new SqliteTimeGrantStore(factory).AddAsync(grant);

        var stored = await new SqliteTimeGrantStore(factory)
            .GetGrantsForDayAsync(new DateOnly(2026, 6, 8), TimeSpan.Zero);

        var loaded = Assert.Single(stored);
        Assert.Equal(grant.Id, loaded.Id);
        Assert.Equal(grantedAt, loaded.GrantedAt);
        Assert.Equal(30, loaded.Minutes);
    }

    [Fact]
    public async Task GetGrantsForDay_IgnoresGrantsFromOtherDays()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteTimeGrantStore(factory);
        await store.AddAsync(CreateGrant(30, new DateTimeOffset(2026, 6, 7, 23, 59, 0, TimeSpan.Zero)));
        await store.AddAsync(CreateGrant(15, new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)));
        await store.AddAsync(CreateGrant(45, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero)));

        var stored = await new SqliteTimeGrantStore(factory)
            .GetGrantsForDayAsync(new DateOnly(2026, 6, 8), TimeSpan.Zero);

        var loaded = Assert.Single(stored);
        Assert.Equal(15, loaded.Minutes);
    }

    [Fact]
    public async Task GetGrantsForDay_SumsMultipleGrantsOfSameDay()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteTimeGrantStore(factory);
        await store.AddAsync(CreateGrant(10, new DateTimeOffset(2026, 6, 8, 9, 0, 0, TimeSpan.Zero)));
        await store.AddAsync(CreateGrant(20, new DateTimeOffset(2026, 6, 8, 18, 30, 0, TimeSpan.Zero)));

        var stored = await new SqliteTimeGrantStore(factory)
            .GetGrantsForDayAsync(new DateOnly(2026, 6, 8), TimeSpan.Zero);

        Assert.Equal(30, stored.Sum(g => g.Minutes));
    }

    [Fact]
    public async Task GetGrantsForDay_UsesExplicitUtcOffsetForDayBoundaries()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var offset = TimeSpan.FromHours(2);
        var store = new SqliteTimeGrantStore(factory);

        await store.AddAsync(CreateGrant(25, new DateTimeOffset(2026, 6, 8, 0, 30, 0, offset)));
        await store.AddAsync(CreateGrant(5, new DateTimeOffset(2026, 6, 7, 23, 30, 0, offset)));

        var stored = await new SqliteTimeGrantStore(factory)
            .GetGrantsForDayAsync(new DateOnly(2026, 6, 8), offset);

        var loaded = Assert.Single(stored);
        Assert.Equal(25, loaded.Minutes);
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
