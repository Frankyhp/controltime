namespace ControlTime.Core.Domain;

public sealed class UsageRule
{
    public Guid Id { get; }

    public string Name { get; }

    public bool Enabled { get; }

    public IReadOnlySet<DayOfWeek> AllowedDays { get; }

    public AllowedSchedule Schedule { get; }

    public TimeSpan? DailyLimit { get; }

    private UsageRule(
        Guid id,
        string name,
        bool enabled,
        IReadOnlySet<DayOfWeek> allowedDays,
        AllowedSchedule schedule,
        TimeSpan? dailyLimit)
    {
        Id = id;
        Name = name;
        Enabled = enabled;
        AllowedDays = allowedDays;
        Schedule = schedule;
        DailyLimit = dailyLimit;
    }

    public static DomainResult TryCreate(
        string name,
        bool enabled,
        IReadOnlySet<DayOfWeek> allowedDays,
        AllowedSchedule schedule,
        TimeSpan? dailyLimit,
        out UsageRule? rule)
        => TryCreate(Guid.NewGuid(), name, enabled, allowedDays, schedule, dailyLimit, out rule);

    public static DomainResult TryCreate(
        Guid id,
        string name,
        bool enabled,
        IReadOnlySet<DayOfWeek> allowedDays,
        AllowedSchedule schedule,
        TimeSpan? dailyLimit,
        out UsageRule? rule)
    {
        rule = null;

        ArgumentNullException.ThrowIfNull(allowedDays);
        ArgumentNullException.ThrowIfNull(schedule);

        if (string.IsNullOrWhiteSpace(name))
        {
            return DomainResult.Invalid("Rule name must not be empty.");
        }

        if (allowedDays.Count == 0)
        {
            return DomainResult.Invalid("Rule must allow at least one day of the week.");
        }

        if (dailyLimit is TimeSpan limit && limit <= TimeSpan.Zero)
        {
            return DomainResult.Invalid("Daily limit must be positive when specified.");
        }

        rule = new UsageRule(id, name.Trim(), enabled, allowedDays, schedule, dailyLimit);
        return DomainResult.Ok;
    }
}
