using ControlTime.Infrastructure.Ipc;

namespace ControlTime.Tests;

public class CompositeActivityMonitorTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutReportsUsesFallback()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback, new ActivityReportSink(clock), TimeSpan.FromSeconds(5));

        Assert.Equal(ActivityState.Locked, monitor.GetCurrentStatus().State);
    }

    [Fact]
    public void FreshReportWinsOverFallback()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback, sink, TimeSpan.FromSeconds(5));

        sink.Report(new ActivityStatus(ActivityState.Idle, BaseTime.AddMinutes(-6), BaseTime));
        clock.Now = BaseTime.AddSeconds(4);

        var status = monitor.GetCurrentStatus();

        Assert.Equal(ActivityState.Idle, status.State);
        Assert.Equal(0, fallback.ProbeCount);
    }

    [Fact]
    public void StaleReportFallsBackToWts()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);
        var fallback = new FakeActivityMonitor(
            new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));
        var monitor = new CompositeActivityMonitor(
            fallback, sink, TimeSpan.FromSeconds(5));

        sink.Report(new ActivityStatus(ActivityState.Idle, BaseTime, BaseTime));
        clock.Now = BaseTime.AddSeconds(6);

        Assert.Equal(ActivityState.Active, monitor.GetCurrentStatus().State);
    }

    [Fact]
    public void SinkReportsFreshness()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);

        Assert.False(sink.HasFreshReport(TimeSpan.FromSeconds(5)));

        sink.Report(new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));
        Assert.True(sink.HasFreshReport(TimeSpan.FromSeconds(5)));

        clock.Now = BaseTime.AddSeconds(5).AddMilliseconds(1);
        Assert.False(sink.HasFreshReport(TimeSpan.FromSeconds(5)));
    }
}
