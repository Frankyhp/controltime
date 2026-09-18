using ControlTime.Core.Domain;

namespace ControlTime.App.ViewModels;

public sealed class RuleListItem
{
    public static readonly (DayOfWeek Day, string Label)[] WeekDays =
    [
        (DayOfWeek.Monday, "Lun"),
        (DayOfWeek.Tuesday, "Mar"),
        (DayOfWeek.Wednesday, "Mié"),
        (DayOfWeek.Thursday, "Jue"),
        (DayOfWeek.Friday, "Vie"),
        (DayOfWeek.Saturday, "Sáb"),
        (DayOfWeek.Sunday, "Dom"),
    ];

    public RuleListItem(UsageRule rule)
    {
        Rule = rule;
        Display = BuildDisplay(rule);
    }

    public UsageRule Rule { get; }

    public string Display { get; }

    public override string ToString() => Display;

    private static string BuildDisplay(UsageRule rule)
    {
        var days = string.Join(", ", WeekDays
            .Where(entry => rule.AllowedDays.Contains(entry.Day))
            .Select(entry => entry.Label));

        var schedule = $"{rule.Schedule.Start:HH\\:mm}–{rule.Schedule.End:HH\\:mm}";

        return $"{rule.Name}{(rule.Enabled ? string.Empty : " (desactivada)")} · {days} · {schedule} · {FormatDailyLimit(rule.DailyLimit)}";
    }

    public static string FormatDailyLimit(TimeSpan? limit)
    {
        if (limit is null)
        {
            return "sin límite";
        }

        var totalMinutes = (int)limit.Value.TotalMinutes;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        return (hours, minutes) switch
        {
            (> 0, > 0) => $"{hours} h {minutes} min",
            (> 0, _) => $"{hours} h",
            _ => $"{minutes} min",
        };
    }
}
