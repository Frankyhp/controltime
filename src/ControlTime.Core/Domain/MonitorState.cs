namespace ControlTime.Core.Domain;

public sealed record MonitorState(ActivityStatus Activity, DashboardSnapshot Snapshot);
