using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Models;

public sealed record FanControlStateOptions
{
    public int FanIndex { get; init; }

    public FanControlMode Mode { get; init; } = FanControlMode.Auto;

    public Dictionary<int, double> CustomCurvePoints { get; init; } = [];

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public int[] DrivingSensorIndices { get; init; } = [];
}