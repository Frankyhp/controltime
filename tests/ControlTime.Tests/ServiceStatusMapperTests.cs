using ControlTime.Core.Contracts;

namespace ControlTime.Tests;

public class ServiceStatusMapperTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    private static MonitorState CreateState()
    {
        var scheduleResult = AllowedSchedule.TryCreate(
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            out var schedule);
        Assert.True(scheduleResult.Success, scheduleResult.Error);

        var activity = new ActivityStatus(ActivityState.Idle, BaseTime.AddMinutes(-10), BaseTime);

        var snapshot = new DashboardSnapshot(
            AccessState.Warning,
            "DailyLimitExhausted",
            TimeSpan.FromMinutes(5),
            BaseTime.AddHours(3),
            TimeSpan.FromMinutes(120),
            TimeSpan.FromMinutes(30),
            schedule,
            true,
            TimeSpan.FromMinutes(100));

        return new MonitorState(activity, snapshot);
    }

    [Fact]
    public void RoundTripPreservesActivityAndSnapshot()
    {
        MonitorState original = CreateState();

        ServiceStatus status = original.ToServiceStatus(
            isAdminMode: true,
            hasPassword: true,
            enforcementEnabled: true,
            usingReportedActivity: true);

        MonitorState restored = status.ToMonitorState();

        Assert.Equal(original.Activity.State, restored.Activity.State);
        Assert.Equal(original.Activity.LastInputAt, restored.Activity.LastInputAt);
        Assert.Equal(original.Activity.Timestamp, restored.Activity.Timestamp);

        Assert.Equal(original.Snapshot.State, restored.Snapshot.State);
        Assert.Equal(original.Snapshot.Reason, restored.Snapshot.Reason);
        Assert.Equal(original.Snapshot.RemainingTime, restored.Snapshot.RemainingTime);
        Assert.Equal(original.Snapshot.NextAllowedAt, restored.Snapshot.NextAllowedAt);
        Assert.Equal(original.Snapshot.ConfiguredDailyLimit, restored.Snapshot.ConfiguredDailyLimit);
        Assert.Equal(original.Snapshot.BonusToday, restored.Snapshot.BonusToday);
        Assert.Equal(original.Snapshot.HasEnabledRules, restored.Snapshot.HasEnabledRules);
        Assert.Equal(original.Snapshot.UsageToday, restored.Snapshot.UsageToday);

        Assert.NotNull(restored.Snapshot.ActiveSchedule);
        Assert.Equal("09:00", restored.Snapshot.ActiveSchedule!.Start.ToString("HH:mm"));
        Assert.Equal("18:00", restored.Snapshot.ActiveSchedule!.End.ToString("HH:mm"));
    }

    [Fact]
    public void RoundTripWithoutScheduleKeepsNullSchedule()
    {
        var activity = new ActivityStatus(ActivityState.Active, BaseTime, BaseTime);
        var snapshot = new DashboardSnapshot(
            AccessState.Allowed,
            null,
            null,
            null,
            null,
            TimeSpan.Zero,
            null,
            false,
            TimeSpan.Zero);

        MonitorState restored = new MonitorState(activity, snapshot)
            .ToServiceStatus(false, false, false, false)
            .ToMonitorState();

        Assert.Null(restored.Snapshot.ActiveSchedule);
    }
}
