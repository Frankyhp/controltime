using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class DashboardService
{
    private readonly IUsageRuleStore _ruleStore;
    private readonly ITimeGrantStore _grantStore;
    private readonly UsageService _usageService;
    private readonly RuleEngine _ruleEngine;
    private readonly TimeProvider _timeProvider;

    public DashboardService(
        IUsageRuleStore ruleStore,
        ITimeGrantStore grantStore,
        UsageService usageService,
        RuleEngine ruleEngine,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(ruleStore);
        ArgumentNullException.ThrowIfNull(grantStore);
        ArgumentNullException.ThrowIfNull(usageService);
        ArgumentNullException.ThrowIfNull(ruleEngine);

        _ruleStore = ruleStore;
        _grantStore = grantStore;
        _usageService = usageService;
        _ruleEngine = ruleEngine;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        // La evaluación de reglas (día de la semana y franja horaria) debe
        // hacerse en la hora local del usuario, no en UTC.
        DateTimeOffset now = _timeProvider.GetUtcNow().ToOffset(utcOffset);

        IReadOnlyList<UsageRule> rules = await _ruleStore.GetAllAsync(cancellationToken);

        DateOnly today = DateOnly.FromDateTime(now.DateTime);

        IReadOnlyList<TimeGrant> grants = await _grantStore.GetGrantsForDayAsync(
            today,
            utcOffset,
            cancellationToken);

        TimeSpan bonusToday = TimeSpan.FromTicks(grants.Sum(g => g.Amount.Ticks));

        TimeSpan usageToday = await _usageService.GetUsageTodayAsync(utcOffset, cancellationToken);

        RuleResult result = _ruleEngine.Evaluate(now, usageToday, rules, bonusToday);

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
            enabled.Count > 0,
            usageToday);
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
