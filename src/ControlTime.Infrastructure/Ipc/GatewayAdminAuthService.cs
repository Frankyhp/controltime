using ControlTime.Core.Abstractions;
using ControlTime.Core.Contracts;
using ControlTime.Core.Services;

namespace ControlTime.Infrastructure.Ipc;

public sealed class GatewayAdminAuthService(IControlTimeGateway gateway) : IAdminAuthService
{
    private readonly IControlTimeGateway _gateway = gateway
        ?? throw new ArgumentNullException(nameof(gateway));

    private volatile bool _isAdminMode;

    public bool IsAdminMode => _isAdminMode;

    public void UpdateStatus(ServiceStatus? status) =>
        _isAdminMode = status?.IsAdminMode ?? false;

    public Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default) =>
        _gateway.HasPasswordAsync(cancellationToken);

    public async Task<AdminPasswordResult> SetPasswordAsync(
        string password,
        CancellationToken cancellationToken = default) =>
        await _gateway.SetPasswordAsync(password, cancellationToken)
        ?? AdminPasswordResult.ServiceUnavailable;

    public async Task<AdminPasswordChangeResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        await _gateway.ChangePasswordAsync(currentPassword, newPassword, cancellationToken)
        ?? AdminPasswordChangeResult.ServiceUnavailable;

    public async Task<AdminAuthenticationResult> TryEnterAdminModeAsync(
        string password,
        CancellationToken cancellationToken = default) =>
        await _gateway.EnterAdminModeAsync(password, cancellationToken)
        ?? AdminAuthenticationResult.ServiceUnavailable;

    public Task<bool> VerifyPasswordAsync(
        string password,
        CancellationToken cancellationToken = default) =>
        _gateway.VerifyPasswordAsync(password, cancellationToken);

    public void ExitAdminMode() => _gateway.ExitAdminMode();
}
