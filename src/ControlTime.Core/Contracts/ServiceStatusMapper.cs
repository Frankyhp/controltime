using ControlTime.Core.Domain;

namespace ControlTime.Core.Contracts;

public static class ServiceStatusMapper
{
    public static ServiceStatus ToServiceStatus(
        this MonitorState state,
        bool isAdminMode,
        bool hasPassword,
        bool enforcementEnabled,
        bool usingReportedActivity)
    {
        ArgumentNullException.ThrowIfNull(state);

        DashboardSnapshot snapshot = state.Snapshot;

        return new ServiceStatus(
            state.Activity.State,
            state.Activity.LastInputAt,
            state.Activity.Timestamp,
            snapshot.State,
            snapshot.Reason,
            snapshot.RemainingTime,
            snapshot.NextAllowedAt,
            snapshot.ConfiguredDailyLimit,
            snapshot.BonusToday,
            snapshot.ActiveSchedule?.Start,
            snapshot.ActiveSchedule?.End,
            snapshot.HasEnabledRules,
            snapshot.UsageToday,
            isAdminMode,
            hasPassword,
            enforcementEnabled,
            usingReportedActivity,
            DateTimeOffset.UtcNow);
    }

    public static MonitorState ToMonitorState(this ServiceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        AllowedSchedule? schedule = null;

        if (status.ScheduleStart is TimeOnly start && status.ScheduleEnd is TimeOnly end)
        {
            AllowedSchedule.TryCreate(start, end, out schedule);
        }

        var activity = new ActivityStatus(
            status.Activity,
            status.ActivityLastInputAt,
            status.ActivityTimestamp);

        var snapshot = new DashboardSnapshot(
            status.Access,
            status.Reason,
            status.RemainingTime,
            status.NextAllowedAt,
            status.ConfiguredDailyLimit,
            status.BonusToday,
            schedule,
            status.HasEnabledRules,
            status.UsageToday);

        return new MonitorState(activity, snapshot);
    }
}
