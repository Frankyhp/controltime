namespace ControlTime.Core.Domain;

public sealed record WarningThreshold(TimeSpan Remaining)
{
    public static readonly WarningThreshold FifteenMinutes = new(TimeSpan.FromMinutes(15));
    public static readonly WarningThreshold FiveMinutes = new(TimeSpan.FromMinutes(5));
    public static readonly WarningThreshold OneMinute = new(TimeSpan.FromMinutes(1));

    public static readonly IReadOnlyList<WarningThreshold> Standard =
    [
        FifteenMinutes,
        FiveMinutes,
        OneMinute,
    ];

    public int Minutes => (int)Remaining.TotalMinutes;
}
