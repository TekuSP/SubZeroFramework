namespace SubZeroFramework.Models;

public sealed record BatteryTelemetryHistorySeries
{
    public required int BatteryIndex { get; init; }

    public required TelemetryMetric Metric { get; init; }

    public required ImmutableArray<TelemetryPoint> Points { get; init; }
}
