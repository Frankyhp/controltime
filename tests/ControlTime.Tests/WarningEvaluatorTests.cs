namespace ControlTime.Tests;

public class WarningEvaluatorTests
{
    private static readonly WarningEvaluator Evaluator = new();

    private static DashboardSnapshot Snapshot(
        AccessState state,
        TimeSpan? remaining) =>
        new(
            state,
            null,
            remaining,
            null,
            TimeSpan.FromHours(2),
            TimeSpan.Zero,
            null,
            true,
            TimeSpan.FromHours(1));

    [Theory]
    [InlineData(15, 1, null)]
    [InlineData(15, 0, 15)]
    [InlineData(14, 59, 15)]
    [InlineData(5, 0, 5)]
    [InlineData(4, 59, 5)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 0, 1)]
    public void Evaluate_ReturnsMostUrgentCrossedThreshold(
        int minutes,
        int seconds,
        int? expectedMinutes)
    {
        var remaining = new TimeSpan(0, minutes, seconds);

        WarningThreshold? threshold = Evaluator.Evaluate(Snapshot(AccessState.Allowed, remaining));

        if (expectedMinutes is null)
        {
            Assert.Null(threshold);
        }
        else
        {
            Assert.NotNull(threshold);
            Assert.Equal(expectedMinutes.Value, threshold.Minutes);
        }
    }

    [Fact]
    public void Evaluate_UnlimitedRemaining_ReturnsNull()
    {
        Assert.Null(Evaluator.Evaluate(Snapshot(AccessState.Allowed, null)));
    }

    [Fact]
    public void Evaluate_WarningState_EvaluatesSameAsAllowed()
    {
        WarningThreshold? threshold = Evaluator.Evaluate(
            Snapshot(AccessState.Warning, TimeSpan.FromMinutes(3)));

        Assert.Equal(5, threshold?.Minutes);
    }

    [Fact]
    public void Evaluate_Blocked_ReturnsNull()
    {
        Assert.Null(Evaluator.Evaluate(Snapshot(AccessState.Blocked, TimeSpan.FromMinutes(2))));
        Assert.Null(Evaluator.Evaluate(Snapshot(AccessState.Blocked, null)));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WarningEvaluator([new WarningThreshold(TimeSpan.Zero)]));
    }
}
