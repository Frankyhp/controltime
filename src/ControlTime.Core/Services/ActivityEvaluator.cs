using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public static class ActivityEvaluator
{
    public static ActivityStatus Evaluate(
        DateTimeOffset now,
        DateTimeOffset lastInputAt,
        bool isLocked,
        TimeSpan idleThreshold)
    {
        if (idleThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleThreshold),
                "The idle threshold must be positive.");
        }

        if (lastInputAt > now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastInputAt),
                "The last input moment cannot be in the future.");
        }

        ActivityState state = isLocked
            ? ActivityState.Locked
            : now - lastInputAt >= idleThreshold
                ? ActivityState.Idle
                : ActivityState.Active;

        return new ActivityStatus(state, lastInputAt, now);
    }
}
