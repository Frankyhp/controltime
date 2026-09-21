namespace ControlTime.Core.Domain;

public sealed record DashboardSnapshot(
    AccessState State,
    string? Reason,
    TimeSpan? RemainingTime,
    DateTimeOffset? NextAllowedAt,
    TimeSpan? ConfiguredDailyLimit,
    TimeSpan BonusToday,
    AllowedSchedule? ActiveSchedule,
    bool HasEnabledRules)
{
    public TimeSpan? EffectiveDailyAllowance => ConfiguredDailyLimit is TimeSpan limit
        ? limit + BonusToday
        : null;
}
