namespace ControlTime.Core.Domain;

public sealed class AllowedSchedule
{
    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    private AllowedSchedule(TimeOnly start, TimeOnly end)
    {
        Start = start;
        End = end;
    }

    public static DomainResult TryCreate(TimeOnly start, TimeOnly end, out AllowedSchedule? schedule)
    {
        schedule = null;

        if (start >= end)
        {
            return DomainResult.Invalid("Schedule start must be earlier than schedule end. Schedules crossing midnight are not supported.");
        }

        schedule = new AllowedSchedule(start, end);
        return DomainResult.Ok;
    }

    public bool IsWithin(TimeOnly moment) => moment >= Start && moment < End;

    public bool IsBeforeSchedule(TimeOnly moment) => moment < Start;

    public bool IsAfterSchedule(TimeOnly moment) => moment >= End;
}
