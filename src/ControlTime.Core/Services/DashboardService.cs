using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class DashboardService
{
    private readonly IUsageRuleStore _ruleStore;
    private readonly ITimeGrantStore _grantStore;
    private readonly RuleEngine _ruleEngine;
    private readonly TimeProvider _timeProvider;

    public DashboardService(
        IUsageRuleStore ruleStore,
        ITimeGrantStore grantStore,
        RuleEngine ruleEngine,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(ruleStore);
        ArgumentNullException.ThrowIfNull(grantStore);
        ArgumentNullException.ThrowIfNull(ruleEngine);

        _ruleStore = ruleStore;
        _grantStore = grantStore;
        _ruleEngine = ruleEngine;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        IReadOnlyList<UsageRule> rules = await _ruleStore.GetAllAsync(cancellationToken);

        DateOnly today = DateOnly.FromDateTime(now.ToOffset(utcOffset).DateTime);

        IReadOnlyList<TimeGrant> grants = await _grantStore.GetGrantsForDayAsync(
            today,
            utcOffset,
            cancellationToken);

        TimeSpan bonusToday = TimeSpan.FromTicks(grants.Sum(g => g.Amount.Ticks));

        RuleResult result = _ruleEngine.Evaluate(now, TimeSpan.Zero, rules, bonusToday);

        List<UsageRule> enabled = rules.Where(r => r.Enabled).ToList();

        TimeSpan? configuredLimit = null;
        if (enabled.Count > 0 && enabled.All(r => r.DailyLimit is not null))
        {
            configuredLimit = enabled.Max(r => r.DailyLimit!.Value);
        }

        AllowedSchedule? activeSchedule = ResolveActiveSchedule(now, enabled, result.NextAllowedAt);

        return new DashboardSnapshot(
            result.State,
            result.Reason,
            result.RemainingTime,
            result.NextAllowedAt,
            configuredLimit,
            bonusToday,
            activeSchedule,
            enabled.Count > 0);
    }

    private static AllowedSchedule? ResolveActiveSchedule(
        DateTimeOffset now,
        IReadOnlyList<UsageRule> enabled,
        DateTimeOffset? nextAllowedAt)
    {
        TimeOnly moment = TimeOnly.FromDateTime(now.DateTime);

        AllowedSchedule? current = enabled
            .Where(r => r.AllowedDays.Contains(now.DayOfWeek) && r.Schedule.IsWithin(moment))
            .Select(r => r.Schedule)
            .OrderBy(s => s.Start)
            .FirstOrDefault();

        if (current is not null || nextAllowedAt is null)
        {
            return current;
        }

        return enabled
            .Where(r => r.AllowedDays.Contains(nextAllowedAt.Value.DayOfWeek)
                && r.Schedule.Start.ToTimeSpan() == nextAllowedAt.Value.TimeOfDay)
            .Select(r => r.Schedule)
            .OrderBy(s => s.Start)
            .FirstOrDefault();
    }
}
