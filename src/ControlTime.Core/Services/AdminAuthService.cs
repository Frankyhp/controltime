using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public enum AdminAuthenticationResult
{
    Success,
    NotConfigured,
    InvalidPassword
}

public enum AdminPasswordResult
{
    Success,
    AlreadyConfigured,
    TooShort,
    InvalidCharacters
}

public interface IAdminAuthService
{
    bool IsAdminMode { get; }

    Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default);

    Task<AdminPasswordResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task<AdminAuthenticationResult> TryEnterAdminModeAsync(string password, CancellationToken cancellationToken = default);

    void ExitAdminMode();
}

public sealed class AdminAuthService : IAdminAuthService
{
    public const string PasswordHashSettingKey = "admin.password.hash";

    public static readonly TimeSpan DefaultAdminSessionDuration = TimeSpan.FromMinutes(5);

    private readonly IAppSettingsStore _settingsStore;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _adminSessionDuration;

    private DateTimeOffset? _adminModeExpiresAt;

    public AdminAuthService(
        IAppSettingsStore settingsStore,
        IPasswordHasher passwordHasher,
        TimeProvider? timeProvider = null,
        TimeSpan? adminSessionDuration = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(passwordHasher);

        _settingsStore = settingsStore;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _adminSessionDuration = adminSessionDuration ?? DefaultAdminSessionDuration;

        if (_adminSessionDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(adminSessionDuration));
        }
    }

    public bool IsAdminMode =>
        _adminModeExpiresAt is DateTimeOffset expiresAt && _timeProvider.GetUtcNow() < expiresAt;

    public async Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default)
    {
        var hash = await _settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken);
        return !string.IsNullOrWhiteSpace(hash);
    }

    public async Task<AdminPasswordResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (await HasPasswordAsync(cancellationToken))
        {
            return AdminPasswordResult.AlreadyConfigured;
        }

        if (password.Length < MinPasswordLength)
        {
            return AdminPasswordResult.TooShort;
        }

        if (password.Trim().Length == 0)
        {
            return AdminPasswordResult.InvalidCharacters;
        }

        await _settingsStore.SetAsync(PasswordHashSettingKey, _passwordHasher.Hash(password), cancellationToken);
        EnterAdminMode();

        return AdminPasswordResult.Success;
    }

    public async Task<AdminAuthenticationResult> TryEnterAdminModeAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        var storedHash = await _settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return AdminAuthenticationResult.NotConfigured;
        }

        if (!_passwordHasher.Verify(password, storedHash))
        {
            return AdminAuthenticationResult.InvalidPassword;
        }

        EnterAdminMode();
        return AdminAuthenticationResult.Success;
    }

    public void ExitAdminMode() => _adminModeExpiresAt = null;

    private void EnterAdminMode() =>
        _adminModeExpiresAt = _timeProvider.GetUtcNow() + _adminSessionDuration;

    public const int MinPasswordLength = 8;
}
