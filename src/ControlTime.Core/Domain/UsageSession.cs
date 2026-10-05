namespace ControlTime.Core.Domain;

public sealed class UsageSession
{
    public Guid Id { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; }

    public bool IsOpen => EndedAt is null;

    public TimeSpan? Duration => EndedAt is DateTimeOffset end
        ? end - StartedAt
        : null;

    private UsageSession(Guid id, DateTimeOffset startedAt, DateTimeOffset? endedAt)
    {
        Id = id;
        StartedAt = startedAt;
        EndedAt = endedAt;
    }

    public static DomainResult TryCreate(
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        out UsageSession? session)
        => TryCreate(Guid.NewGuid(), startedAt, endedAt, out session);

    public static DomainResult TryCreate(
        Guid id,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        out UsageSession? session)
    {
        session = null;

        if (endedAt < startedAt)
        {
            return DomainResult.Invalid("Session end must not be earlier than session start.");
        }

        session = new UsageSession(id, startedAt, endedAt);
        return DomainResult.Ok;
    }

    public static DomainResult TryCreateOpen(DateTimeOffset startedAt, out UsageSession? session)
        => TryCreateOpen(Guid.NewGuid(), startedAt, out session);

    public static DomainResult TryCreateOpen(
        Guid id,
        DateTimeOffset startedAt,
        out UsageSession? session)
    {
        session = new UsageSession(id, startedAt, endedAt: null);
        return DomainResult.Ok;
    }

    public DomainResult TryClose(DateTimeOffset endedAt, out UsageSession? closed)
    {
        closed = null;

        if (!IsOpen)
        {
            return DomainResult.Invalid("Only an open session can be closed.");
        }

        if (endedAt < StartedAt)
        {
            return DomainResult.Invalid("Session end must not be earlier than session start.");
        }

        closed = new UsageSession(Id, StartedAt, endedAt);
        return DomainResult.Ok;
    }
}
