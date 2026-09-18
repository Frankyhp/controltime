namespace ControlTime.Tests;

public class AdminAuthServiceTests
{
    private const string ValidPassword = "sup3r-secret-admin";

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static (AdminAuthService Service, InMemoryAppSettingsStore Store, FakeTimeProvider Clock) CreateService(
        TimeSpan? sessionDuration = null)
    {
        var store = new InMemoryAppSettingsStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
        var service = new AdminAuthService(
            store,
            new Pbkdf2PasswordHasher(),
            clock,
            sessionDuration ?? AdminAuthService.DefaultAdminSessionDuration);

        return (service, store, clock);
    }

    [Fact]
    public async Task HasPassword_NoPasswordStored_ReturnsFalse()
    {
        var (service, _, _) = CreateService();

        Assert.False(await service.HasPasswordAsync());
    }

    [Fact]
    public async Task SetPassword_FirstTime_SucceedsAndStoresHashOnly()
    {
        var (service, store, _) = CreateService();

        var result = await service.SetPasswordAsync(ValidPassword);

        Assert.Equal(AdminPasswordResult.Success, result);
        Assert.True(await service.HasPasswordAsync());

        var stored = store.RawValues[AdminAuthService.PasswordHashSettingKey];
        Assert.DoesNotContain(ValidPassword, stored);
        Assert.False(string.IsNullOrWhiteSpace(stored));
    }

    [Fact]
    public async Task SetPassword_AfterSetup_ReturnsAlreadyConfigured()
    {
        var (service, _, _) = CreateService();
        await service.SetPasswordAsync(ValidPassword);

        var result = await service.SetPasswordAsync("another-password");

        Assert.Equal(AdminPasswordResult.AlreadyConfigured, result);
        Assert.Equal(AdminAuthenticationResult.Success,
            await service.TryEnterAdminModeAsync(ValidPassword));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("       ")]
    public async Task SetPassword_WeakPassword_IsRejected(string password)
    {
        var (service, store, _) = CreateService();

        var result = await service.SetPasswordAsync(password);

        Assert.NotEqual(AdminPasswordResult.Success, result);
        Assert.False(await service.HasPasswordAsync());
        Assert.Empty(store.RawValues);
    }

    [Fact]
    public async Task TryEnterAdminMode_CorrectPassword_EntersAdminMode()
    {
        var (service, _, _) = CreateService();
        await service.SetPasswordAsync(ValidPassword);
        service.ExitAdminMode();

        var result = await service.TryEnterAdminModeAsync(ValidPassword);

        Assert.Equal(AdminAuthenticationResult.Success, result);
        Assert.True(service.IsAdminMode);
    }

    [Fact]
    public async Task TryEnterAdminMode_WrongPassword_StaysInNormalMode()
    {
        var (service, _, _) = CreateService();
        await service.SetPasswordAsync(ValidPassword);
        service.ExitAdminMode();

        var result = await service.TryEnterAdminModeAsync("wrong-password");

        Assert.Equal(AdminAuthenticationResult.InvalidPassword, result);
        Assert.False(service.IsAdminMode);
    }

    [Fact]
    public async Task TryEnterAdminMode_NoPasswordConfigured_ReturnsNotConfigured()
    {
        var (service, _, _) = CreateService();

        var result = await service.TryEnterAdminModeAsync(ValidPassword);

        Assert.Equal(AdminAuthenticationResult.NotConfigured, result);
        Assert.False(service.IsAdminMode);
    }

    [Fact]
    public async Task AdminMode_SetupEntersAdminMode_ThenExpiresAutomatically()
    {
        var (service, _, clock) = CreateService(TimeSpan.FromMinutes(5));

        await service.SetPasswordAsync(ValidPassword);
        Assert.True(service.IsAdminMode);

        clock.UtcNow = clock.UtcNow + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1);
        Assert.True(service.IsAdminMode);

        clock.UtcNow = clock.UtcNow + TimeSpan.FromSeconds(1);
        Assert.False(service.IsAdminMode);
    }

    [Fact]
    public async Task ExitAdminMode_ReturnsToNormalMode()
    {
        var (service, _, _) = CreateService();
        await service.SetPasswordAsync(ValidPassword);

        service.ExitAdminMode();

        Assert.False(service.IsAdminMode);
    }

    [Fact]
    public async Task NewServiceInstance_VerifiesPasswordAgainstPersistedHash()
    {
        var store = new InMemoryAppSettingsStore();
        var first = new AdminAuthService(store, new Pbkdf2PasswordHasher());
        await first.SetPasswordAsync(ValidPassword);

        var second = new AdminAuthService(store, new Pbkdf2PasswordHasher());

        Assert.True(await second.HasPasswordAsync());
        Assert.Equal(AdminAuthenticationResult.Success,
            await second.TryEnterAdminModeAsync(ValidPassword));
        Assert.Equal(AdminAuthenticationResult.InvalidPassword,
            await second.TryEnterAdminModeAsync("wrong-password"));
    }
}
