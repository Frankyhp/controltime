namespace ControlTime.Infrastructure.Entities;

public class UsageSessionEntity
{
    public Guid Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }
}
