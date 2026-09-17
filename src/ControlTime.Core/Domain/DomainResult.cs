namespace ControlTime.Core.Domain;

public readonly record struct DomainResult(bool Success, string? Error)
{
    public static DomainResult Ok { get; } = new(true, null);

    public static DomainResult Invalid(string error) => new(false, error);

    public bool Failed => !Success;
}
