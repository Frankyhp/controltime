using ControlTime.Core.Domain;

namespace ControlTime.Core.Contracts;

public enum IpcRequestKind
{
    GetStatus,
    ReportActivity,
    EnterAdminMode,
    ExitAdminMode,
    VerifyPassword,
    SetPassword,
    ChangePassword,
    HasPassword,
}

public sealed record ActivityReportPayload(
    ActivityState State,
    DateTimeOffset LastInputAt,
    DateTimeOffset Timestamp);

public sealed record IpcRequest(
    IpcRequestKind Kind,
    string? Password = null,
    string? NewPassword = null,
    ActivityReportPayload? Activity = null);

public sealed record IpcResponse(
    bool Success,
    ServiceStatus? Status = null,
    int? ErrorCode = null);

public sealed record ServiceStatus(
    ActivityState Activity,
    DateTimeOffset ActivityLastInputAt,
    DateTimeOffset ActivityTimestamp,
    AccessState Access,
    string? Reason,
    TimeSpan? RemainingTime,
    DateTimeOffset? NextAllowedAt,
    TimeSpan? ConfiguredDailyLimit,
    TimeSpan BonusToday,
    TimeOnly? ScheduleStart,
    TimeOnly? ScheduleEnd,
    bool HasEnabledRules,
    TimeSpan UsageToday,
    bool IsAdminMode,
    bool HasPassword,
    bool EnforcementEnabled,
    bool UsingReportedActivity,
    DateTimeOffset Timestamp);
