namespace SubZeroFramework.Models;

public sealed record FrameworkDesktopCoolingDetails : FrameworkCoolingDetails
{
    public required string Platform { get; init; }

    public ImmutableArray<FrameworkDesktopFanOption> SupportedFanOptions { get; init; } = [];
}
