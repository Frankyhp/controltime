namespace ControlTime.Core.Domain;

public sealed record RuleResult(
    AccessState State,
    string? Reason = null,
    TimeSpan? RemainingTime = null,
    DateTimeOffset? NextAllowedAt = null);
