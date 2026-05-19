namespace SubZeroFramework.Models;

public sealed record FrameworkCoolingMetadata
{
    public required int MaximumSpeedRpm { get; init; }

    public FrameworkCoolingDetails? CoolingDetails { get; init; }
}
