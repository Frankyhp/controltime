using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Contracts;
using ControlTime.Core.Domain;
using ControlTime.Core.Services;
using ControlTime.Infrastructure.Windows;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure.Ipc;

public sealed class ControlTimePipeServer
{
    public const string DefaultPipeName = "ControlTime.Monitoring";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAdminAuthService _adminAuthService;
    private readonly ActivityReportSink _sink;
    private readonly Func<CancellationToken, Task<ServiceStatus?>> _statusProvider;
    private readonly string _pipeName;
    private readonly ILogger<ControlTimePipeServer>? _logger;

    public ControlTimePipeServer(
        IAdminAuthService adminAuthService,
        ActivityReportSink sink,
        Func<CancellationToken, Task<ServiceStatus?>> statusProvider,
        ILogger<ControlTimePipeServer>? logger = null,
        string pipeName = DefaultPipeName)
    {
        ArgumentNullException.ThrowIfNull(adminAuthService);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(statusProvider);

        _adminAuthService = adminAuthService;
        _sink = sink;
        _statusProvider = statusProvider;
        _logger = logger;
        _pipeName = pipeName;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream stream;

            try
            {
                stream = CreateStream();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "No se pudo crear la instancia IPC de ControlTime.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            try
            {
                await stream.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await stream.DisposeAsync();
                return;
            }
            catch (Exception ex)
            {
                await stream.DisposeAsync();
                _logger?.LogError(ex, "No se pudo aceptar la conexión IPC de ControlTime.");
                continue;
            }

            _ = Task.Run(
                () => HandleConnectionAsync(stream, cancellationToken),
                CancellationToken.None);
        }
    }

    private NamedPipeServerStream CreateStream()
    {
        // The stream takes ownership of the handle; do not dispose it here.
        var handle = new Microsoft.Win32.SafeHandles.SafePipeHandle(
            WindowsNamedPipes.CreatePipe(
                _pipeName,
                WindowsNamedPipes.SystemAdminsInteractiveSddl),
            ownsHandle: true);

        return new NamedPipeServerStream(
            PipeDirection.InOut,
            isAsync: true,
            isConnected: false,
            handle);
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await using (stream)
            using (var reader = new StreamReader(stream))
            using (var writer = new StreamWriter(stream) { AutoFlush = true })
            {
                string? line;

                while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
                {
                    IpcResponse response = await ProcessAsync(line, cancellationToken);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "La conexión IPC de ControlTime finalizó de forma inesperada.");
        }
    }

    private async Task<IpcResponse> ProcessAsync(string line, CancellationToken cancellationToken)
    {
        IpcRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<IpcRequest>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return new IpcResponse(false);
        }

        if (request is null)
        {
            return new IpcResponse(false);
        }

        try
        {
            return await DispatchAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "El servicio IPC de ControlTime no pudo procesar {Kind}.", request.Kind);
            return new IpcResponse(false);
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        switch (request.Kind)
        {
            case IpcRequestKind.GetStatus:
            {
                ServiceStatus? status = await _statusProvider(cancellationToken);
                return status is null
                    ? new IpcResponse(false)
                    : new IpcResponse(true, status);
            }

            case IpcRequestKind.ReportActivity:
            {
                if (request.Activity is not { } report)
                {
                    return new IpcResponse(false);
                }

                _sink.Report(new ActivityStatus(report.State, report.LastInputAt, report.Timestamp));
                return new IpcResponse(true);
            }

            case IpcRequestKind.EnterAdminMode:
            {
                AdminAuthenticationResult result =
                    await _adminAuthService.TryEnterAdminModeAsync(request.Password ?? string.Empty, cancellationToken);

                return new IpcResponse(result == AdminAuthenticationResult.Success, ErrorCode: (int)result);
            }

            case IpcRequestKind.ExitAdminMode:
            {
                _adminAuthService.ExitAdminMode();
                return new IpcResponse(true);
            }

            case IpcRequestKind.VerifyPassword:
            {
                bool ok = await _adminAuthService.VerifyPasswordAsync(request.Password ?? string.Empty, cancellationToken);
                return new IpcResponse(ok);
            }

            case IpcRequestKind.SetPassword:
            {
                AdminPasswordResult result =
                    await _adminAuthService.SetPasswordAsync(request.Password ?? string.Empty, cancellationToken);

                return new IpcResponse(result == AdminPasswordResult.Success, ErrorCode: (int)result);
            }

            case IpcRequestKind.ChangePassword:
            {
                AdminPasswordChangeResult result = await _adminAuthService.ChangePasswordAsync(
                    request.Password ?? string.Empty,
                    request.NewPassword ?? string.Empty,
                    cancellationToken);

                return new IpcResponse(result == AdminPasswordChangeResult.Success, ErrorCode: (int)result);
            }

            case IpcRequestKind.HasPassword:
            {
                bool hasPassword = await _adminAuthService.HasPasswordAsync(cancellationToken);
                return new IpcResponse(hasPassword);
            }

            default:
                return new IpcResponse(false);
        }
    }
}
