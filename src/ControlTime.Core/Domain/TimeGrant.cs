namespace ControlTime.Core.Domain;

public sealed class TimeGrant
{
    public Guid Id { get; }

    public DateTimeOffset GrantedAt { get; }

    public int Minutes { get; }

    public TimeSpan Amount => TimeSpan.FromMinutes(Minutes);

    private TimeGrant(Guid id, DateTimeOffset grantedAt, int minutes)
    {
        Id = id;
        GrantedAt = grantedAt;
        Minutes = minutes;
    }

    public static DomainResult TryCreate(
        DateTimeOffset grantedAt,
        int minutes,
        out TimeGrant? grant)
        => TryCreate(Guid.NewGuid(), grantedAt, minutes, out grant);

    public static DomainResult TryCreate(
        Guid id,
        DateTimeOffset grantedAt,
        int minutes,
        out TimeGrant? grant)
    {
        grant = null;

        if (minutes <= 0)
        {
            return DomainResult.Invalid("Grant minutes must be positive.");
        }

        grant = new TimeGrant(id, grantedAt, minutes);
        return DomainResult.Ok;
    }
}
