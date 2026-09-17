namespace ControlTime.Tests;

public class AllowedScheduleTests
{
    private static AllowedSchedule CreateSchedule(int startHour, int endHour)
    {
        var result = AllowedSchedule.TryCreate(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), out var schedule);
        Assert.True(result.Success, result.Error);
        return schedule!;
    }

    [Fact]
    public void TryCreate_ValidRange_Succeeds()
    {
        var result = AllowedSchedule.TryCreate(new TimeOnly(17, 0), new TimeOnly(21, 0), out var schedule);

        Assert.True(result.Success);
        Assert.NotNull(schedule);
        Assert.Equal(new TimeOnly(17, 0), schedule!.Start);
        Assert.Equal(new TimeOnly(21, 0), schedule!.End);
    }

    [Theory]
    [InlineData(21, 17)]
    [InlineData(17, 17)]
    [InlineData(0, 0)]
    public void TryCreate_InvalidOrWrappingRange_Fails(int startHour, int endHour)
    {
        var result = AllowedSchedule.TryCreate(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), out var schedule);

        Assert.True(result.Failed);
        Assert.Null(schedule);
    }

    [Fact]
    public void IsWithin_ExactlyAtStart_IsInside()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.True(schedule.IsWithin(new TimeOnly(17, 0)));
    }

    [Fact]
    public void IsWithin_ExactlyAtEnd_IsOutside()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.False(schedule.IsWithin(new TimeOnly(21, 0)));
    }

    [Fact]
    public void IsWithin_OneMinuteBeforeStart_IsOutside()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.False(schedule.IsWithin(new TimeOnly(16, 59)));
    }

    [Fact]
    public void IsWithin_OneMinuteBeforeEnd_IsInside()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.True(schedule.IsWithin(new TimeOnly(20, 59)));
    }

    [Fact]
    public void IsWithin_MidRange_IsInside()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.True(schedule.IsWithin(new TimeOnly(19, 30)));
    }

    [Fact]
    public void IsBeforeSchedule_OneMinuteBeforeStart_IsTrue()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.True(schedule.IsBeforeSchedule(new TimeOnly(16, 59)));
        Assert.False(schedule.IsBeforeSchedule(new TimeOnly(17, 0)));
    }

    [Fact]
    public void IsAfterSchedule_ExactlyAtEnd_IsTrue()
    {
        var schedule = CreateSchedule(17, 21);

        Assert.False(schedule.IsAfterSchedule(new TimeOnly(20, 59)));
        Assert.True(schedule.IsAfterSchedule(new TimeOnly(21, 0)));
    }
}
