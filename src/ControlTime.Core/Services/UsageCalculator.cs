using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public static class UsageCalculator
{
    public static TimeSpan GetUsageForDay(
        IReadOnlyCollection<UsageSession> sessions,
        DateTimeOffset dayStartUtc,
        DateTimeOffset dayEndUtc,
        DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (dayEndUtc <= dayStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(dayEndUtc));
        }

        long ticks = 0;

        foreach (UsageSession session in sessions)
        {
            DateTimeOffset endedAt;
            if (session.EndedAt is DateTimeOffset closed)
            {
                endedAt = closed;
            }
            else if (asOf is DateTimeOffset moment)
            {
                endedAt = moment;
            }
            else
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
