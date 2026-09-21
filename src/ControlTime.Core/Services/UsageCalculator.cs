using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public static class UsageCalculator
{
    public static TimeSpan GetUsageForDay(
        IReadOnlyCollection<UsageSession> sessions,
        DateTimeOffset dayStartUtc,
        DateTimeOffset dayEndUtc)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (dayEndUtc <= dayStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(dayEndUtc));
        }

        long ticks = 0;

        foreach (UsageSession session in sessions)
        {
            if (session.EndedAt is not DateTimeOffset endedAt)
            {
                continue;
            }

            DateTimeOffset start = Max(session.StartedAt, dayStartUtc);
            DateTimeOffset end = Min(endedAt, dayEndUtc);

            if (end > start)
            {
                ticks += (end - start).Ticks;
            }
        }

        return TimeSpan.FromTicks(ticks);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
        => left >= right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;
}
