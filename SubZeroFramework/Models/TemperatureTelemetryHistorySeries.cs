using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record TemperatureTelemetryHistorySeries
{
    public required int SensorIndex { get; init; }

    public required ImmutableArray<TelemetryPoint> Points { get; init; }
}
