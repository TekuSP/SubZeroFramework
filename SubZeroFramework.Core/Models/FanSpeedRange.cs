namespace SubZeroFramework.Models;

public sealed record FanSpeedRange
{
    public required int MinimumRpm { get; init; }

    public required int MaximumRpm { get; init; }
}
