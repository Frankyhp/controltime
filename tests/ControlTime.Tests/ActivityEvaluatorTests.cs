using ControlTime.Core.Domain;
using ControlTime.Core.Services;

namespace ControlTime.Tests;

public class ActivityEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    private static DateTimeOffset MinutesAgo(int minutes) => Now.AddMinutes(-minutes);

    [Fact]
    public void Evaluate_RecentInputNotLocked_ReturnsActive()
    {
        var status = ActivityEvaluator.Evaluate(Now, MinutesAgo(1), isLocked: false, Threshold);

        Assert.Equal(ActivityState.Active, status.State);
    }

    [Fact]
    public void Evaluate_InputExactlyOneSecondBeforeThreshold_ReturnsActive()
    {
        var lastInput = Now - Threshold + TimeSpan.FromSeconds(1);

        var status = ActivityEvaluator.Evaluate(Now, lastInput, isLocked: false, Threshold);

        Assert.Equal(ActivityState.Active, status.State);
    }

    [Fact]
    public void Evaluate_InputExactlyAtThreshold_ReturnsIdle()
    {
        var status = ActivityEvaluator.Evaluate(Now, MinutesAgo(5), isLocked: false, Threshold);

        Assert.Equal(ActivityState.Idle, status.State);
    }

    [Fact]
    public void Evaluate_InputLongerAgoThanThreshold_ReturnsIdle()
    {
        var status = ActivityEvaluator.Evaluate(Now, MinutesAgo(30), isLocked: false, Threshold);

        Assert.Equal(ActivityState.Idle, status.State);
    }

    [Fact]
    public void Evaluate_LockedWithRecentInput_ReturnsLocked()
    {
        var status = ActivityEvaluator.Evaluate(Now, Now, isLocked: true, Threshold);

        Assert.Equal(ActivityState.Locked, status.State);
    }

    [Fact]
    public void Evaluate_LockedWhileIdle_ReturnsLocked()
    {
        var status = ActivityEvaluator.Evaluate(Now, MinutesAgo(30), isLocked: true, Threshold);

        Assert.Equal(ActivityState.Locked, status.State);
    }

    [Fact]
    public void Evaluate_PreservesLastInputAndTimestamp()
    {
        var lastInput = MinutesAgo(2);

        var status = ActivityEvaluator.Evaluate(Now, lastInput, isLocked: false, Threshold);

        Assert.Equal(lastInput, status.LastInputAt);
        Assert.Equal(Now, status.Timestamp);
    }

    [Fact]
    public void Evaluate_FutureLastInput_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ActivityEvaluator.Evaluate(Now, Now.AddSeconds(1), isLocked: false, Threshold));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Evaluate_NonPositiveThreshold_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ActivityEvaluator.Evaluate(Now, MinutesAgo(1), isLocked: false, TimeSpan.FromSeconds(seconds)));
    }
}
