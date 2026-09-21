namespace ControlTime.Tests;

public class UsageSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryCreate_ValidTimes_ExposesFieldsAndDuration()
    {
        var result = UsageSession.TryCreate(Start, Start.AddMinutes(90), out var session);

        Assert.True(result.Success, result.Error);
        Assert.False(session!.IsOpen);
        Assert.Equal(Start, session.StartedAt);
        Assert.Equal(Start.AddMinutes(90), session.EndedAt);
        Assert.Equal(TimeSpan.FromMinutes(90), session.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void TryCreate_EndNotAfterStart_Rejected(int durationMinutes)
    {
        var result = UsageSession.TryCreate(Start, Start.AddMinutes(durationMinutes), out var session);

        Assert.True(result.Failed);
        Assert.Null(session);
    }

    [Fact]
    public void TryCreateOpen_HasNoEndAndNoDuration()
    {
        var result = UsageSession.TryCreateOpen(Start, out var session);

        Assert.True(result.Success, result.Error);
        Assert.True(session!.IsOpen);
        Assert.Null(session.EndedAt);
        Assert.Null(session.Duration);
    }

    [Fact]
    public void TryClose_ValidEnd_ReturnsNewClosedSessionKeepingIdentity()
    {
        UsageSession.TryCreateOpen(Start, out var open);

        var result = open!.TryClose(Start.AddMinutes(45), out var closed);

        Assert.True(result.Success, result.Error);
        Assert.Equal(open.Id, closed!.Id);
        Assert.Equal(open.StartedAt, closed.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(45), closed.Duration);
        Assert.True(open.IsOpen);
    }

    [Fact]
    public void TryClose_EndBeforeStart_Rejected()
    {
        UsageSession.TryCreateOpen(Start, out var open);

        var result = open!.TryClose(Start.AddMinutes(-5), out var closed);

        Assert.True(result.Failed);
        Assert.Null(closed);
    }

    [Fact]
    public void TryClose_AlreadyClosed_Rejected()
    {
        UsageSession.TryCreate(Start, Start.AddMinutes(30), out var closedSession);

        var result = closedSession!.TryClose(Start.AddMinutes(60), out var again);

        Assert.True(result.Failed);
        Assert.Null(again);
    }
}
