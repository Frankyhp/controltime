using ControlTime.Infrastructure.Ipc;

namespace ControlTime.Tests;

public class ActivityReportSinkTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptySinkHasNoReports()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);

        Assert.False(sink.HasFreshReportForSession(1, TimeSpan.FromSeconds(5)));
        Assert.Null(sink.TryGetFreshForSession(1, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ReportsAreAttributedPerSession()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);

        sink.Report(1, new ActivityStatus(ActivityState.Idle, BaseTime, BaseTime));
        sink.Report(2, new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));

        Assert.Equal(
            ActivityState.Idle,
            sink.TryGetFreshForSession(1, TimeSpan.FromSeconds(5))!.State);
        Assert.Equal(
            ActivityState.Active,
            sink.TryGetFreshForSession(2, TimeSpan.FromSeconds(5))!.State);
        Assert.Null(sink.TryGetFreshForSession(3, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void FreshnessIsTrackedPerSession()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);

        sink.Report(1, new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));
        Assert.True(sink.HasFreshReportForSession(1, TimeSpan.FromSeconds(5)));

        clock.Now = BaseTime.AddSeconds(3);
        sink.Report(2, new ActivityStatus(ActivityState.Idle, BaseTime, BaseTime));

        clock.Now = BaseTime.AddSeconds(5).AddMilliseconds(1);

        Assert.False(sink.HasFreshReportForSession(1, TimeSpan.FromSeconds(5)));
        Assert.True(sink.HasFreshReportForSession(2, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void LatestReportPerSessionWins()
    {
        var clock = new FixedTimeProvider(BaseTime);
        var sink = new ActivityReportSink(clock);

        sink.Report(1, new ActivityStatus(ActivityState.Active, BaseTime, BaseTime));
        sink.Report(1, new ActivityStatus(ActivityState.Locked, BaseTime, BaseTime));

        Assert.Equal(
            ActivityState.Locked,
            sink.TryGetFreshForSession(1, TimeSpan.FromSeconds(5))!.State);
    }
}
