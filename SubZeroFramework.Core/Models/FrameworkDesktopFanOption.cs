namespace SubZeroFramework.Models;

public sealed record FrameworkDesktopFanOption
{
    public required string ModelName { get; init; }

    public required CoolingFanDimensions FanDimensions { get; init; }

    public required string ConnectorType { get; init; }

    public required double MaximumAirflowCfm { get; init; }

    public string? AlternateAirflowDisplay { get; init; }

    public required string AcousticNoiseDisplay { get; init; }

    public double? AcousticNoiseDecibels { get; init; }

    public double? MaximumAcousticNoiseDecibels { get; init; }

    public required int MaximumFanSpeedRpm { get; init; }
}
