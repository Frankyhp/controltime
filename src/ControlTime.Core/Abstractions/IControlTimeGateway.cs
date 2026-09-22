using ControlTime.Core.Contracts;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.Core.Abstractions;

public interface IControlTimeGateway
{
    bool IsServiceAvailable { get; }

    Task<ServiceStatus?> GetStatusAsync(CancellationToken cancellationToken = default);

    Task ReportActivityAsync(ActivityStatus activity, CancellationToken cancellationToken = default);

    Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default);

    Task<AdminPasswordResult?> SetPasswordAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<AdminPasswordChangeResult?> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<AdminAuthenticationResult?> EnterAdminModeAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyPasswordAsync(
        string password,
        CancellationToken cancellationToken = default);

    void ExitAdminMode();
}
