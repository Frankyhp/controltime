using Microsoft.Data.Sqlite;

namespace ControlTime.Tests;

public sealed class RuleStoreCorruptedRowTests : IDisposable
{
    private readonly string _databasePath;

    public RuleStoreCorruptedRowTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"controltime-rules-{Guid.NewGuid():N}.db");
    }

    private TestDbContextFactory CreateFactory() => new(_databasePath);

    private static UsageRule CreateRule(string name)
    {
        var scheduleResult = AllowedSchedule.TryCreate(
            new TimeOnly(17, 0),
            new TimeOnly(21, 0),
            out var schedule);
        Assert.True(scheduleResult.Success, scheduleResult.Error);

        var ruleResult = UsageRule.TryCreate(
            name,
            true,
            new HashSet<DayOfWeek> { DayOfWeek.Monday },
            schedule!,
            TimeSpan.FromMinutes(120),
            out var rule);
        Assert.True(ruleResult.Success, ruleResult.Error);

        return rule!;
    }

    private async Task<int> CorruptRowAsync(string ruleName, string allowedDays)
    {
        await using var connection = new SqliteConnection(ControlTimeStorage.GetConnectionString(_databasePath));
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE UsageRules SET AllowedDays = $days WHERE Name = $name;";
        command.Parameters.AddWithValue("$days", allowedDays);
        command.Parameters.AddWithValue("$name", ruleName);
        return await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetAllAsync_OneCorruptedRow_ReturnsValidRulesAndSkipsCorrupted()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageRuleStore(factory);

        var goodRule = CreateRule("Valid rule");
        var corruptedRule = CreateRule("Corrupted rule");

        await store.AddAsync(goodRule);
        await store.AddAsync(corruptedRule);

        Assert.Equal(1, await CorruptRowAsync(corruptedRule.Name, "not-a-day-list"));

        var loaded = await store.GetAllAsync();

        var remaining = Assert.Single(loaded);
        Assert.Equal(goodRule.Id, remaining.Id);
    }

    [Fact]
    public async Task GetAllAsync_AllRowsCorrupted_ReturnsEmptyWithoutThrowing()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteUsageRuleStore(factory);

        var rule = CreateRule("Corrupted rule");
        await store.AddAsync(rule);

        Assert.Equal(1, await CorruptRowAsync(rule.Name, "not-a-day-list"));

        Assert.Empty(await store.GetAllAsync());
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
        }
    }
}
