namespace ControlTime.Core.Domain;

public sealed record UsageWarning(
    WarningThreshold Threshold,
    TimeSpan Remaining,
    TimeSpan? EffectiveDailyAllowance);
