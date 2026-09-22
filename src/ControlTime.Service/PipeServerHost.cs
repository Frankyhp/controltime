using ControlTime.Infrastructure.Ipc;
using Microsoft.Extensions.Hosting;

namespace ControlTime.Service;

public sealed class PipeServerHost(ControlTimePipeServer pipeServer) : BackgroundService
{
    private readonly ControlTimePipeServer _pipeServer = pipeServer
        ?? throw new ArgumentNullException(nameof(pipeServer));

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _pipeServer.RunAsync(stoppingToken);
}
