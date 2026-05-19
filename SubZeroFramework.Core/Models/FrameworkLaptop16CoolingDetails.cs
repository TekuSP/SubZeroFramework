namespace SubZeroFramework.Models;

public sealed record FrameworkLaptop16CoolingDetails : FrameworkCoolingDetails
{
    public required string ProcessorSupport { get; init; }

    public required string PrimaryCpuThermalInterfaceMaterial { get; init; }

    public required CoolingFanDimensions ShellFanDimensions { get; init; }

    public required CoolingFanDimensions GraphicsFanDimensions { get; init; }

    public required double ExpansionBayPowerLimitWatts { get; init; }

    public required int StandardFirmwareMaximumRpm { get; init; }

    public required int ApproximateThermalStressMaximumRpm { get; init; }
}
