namespace ControlTime.Core.Domain;

public sealed record ActivityStatus(
    ActivityState State,
    DateTimeOffset LastInputAt,
    DateTimeOffset Timestamp);
