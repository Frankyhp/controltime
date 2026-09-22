using ControlTime.Infrastructure.Ipc;

namespace ControlTime.Tests;

public class CompositeActivityMonitorTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private const int ConsoleSession = 1;
    private const int OtherSession = 2;

    [Fact]
    public void WithoutReportsUsesFallback()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback,
            new ActivityReportSink(clock),
            () => ConsoleSession,
            TimeSpan.FromSeconds(5));

        Assert.Equal(ActivityState.Locked, monitor.GetCurrentStatus().State);
    }

    [Fact]
    public void FreshReportFromConsoleSessionWinsOverFallback()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback,
            sink,
            () => ConsoleSession,
            TimeSpan.FromSeconds(5));

        sink.Report(ConsoleSession, new ActivityStatus(ActivityState.Idle, BaseTime.AddMinutes(-6), BaseTime));
        clock.Now = BaseTime.AddSeconds(4);

        var status = monitor.GetCurrentStatus();

        Assert.Equal(ActivityState.Idle, status.State);
        Assert.Equal(0, fallback.ProbeCount);
        Assert.True(monitor.IsUsingReportedActivity());
    }

    [Fact]
    public void FreshReportFromOtherSessionIsIgnored()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback,
            sink,
            () => ConsoleSession,
            TimeSpan.FromSeconds(5));

        sink.Report(OtherSession, new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));

        var status = monitor.GetCurrentStatus();

        Assert.Equal(ActivityState.Locked, status.State);
        Assert.Equal(1, fallback.ProbeCount);
        Assert.False(monitor.IsUsingReportedActivity());
    }

    [Fact]
    public void WithoutActiveConsoleSessionReportsAreIgnored()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback,
            sink,
            () => null,
            TimeSpan.FromSeconds(5));

        sink.Report(ConsoleSession, new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));

        Assert.Equal(ActivityState.Locked, monitor.GetCurrentStatus().State);
        Assert.Equal(1, fallback.ProbeCount);
    }

    [Fact]
    public void StaleReportFallsBackToWts()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback,
            sink,
            () => ConsoleSession,
            TimeSpan.FromSeconds(5));

        sink.Report(ConsoleSession, new ActivityStatus(ActivityState.Idle, BaseTime, BaseTime));
        clock.Now = BaseTime.AddSeconds(6);

        Assert.Equal(ActivityState.Active, monitor.GetCurrentStatus().State);
        Assert.False(monitor.IsUsingReportedActivity());
    }
}
