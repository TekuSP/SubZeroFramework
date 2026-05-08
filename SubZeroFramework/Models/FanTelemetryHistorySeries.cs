using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record FanTelemetryHistorySeries
{
    public required int FanIndex { get; init; }

    public ImmutableArray<FanTelemetrySeriesPoint> Points { get; init; } = [];
}
