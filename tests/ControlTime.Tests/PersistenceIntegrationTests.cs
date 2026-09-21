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

    private TestDbContextFactory CreateFactory() => new(_databasePath);

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
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var rule = CreateRule();
        await new SqliteUsageRuleStore(factory).AddAsync(rule);

        var stored = await new SqliteUsageRuleStore(factory).GetAllAsync();

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
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        await new SqliteUsageRuleStore(factory).AddAsync(CreateRule(limitMinutes: null));

        var stored = await new SqliteUsageRuleStore(factory).GetAllAsync();

        var loaded = Assert.Single(stored);
        Assert.Null(loaded.DailyLimit);
    }

    [Fact]
    public async Task UpdateRule_ChangesArePersisted()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var rule = CreateRule();
        await new SqliteUsageRuleStore(factory).AddAsync(rule);

        var updateResult = UsageRule.TryCreate(
            rule.Id,
            "Renamed rule",
            enabled: false,
            new HashSet<DayOfWeek> { DayOfWeek.Sunday },
            rule.Schedule,
            TimeSpan.FromMinutes(45),
            out var updated);
        Assert.True(updateResult.Success, updateResult.Error);

        await new SqliteUsageRuleStore(factory).UpdateAsync(updated!);

        var stored = await new SqliteUsageRuleStore(factory).GetAllAsync();

        var loaded = Assert.Single(stored);
        Assert.Equal("Renamed rule", loaded.Name);
        Assert.False(loaded.Enabled);
        Assert.Equal([DayOfWeek.Sunday], loaded.AllowedDays);
        Assert.Equal(TimeSpan.FromMinutes(45), loaded.DailyLimit);
    }

    [Fact]
    public async Task RemoveRule_PersistsDeletion()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var rule = CreateRule();
        var store = new SqliteUsageRuleStore(factory);
        await store.AddAsync(rule);

        Assert.True(await store.RemoveAsync(rule.Id));
        Assert.False(await store.RemoveAsync(rule.Id));

        Assert.Empty(await new SqliteUsageRuleStore(factory).GetAllAsync());
    }

    [Fact]
    public async Task AppSettings_SetThenGetFromNewContext_ReturnsValue()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteAppSettingsStore(factory);

        Assert.Null(await store.GetAsync("missing.key"));

        await store.SetAsync("sample.key", "value-1");

        Assert.Equal("value-1", await new SqliteAppSettingsStore(factory).GetAsync("sample.key"));
    }

    [Fact]
    public async Task AppSettings_SetTwice_UpdatesExistingValue()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var store = new SqliteAppSettingsStore(factory);
        await store.SetAsync("sample.key", "value-1");
        await store.SetAsync("sample.key", "value-2");

        Assert.Equal("value-2", await store.GetAsync("sample.key"));
    }

    [Fact]
    public async Task AdminPassword_SetThenVerifyFromNewContext_SucceedsWithoutStoringPlainText()
    {
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var service = new AdminAuthService(
            new SqliteAppSettingsStore(factory),
            new Pbkdf2PasswordHasher());
        Assert.Equal(AdminPasswordResult.Success, await service.SetPasswordAsync("sqlite-admin-pass"));

        var reloaded = new AdminAuthService(
            new SqliteAppSettingsStore(factory),
            new Pbkdf2PasswordHasher());

        Assert.True(await reloaded.HasPasswordAsync());
        Assert.Equal(AdminAuthenticationResult.Success,
            await reloaded.TryEnterAdminModeAsync("sqlite-admin-pass"));
        Assert.Equal(AdminAuthenticationResult.InvalidPassword,
            await reloaded.TryEnterAdminModeAsync("wrong-password"));

        var storedHash = await new SqliteAppSettingsStore(factory)
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
