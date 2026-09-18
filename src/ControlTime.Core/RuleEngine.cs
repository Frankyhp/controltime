using ControlTime.Core.Domain;

namespace ControlTime.Core;

public sealed class RuleEngine
{
    public static readonly TimeSpan DefaultWarningThreshold = TimeSpan.FromMinutes(5);

    private const int MaxSearchDays = 7;

    private readonly TimeSpan _warningThreshold;

    public RuleEngine(TimeSpan? warningThreshold = null)
    {
        var threshold = warningThreshold ?? DefaultWarningThreshold;

        if (threshold < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(warningThreshold));
        }

        _warningThreshold = threshold;
    }

    public RuleResult Evaluate(
        DateTimeOffset now,
        TimeSpan usageToday,
        IReadOnlyCollection<UsageRule> rules,
        TimeSpan bonusToday = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (usageToday < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(usageToday));
        }

        if (bonusToday < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bonusToday));
        }

        List<UsageRule> enabled = rules.Where(r => r.Enabled).ToList();

        if (enabled.Count == 0)
        {
            return new RuleResult(AccessState.Blocked, "No usage rule is enabled.");
        }

        TimeOnly moment = TimeOnly.FromDateTime(now.DateTime);

        List<UsageRule> satisfied = [];
        bool anyDayAllowed = false;
        bool limitReachedInWindow = false;

        foreach (UsageRule rule in enabled)
        {
            bool dayAllowed = rule.AllowedDays.Contains(now.DayOfWeek);
            bool insideSchedule = rule.Schedule.IsWithin(moment);

            if (dayAllowed)
            {
                anyDayAllowed = true;
            }

            if (!dayAllowed || !insideSchedule)
            {
                continue;
            }

            if (rule.DailyLimit is TimeSpan limit && usageToday >= limit + bonusToday)
            {
                limitReachedInWindow = true;
                continue;
            }

            satisfied.Add(rule);
        }

        if (satisfied.Count > 0)
        {
            bool unlimited = satisfied.Any(r => r.DailyLimit is null);

            if (unlimited)
            {
                return new RuleResult(AccessState.Allowed);
            }

            TimeSpan remaining = satisfied
                .Select(r => r.DailyLimit!.Value + bonusToday - usageToday)
                .Max();

            AccessState state = remaining <= _warningThreshold
                ? AccessState.Warning
                : AccessState.Allowed;

            return new RuleResult(state, null, remaining);
        }

        string reason;
        bool searchFromTomorrow;

        if (limitReachedInWindow)
        {
            reason = "The daily usage limit has been reached.";
            searchFromTomorrow = true;
        }
        else if (anyDayAllowed)
        {
            reason = "The current time is outside the allowed schedule.";
            searchFromTomorrow = false;
        }
        else
        {
            reason = "Today is not an allowed day.";
            searchFromTomorrow = false;
        }

        return new RuleResult(
            AccessState.Blocked,
            reason,
            null,
            FindNextAllowedStart(now, enabled, searchFromTomorrow));
    }

    private static DateTimeOffset? FindNextAllowedStart(
        DateTimeOffset now,
        IReadOnlyCollection<UsageRule> rules,
        bool startTomorrow)
    {
        DateTime firstDay = now.Date;

        if (startTomorrow)
        {
            firstDay = firstDay.AddDays(1);
        }

        for (int offset = 0; offset <= MaxSearchDays; offset++)
        {
            DateTime day = firstDay.AddDays(offset);

            DateTimeOffset? best = null;

            foreach (UsageRule rule in rules)
            {
                if (!rule.AllowedDays.Contains(day.DayOfWeek))
                {
                    continue;
                }

                DateTimeOffset candidate = new(
                    day.Add(rule.Schedule.Start.ToTimeSpan()),
                    now.Offset);

                if (candidate > now && (best is null || candidate < best))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }
}
