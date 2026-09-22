namespace ControlTime.Tests;

public class EnforcementServiceTests
{
    private sealed class FakeSessionLock : ISessionLock
    {
        public int LockCount { get; private set; }

        public bool Result { get; set; } = true;

        public bool ThrowOnCall { get; set; }

        public bool TryLock()
        {
            LockCount++;

            if (ThrowOnCall)
            {
                throw new InvalidOperationException("Simulated lock failure.");
            }

            return Result;
        }
    }

    private sealed class FakeAdminAuthService : IAdminAuthService
    {
        public bool IsAdminMode { get; set; }

        public Task<bool> HasPasswordAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<AdminPasswordResult> SetPasswordAsync(
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AdminPasswordResult.Success);

        public Task<AdminPasswordChangeResult> ChangePasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AdminPasswordChangeResult.Success);

        public Task<AdminAuthenticationResult> TryEnterAdminModeAsync(
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AdminAuthenticationResult.Success);

        public Task<bool> VerifyPasswordAsync(
            string password,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void ExitAdminMode()
        {
        }
    }

    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 21, 22, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public FakeSessionLock SessionLock { get; } = new();

        public FakeAdminAuthService AdminAuthService { get; } = new();

        public EnforcementService Service { get; }

        public Fixture() => Service = new EnforcementService(AdminAuthService, SessionLock);

        public void Process(
            AccessState access,
            ActivityState activity,
            bool hasEnabledRules = true) =>
            Service.Process(new MonitorState(
                new ActivityStatus(activity, BaseTime, BaseTime),
                new DashboardSnapshot(
                    access,
                    null,
                    null,
                    null,
                    TimeSpan.FromHours(1),
                    TimeSpan.Zero,
                    null,
                    hasEnabledRules,
                    TimeSpan.FromHours(1))));
    }

    [Theory]
    [InlineData(AccessState.Allowed, ActivityState.Active)]
    [InlineData(AccessState.Warning, ActivityState.Active)]
    [InlineData(AccessState.Allowed, ActivityState.Idle)]
    [InlineData(AccessState.Warning, ActivityState.Idle)]
    public void NeverLocksWhenNotBlocked(AccessState access, ActivityState activity)
    {
        var fixture = new Fixture();

        fixture.Process(access, activity);
        fixture.Process(access, activity);

        Assert.Equal(0, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void NeverLocksWithoutEnabledRules()
    {
        var fixture = new Fixture();

        fixture.Process(AccessState.Blocked, ActivityState.Active, hasEnabledRules: false);

        Assert.Equal(0, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void NeverLocksWhileWindowsSessionIsAlreadyLocked()
    {
        var fixture = new Fixture();

        fixture.Process(AccessState.Blocked, ActivityState.Locked);
        fixture.Process(AccessState.Blocked, ActivityState.Locked);

        Assert.Equal(0, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void LocksOnceOnEnteringBlockedEpisode()
    {
        var fixture = new Fixture();

        fixture.Process(AccessState.Allowed, ActivityState.Active);
        fixture.Process(AccessState.Blocked, ActivityState.Active);
        fixture.Process(AccessState.Blocked, ActivityState.Active);
        fixture.Process(AccessState.Blocked, ActivityState.Active);

        Assert.Equal(1, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void ReLocksOncePerActivityEpisodeWhileBlocked()
    {
        var fixture = new Fixture();

        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(1, fixture.SessionLock.LockCount);

        fixture.Process(AccessState.Blocked, ActivityState.Locked);
        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(2, fixture.SessionLock.LockCount);

        fixture.Process(AccessState.Blocked, ActivityState.Idle);
        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(3, fixture.SessionLock.LockCount);

        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(3, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void AdminModeSuspendsLocking()
    {
        var fixture = new Fixture();
        fixture.AdminAuthService.IsAdminMode = true;

        fixture.Process(AccessState.Blocked, ActivityState.Active);

        Assert.Equal(0, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void LocksWhenAdminModeExpiresWhileBlocked()
    {
        var fixture = new Fixture();
        fixture.AdminAuthService.IsAdminMode = true;

        fixture.Process(AccessState.Blocked, ActivityState.Active);

        fixture.AdminAuthService.IsAdminMode = false;
        fixture.Process(AccessState.Blocked, ActivityState.Active);
        fixture.Process(AccessState.Blocked, ActivityState.Active);

        Assert.Equal(1, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void LocksAgainOnNewBlockedEpisode()
    {
        var fixture = new Fixture();

        fixture.Process(AccessState.Blocked, ActivityState.Active);
        fixture.Process(AccessState.Allowed, ActivityState.Active);
        fixture.Process(AccessState.Blocked, ActivityState.Active);

        Assert.Equal(2, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void FailedLockIsRetriedOnNextTick()
    {
        var fixture = new Fixture();
        fixture.SessionLock.Result = false;

        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(1, fixture.SessionLock.LockCount);

        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(2, fixture.SessionLock.LockCount);

        fixture.SessionLock.Result = true;
        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(3, fixture.SessionLock.LockCount);

        fixture.Process(AccessState.Blocked, ActivityState.Active);
        Assert.Equal(3, fixture.SessionLock.LockCount);
    }

    [Fact]
    public void ThrowingLockIsRetriedAndNeverCrashes()
    {
        var fixture = new Fixture();
        fixture.SessionLock.ThrowOnCall = true;

        fixture.Process(AccessState.Blocked, ActivityState.Active);

        fixture.SessionLock.ThrowOnCall = false;
        fixture.Process(AccessState.Blocked, ActivityState.Active);

        Assert.Equal(2, fixture.SessionLock.LockCount);
    }
}
