namespace ControlTime.Core.Domain;

public sealed record UsageHistoryEntry(
    DateOnly Date,
    TimeSpan Usage,
    TimeSpan BonusGranted);

public sealed record UsageHistory(
    IReadOnlyList<UsageHistoryEntry> Days,
    TimeSpan CurrentWeekUsage,
    IReadOnlyList<UsageSession> Sessions,
    DateTimeOffset AsOf)
{
    public UsageHistoryEntry? Today => Days.Count > 0 ? Days[0] : null;
}
