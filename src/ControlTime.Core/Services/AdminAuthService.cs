using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public enum AdminAuthenticationResult
{
    Success,
    NotConfigured,
    InvalidPassword,
    LockedOut,
    ServiceUnavailable
}

public enum AdminPasswordResult
{
    Success,
    AlreadyConfigured,
    TooShort,
    InvalidCharacters,
    ServiceUnavailable
}

public enum AdminPasswordChangeResult
{
    Success,
    NotConfigured,
    InvalidPassword,
    TooShort,
    InvalidCharacters,
    LockedOut,
    ServiceUnavailable
}

public interface IAdminAuthService
{
    bool IsAdminMode { get; }

    Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default);

    Task<AdminPasswordResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task<AdminPasswordChangeResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<AdminAuthenticationResult> TryEnterAdminModeAsync(string password, CancellationToken cancellationToken = default);

    Task<bool> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default);

    void ExitAdminMode();
}

public sealed class AdminAuthService : IAdminAuthService
{
    public const string PasswordHashSettingKey = "admin.password.hash";

    public const int MaxFailedAuthenticationAttempts = 5;

    public static readonly TimeSpan DefaultAdminSessionDuration = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    private readonly IAppSettingsStore _settingsStore;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _adminSessionDuration;

    private DateTimeOffset? _adminModeExpiresAt;
    private int _consecutiveFailedAttempts;
    private DateTimeOffset? _lockoutExpiresAt;

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

    public DateTimeOffset? AdminModeExpiresAt => IsAdminMode ? _adminModeExpiresAt : null;

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

    public async Task<AdminPasswordChangeResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        if (IsLockedOut)
        {
            return AdminPasswordChangeResult.LockedOut;
        }

        var storedHash = await _settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return AdminPasswordChangeResult.NotConfigured;
        }

        if (!_passwordHasher.Verify(currentPassword, storedHash))
        {
            RegisterFailedAttempt();
            return AdminPasswordChangeResult.InvalidPassword;
        }

        RegisterSuccessfulAttempt();
        EnterAdminMode();

        if (newPassword.Length < MinPasswordLength)
        {
            return AdminPasswordChangeResult.TooShort;
        }

        if (newPassword.Trim().Length == 0)
        {
            return AdminPasswordChangeResult.InvalidCharacters;
        }

        await _settingsStore.SetAsync(PasswordHashSettingKey, _passwordHasher.Hash(newPassword), cancellationToken);

        return AdminPasswordChangeResult.Success;
    }

    public async Task<AdminAuthenticationResult> TryEnterAdminModeAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (IsLockedOut)
        {
            return AdminAuthenticationResult.LockedOut;
        }

        var storedHash = await _settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return AdminAuthenticationResult.NotConfigured;
        }

        if (!_passwordHasher.Verify(password, storedHash))
        {
            RegisterFailedAttempt();
            return AdminAuthenticationResult.InvalidPassword;
        }

        RegisterSuccessfulAttempt();
        EnterAdminMode();
        return AdminAuthenticationResult.Success;
    }

    public async Task<bool> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (IsLockedOut)
        {
            return false;
        }

        var storedHash = await _settingsStore.GetAsync(PasswordHashSettingKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        if (!_passwordHasher.Verify(password, storedHash))
        {
            RegisterFailedAttempt();
            return false;
        }

        RegisterSuccessfulAttempt();
        return true;
    }

    public void ExitAdminMode() => _adminModeExpiresAt = null;

    private void EnterAdminMode() =>
        _adminModeExpiresAt = _timeProvider.GetUtcNow() + _adminSessionDuration;

    private bool IsLockedOut =>
        _lockoutExpiresAt is DateTimeOffset expiresAt && _timeProvider.GetUtcNow() < expiresAt;

    private void RegisterFailedAttempt()
    {
        _consecutiveFailedAttempts++;

        if (_consecutiveFailedAttempts >= MaxFailedAuthenticationAttempts)
        {
            _lockoutExpiresAt = _timeProvider.GetUtcNow() + LockoutDuration;
            _consecutiveFailedAttempts = 0;
        }
    }

    private void RegisterSuccessfulAttempt()
    {
        _consecutiveFailedAttempts = 0;
        _lockoutExpiresAt = null;
    }

    public const int MinPasswordLength = 8;
}
