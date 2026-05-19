namespace SubZeroFramework.Models;

public sealed record FrameworkLaptop13CoolingDetails : FrameworkCoolingDetails
{
    public required string ProcessorSupport { get; init; }

    public required string ChassisMaterial { get; init; }

    public required int ApproximateFirmwareIdleSpeedRpm { get; init; }

    public required int ApproximateUserTunedIdleSpeedRpm { get; init; }

    public required int MaximumFirmwareLimitRpm { get; init; }

    public required int ApproximatePhysicalMaximumRpm { get; init; }
}
