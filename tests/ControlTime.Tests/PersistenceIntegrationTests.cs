using Microsoft.Data.Sqlite;

namespace ControlTime.Tests;

public sealed class PersistenceIntegrationTests : IDisposable
{
    private readonly string _databasePath;

    public PersistenceIntegrationTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"controltime-tests-{Guid.NewGuid():N}.db");
    }

    private ControlTimeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ControlTimeDbContext>()
            .UseSqlite(ControlTimeStorage.GetConnectionString(_databasePath))
            .Options;

        return new ControlTimeDbContext(options);
    }

    private static UsageRule CreateRule(
        string name = "Weekday rule",
        bool enabled = true,
        DayOfWeek[]? days = null,
        int startHour = 17,
        int endHour = 21,
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
            new HashSet<DayOfWeek>(days ?? [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Friday]),
            schedule!,
            limitMinutes is int m ? TimeSpan.FromMinutes(m) : null,
            out var rule);
        Assert.True(ruleResult.Success, ruleResult.Error);

        return rule!;
    }

    [Fact]
    public async Task AddRule_ReloadFromNewContext_PreservesAllFields()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var rule = CreateRule();
        var store = new SqliteUsageRuleStore(context);
        await store.AddAsync(rule);

        using var reader = CreateContext();
        var stored = await new SqliteUsageRuleStore(reader).GetAllAsync();

        var loaded = Assert.Single(stored);
        Assert.Equal(rule.Id, loaded.Id);
        Assert.Equal(rule.Name, loaded.Name);
        Assert.Equal(rule.Enabled, loaded.Enabled);
        Assert.Equal(rule.AllowedDays, loaded.AllowedDays);
        Assert.Equal(rule.Schedule.Start, loaded.Schedule.Start);
        Assert.Equal(rule.Schedule.End, loaded.Schedule.End);
        Assert.Equal(rule.DailyLimit, loaded.DailyLimit);
    }

    [Fact]
    public async Task AddRule_WithoutDailyLimit_RoundTripsAsUnlimited()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        await new SqliteUsageRuleStore(context).AddAsync(CreateRule(limitMinutes: null));

        using var reader = CreateContext();
        var stored = await new SqliteUsageRuleStore(reader).GetAllAsync();

        var loaded = Assert.Single(stored);
        Assert.Null(loaded.DailyLimit);
    }

    [Fact]
    public async Task UpdateRule_ChangesArePersisted()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var rule = CreateRule();
        await new SqliteUsageRuleStore(context).AddAsync(rule);

        var updateResult = UsageRule.TryCreate(
            rule.Id,
            "Renamed rule",
            enabled: false,
            new HashSet<DayOfWeek> { DayOfWeek.Sunday },
            rule.Schedule,
            TimeSpan.FromMinutes(45),
            out var updated);
        Assert.True(updateResult.Success, updateResult.Error);

        await new SqliteUsageRuleStore(context).UpdateAsync(updated!);

        using var reader = CreateContext();
        var stored = await new SqliteUsageRuleStore(reader).GetAllAsync();

        var loaded = Assert.Single(stored);
        Assert.Equal("Renamed rule", loaded.Name);
        Assert.False(loaded.Enabled);
        Assert.Equal([DayOfWeek.Sunday], loaded.AllowedDays);
        Assert.Equal(TimeSpan.FromMinutes(45), loaded.DailyLimit);
    }

    [Fact]
    public async Task RemoveRule_PersistsDeletion()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var rule = CreateRule();
        var store = new SqliteUsageRuleStore(context);
        await store.AddAsync(rule);

        Assert.True(await store.RemoveAsync(rule.Id));
        Assert.False(await store.RemoveAsync(rule.Id));

        using var reader = CreateContext();
        Assert.Empty(await new SqliteUsageRuleStore(reader).GetAllAsync());
    }

    [Fact]
    public async Task AppSettings_SetThenGetFromNewContext_ReturnsValue()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var store = new SqliteAppSettingsStore(context);

        Assert.Null(await store.GetAsync("missing.key"));

        await store.SetAsync("sample.key", "value-1");

        using var reader = CreateContext();
        Assert.Equal("value-1", await new SqliteAppSettingsStore(reader).GetAsync("sample.key"));
    }

    [Fact]
    public async Task AppSettings_SetTwice_UpdatesExistingValue()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var store = new SqliteAppSettingsStore(context);
        await store.SetAsync("sample.key", "value-1");
        await store.SetAsync("sample.key", "value-2");

        Assert.Equal("value-2", await store.GetAsync("sample.key"));
    }

    [Fact]
    public async Task AdminPassword_SetThenVerifyFromNewContext_SucceedsWithoutStoringPlainText()
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        var service = new AdminAuthService(new SqliteAppSettingsStore(context), new Pbkdf2PasswordHasher());
        Assert.Equal(AdminPasswordResult.Success, await service.SetPasswordAsync("sqlite-admin-pass"));

        using var reader = CreateContext();
        var reloaded = new AdminAuthService(new SqliteAppSettingsStore(reader), new Pbkdf2PasswordHasher());

        Assert.True(await reloaded.HasPasswordAsync());
        Assert.Equal(AdminAuthenticationResult.Success,
            await reloaded.TryEnterAdminModeAsync("sqlite-admin-pass"));
        Assert.Equal(AdminAuthenticationResult.InvalidPassword,
            await reloaded.TryEnterAdminModeAsync("wrong-password"));

        var storedHash = await new SqliteAppSettingsStore(reader)
            .GetAsync(AdminAuthService.PasswordHashSettingKey);
        Assert.NotNull(storedHash);
        Assert.DoesNotContain("sqlite-admin-pass", storedHash);
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
