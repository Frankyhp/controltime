using ControlTime.Core.Contracts;
using ControlTime.Infrastructure.Ipc;

namespace ControlTime.Tests;

public sealed class IpcTests : IAsyncLifetime
{
    private readonly string _pipeName = $"ControlTime.Test.{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _cancellation = new(TimeSpan.FromSeconds(30));
    private readonly ActivityReportSink _sink = new();
    private readonly AdminAuthService _adminAuthService;
    private ServiceStatus? _currentStatus;
    private ControlTimePipeServer? _server;
    private Task? _serverTask;

    public IpcTests()
    {
        _adminAuthService = new AdminAuthService(
            new InMemoryAppSettingsStore(),
            new Pbkdf2PasswordHasher());
    }

    private static ServiceStatus SampleStatus(bool isAdminMode = false) =>
        new(
            ActivityState.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AccessState.Allowed,
            null,
            TimeSpan.FromMinutes(10),
            null,
            TimeSpan.FromMinutes(120),
            TimeSpan.Zero,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            true,
            TimeSpan.FromMinutes(110),
            isAdminMode,
            true,
            true,
            false,
            DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _adminAuthService.SetPasswordAsync("clave-super-segura-9");
        _adminAuthService.ExitAdminMode();

        _server = new ControlTimePipeServer(
            _adminAuthService,
            _sink,
            _ => Task.FromResult(_currentStatus),
            logger: null,
            pipeName: _pipeName);

        _serverTask = Task.Run(
            () => _server.RunAsync(_cancellation.Token));

        await WaitUntilReachableAsync();
    }

    public async Task DisposeAsync()
    {
        _cancellation.Cancel();

        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch (Exception)
            {
            }
        }

        _cancellation.Dispose();
    }

    private ControlTimePipeClient CreateClient() => new(_pipeName, 3000);

    private async Task WaitUntilReachableAsync()
    {
        ControlTimePipeClient client = CreateClient();

        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            _currentStatus = SampleStatus();

            if (await client.GetStatusAsync(_cancellation.Token) is not null)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The IPC test server did not become reachable.");
    }

    [Fact]
    public async Task GetStatus_ReturnsCurrentStatus()
    {
        _currentStatus = SampleStatus(isAdminMode: true);

        ServiceStatus? status = await CreateClient()
            .GetStatusAsync(_cancellation.Token);

        Assert.NotNull(status);
        Assert.Equal(AccessState.Allowed, status.Access);
        Assert.Equal(ActivityState.Active, status.Activity);
        Assert.True(status.IsAdminMode);
        Assert.True(status.HasEnabledRules);
        Assert.Equal(new TimeOnly(9, 0), status.ScheduleStart);
        Assert.Equal(new TimeOnly(18, 0), status.ScheduleEnd);
    }

    [Fact]
    public async Task GetStatus_WithoutSnapshot_ReturnsNull()
    {
        _currentStatus = null;

        ServiceStatus? status = await CreateClient()
            .GetStatusAsync(_cancellation.Token);

        Assert.Null(status);
    }

    [Fact]
    public async Task ReportActivity_LandsInSinkAttributedToClientSession()
    {
        var report = new ActivityStatus(
            ActivityState.Idle,
            DateTimeOffset.UtcNow.AddMinutes(-7),
            DateTimeOffset.UtcNow);

        await CreateClient().ReportActivityAsync(report, _cancellation.Token);

        int clientSessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;

        ActivityStatus? fresh = _sink.TryGetFreshForSession(clientSessionId, TimeSpan.FromSeconds(10));

        Assert.NotNull(fresh);
        Assert.Equal(ActivityState.Idle, fresh!.State);

        Assert.Null(_sink.TryGetFreshForSession(clientSessionId + 4242, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task EnterAdminMode_WithCorrectPassword_Succeeds()
    {
        AdminAuthenticationResult? result = await CreateClient()
            .EnterAdminModeAsync("clave-super-segura-9", _cancellation.Token);

        Assert.Equal(AdminAuthenticationResult.Success, result);
        Assert.True(_adminAuthService.IsAdminMode);
    }

    [Fact]
    public async Task EnterAdminMode_WithWrongPassword_Fails()
    {
        AdminAuthenticationResult? result = await CreateClient()
            .EnterAdminModeAsync("incorrecta12345", _cancellation.Token);

        Assert.Equal(AdminAuthenticationResult.InvalidPassword, result);
        Assert.False(_adminAuthService.IsAdminMode);
    }

    [Fact]
    public async Task HasPasswordAndVerifyPassword_WorkThroughServer()
    {
        ControlTimePipeClient client = CreateClient();

        Assert.True(await client.HasPasswordAsync(_cancellation.Token));
        Assert.True(await client.VerifyPasswordAsync("clave-super-segura-9", _cancellation.Token));
        Assert.False(await client.VerifyPasswordAsync("no-es-la-clave-1", _cancellation.Token));
    }

    [Fact]
    public async Task GatewayAdminAuthService_ReflectsServerAdminMode()
    {
        ControlTimePipeClient client = CreateClient();
        var gatewayAuth = new GatewayAdminAuthService(client);

        Assert.False(gatewayAuth.IsAdminMode);

        var entered = await gatewayAuth.TryEnterAdminModeAsync(
            "clave-super-segura-9",
            _cancellation.Token);

        Assert.Equal(AdminAuthenticationResult.Success, entered);

        gatewayAuth.UpdateStatus(SampleStatus(isAdminMode: true));
        Assert.True(gatewayAuth.IsAdminMode);

        gatewayAuth.UpdateStatus(SampleStatus(isAdminMode: false));
        Assert.False(gatewayAuth.IsAdminMode);
    }

    [Fact]
    public async Task UnavailableService_MapsToServiceUnavailable()
    {
        var missingPipeClient = new ControlTimePipeClient(
            $"ControlTime.Missing.{Guid.NewGuid():N}",
            connectTimeoutMilliseconds: 200);

        var gatewayAuth = new GatewayAdminAuthService(missingPipeClient);

        Assert.Null(await missingPipeClient.GetStatusAsync());
        Assert.False(missingPipeClient.IsServiceAvailable);
        Assert.Equal(
            AdminAuthenticationResult.ServiceUnavailable,
            await gatewayAuth.TryEnterAdminModeAsync("cualquiera123"));
        Assert.Equal(
            AdminPasswordResult.ServiceUnavailable,
            await gatewayAuth.SetPasswordAsync("cualquiera123"));
        Assert.False(await gatewayAuth.HasPasswordAsync());
    }
}
