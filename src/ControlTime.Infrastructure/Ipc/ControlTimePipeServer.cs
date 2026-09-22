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
    private readonly Func<int, int?> _clientSessionResolver;
    private readonly string _pipeName;
    private readonly ILogger<ControlTimePipeServer>? _logger;

    public ControlTimePipeServer(
        IAdminAuthService adminAuthService,
        ActivityReportSink sink,
        Func<CancellationToken, Task<ServiceStatus?>> statusProvider,
        ILogger<ControlTimePipeServer>? logger = null,
        string pipeName = DefaultPipeName,
        Func<int, int?>? clientSessionResolver = null)
    {
        ArgumentNullException.ThrowIfNull(adminAuthService);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(statusProvider);

        _adminAuthService = adminAuthService;
        _sink = sink;
        _statusProvider = statusProvider;
        _clientSessionResolver = clientSessionResolver ?? ResolveClientSession;
        _logger = logger;
        _pipeName = pipeName;
    }

    private static int? ResolveClientSession(int processId)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(processId).SessionId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream stream;
            Microsoft.Win32.SafeHandles.SafePipeHandle handle;

            try
            {
                // The stream shares ownership of the handle: keep a reference
                // so the client process can be identified while connected.
                handle = new Microsoft.Win32.SafeHandles.SafePipeHandle(
                    WindowsNamedPipes.CreatePipe(
                        _pipeName,
                        WindowsNamedPipes.SystemAdminsInteractiveSddl),
                    ownsHandle: true);

                stream = new NamedPipeServerStream(
                    PipeDirection.InOut,
                    isAsync: true,
                    isConnected: false,
                    handle);
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
                () => HandleConnectionAsync(stream, handle, cancellationToken),
                CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream stream,
        Microsoft.Win32.SafeHandles.SafePipeHandle handle,
        CancellationToken cancellationToken)
    {
        try
        {
            int? clientSessionId = TryResolveClientSession(handle);

            await using (stream)
            using (var reader = new StreamReader(stream))
            using (var writer = new StreamWriter(stream) { AutoFlush = true })
            {
                string? line;

                while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
                {
                    IpcResponse response = await ProcessAsync(line, clientSessionId, cancellationToken);
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

    private int? TryResolveClientSession(Microsoft.Win32.SafeHandles.SafePipeHandle handle)
    {
        try
        {
            if (!WindowsNamedPipes.TryGetClientProcessId(handle, out int clientProcessId))
            {
                return null;
            }

            return _clientSessionResolver(clientProcessId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "No se pudo determinar la sesión de Windows del cliente IPC de ControlTime.");
            return null;
        }
    }

    private async Task<IpcResponse> ProcessAsync(
        string line,
        int? clientSessionId,
        CancellationToken cancellationToken)
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
            return await DispatchAsync(request, clientSessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "El servicio IPC de ControlTime no pudo procesar {Kind}.", request.Kind);
            return new IpcResponse(false);
        }
    }

    private async Task<IpcResponse> DispatchAsync(
        IpcRequest request,
        int? clientSessionId,
        CancellationToken cancellationToken)
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

                if (clientSessionId is not int sessionId)
                {
                    _logger?.LogWarning(
                        "Informe de actividad rechazado: no se pudo atribuir a una sesión de Windows.");
                    return new IpcResponse(false);
                }

                _sink.Report(sessionId, new ActivityStatus(report.State, report.LastInputAt, report.Timestamp));
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
