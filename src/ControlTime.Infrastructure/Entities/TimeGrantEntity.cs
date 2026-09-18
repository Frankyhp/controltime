namespace ControlTime.Infrastructure.Entities;

public class TimeGrantEntity
{
    public Guid Id { get; set; }

    public DateTime GrantedAtUtc { get; set; }

    public int Minutes { get; set; }
}
