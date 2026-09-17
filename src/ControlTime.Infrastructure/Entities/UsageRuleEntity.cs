namespace ControlTime.Infrastructure.Entities;

public class UsageRuleEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string AllowedDays { get; set; } = string.Empty;

    public TimeOnly ScheduleStart { get; set; }

    public TimeOnly ScheduleEnd { get; set; }

    public long? DailyLimitTicks { get; set; }
}
