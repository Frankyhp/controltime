using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Contracts;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.Infrastructure.Ipc;

public sealed class ControlTimePipeClient : IControlTimeGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _pipeName;
    private readonly int _connectTimeoutMilliseconds;

    private volatile bool _isServiceAvailable;

    public ControlTimePipeClient(
        string pipeName = ControlTimePipeServer.DefaultPipeName,
        int connectTimeoutMilliseconds = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        _pipeName = pipeName;
        _connectTimeoutMilliseconds = connectTimeoutMilliseconds;
    }

    public bool IsServiceAvailable => _isServiceAvailable;

    public async Task<ServiceStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IpcResponse? response = await SendAsync(
            new IpcRequest(IpcRequestKind.GetStatus),
            cancellationToken);

        return response is { Success: true } ? response.Status : null;
    }

    public async Task ReportActivityAsync(
        ActivityStatus activity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        await SendAsync(
            new IpcRequest(
                IpcRequestKind.ReportActivity,
                Activity: new ActivityReportPayload(
                    activity.State,
                    activity.LastInputAt,
                    activity.Timestamp)),
            cancellationToken);
    }

    public async Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default)
    {
        IpcResponse? response = await SendAsync(
            new IpcRequest(IpcRequestKind.HasPassword),
            cancellationToken);

        return response?.Success ?? false;
    }

    public async Task<AdminPasswordResult?> SetPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        IpcResponse? response = await SendAsync(
            new IpcRequest(IpcRequestKind.SetPassword, Password: password),
            cancellationToken);

        return ToResult(response, AdminPasswordResult.ServiceUnavailable);
    }

    public async Task<AdminPasswordChangeResult?> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        IpcResponse? response = await SendAsync(
            new IpcRequest(IpcRequestKind.ChangePassword, Password: currentPassword, NewPassword: newPassword),
            cancellationToken);

        return ToResult(response, AdminPasswordChangeResult.ServiceUnavailable);
    }

    public async Task<AdminAuthenticationResult?> EnterAdminModeAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        IpcResponse? response = await SendAsync(
            new IpcRequest(IpcRequestKind.EnterAdminMode, Password: password),
            cancellationToken);

        return ToResult(response, AdminAuthenticationResult.ServiceUnavailable);
    }

    public async Task<bool> VerifyPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        IpcResponse? response = await SendAsync(
            new IpcRequest(IpcRequestKind.VerifyPassword, Password: password),
            cancellationToken);

        return response?.Success ?? false;
    }

    public void ExitAdminMode()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SendAsync(new IpcRequest(IpcRequestKind.ExitAdminMode), CancellationToken.None);
            }
            catch (Exception)
            {
            }
        });
    }

    private static T? ToResult<T>(IpcResponse? response, T unavailable)
        where T : struct
    {
        if (response is null)
        {
            return unavailable;
        }

        return response.ErrorCode is int code
            ? (T)Enum.ToObject(typeof(T), code)
            : unavailable;
    }

    private async Task<IpcResponse?> SendAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(_connectTimeoutMilliseconds, cancellationToken);

            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));

            string? line = await reader.ReadLineAsync(cancellationToken);

            IpcResponse? response = line is null
                ? null
                : JsonSerializer.Deserialize<IpcResponse>(line, JsonOptions);

            _isServiceAvailable = response is not null;
            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _isServiceAvailable = false;
            return null;
        }
    }
}
