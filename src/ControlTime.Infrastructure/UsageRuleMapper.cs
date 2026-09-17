using ControlTime.Core.Domain;
using ControlTime.Infrastructure.Entities;

namespace ControlTime.Infrastructure;

internal static class UsageRuleMapper
{
    public static UsageRuleEntity ToEntity(UsageRule rule)
    {
        return new UsageRuleEntity
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            AllowedDays = SerializeDays(rule.AllowedDays),
            ScheduleStart = rule.Schedule.Start,
            ScheduleEnd = rule.Schedule.End,
            DailyLimitTicks = rule.DailyLimit?.Ticks,
        };
    }

    public static UsageRule ToDomain(UsageRuleEntity entity)
    {
        var scheduleResult = AllowedSchedule.TryCreate(
            entity.ScheduleStart,
            entity.ScheduleEnd,
            out var schedule);

        if (!scheduleResult.Success || schedule is null)
        {
            throw new InvalidOperationException(
                $"Persisted rule '{entity.Name}' has an invalid schedule: {scheduleResult.Error}");
        }

        var ruleResult = UsageRule.TryCreate(
            entity.Id,
            entity.Name,
            entity.Enabled,
            DeserializeDays(entity.AllowedDays),
            schedule,
            entity.DailyLimitTicks is long ticks ? TimeSpan.FromTicks(ticks) : null,
            out var rule);

        if (!ruleResult.Success || rule is null)
        {
            throw new InvalidOperationException(
                $"Persisted rule '{entity.Name}' is invalid: {ruleResult.Error}");
        }

        return rule;
    }

    private static string SerializeDays(IReadOnlySet<DayOfWeek> days)
    {
        var values = days
            .Select(d => (int)d)
            .OrderBy(v => v)
            .Select(v => v.ToString());

        return string.Join(',', values);
    }

    private static IReadOnlySet<DayOfWeek> DeserializeDays(string allowedDays)
    {
        if (string.IsNullOrWhiteSpace(allowedDays))
        {
            return new HashSet<DayOfWeek>();
        }

        return allowedDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .Select(v => (DayOfWeek)v)
            .ToHashSet();
    }
}
