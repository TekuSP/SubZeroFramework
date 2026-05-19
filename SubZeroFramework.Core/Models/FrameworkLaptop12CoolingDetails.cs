namespace SubZeroFramework.Models;

public sealed record FrameworkLaptop12CoolingDetails : FrameworkCoolingDetails
{
    public required string ProcessorSupport { get; init; }

    public required string ThermalCapacity { get; init; }

    public required string HeatPipeConfiguration { get; init; }

    public required CoolingFanDimensions FanDimensions { get; init; }

    public required string ThermalInterfaceMaterial { get; init; }

    public required FanSpeedRange FirmwareOperatingRangeRpm { get; init; }

    public required int MaximumPhysicalLimitRpm { get; init; }
}
