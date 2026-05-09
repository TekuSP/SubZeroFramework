using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record FanControlStateSnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public required FanControlMode Mode { get; init; }

    public ImmutableSortedDictionary<int, double> CustomCurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; }

    public ImmutableArray<int> DrivingSensorIndices { get; init; } = [];

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }
}