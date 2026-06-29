namespace SubZeroFramework.Models;

/// <summary>
/// One per-fan curve profile slot. A fan owns up to five unique slots; <see cref="IsConfigured"/> is
/// false for an empty slot. When <see cref="FollowFanIndex"/> is set, the slot mirrors the active curve
/// of that fan instead of using its own <see cref="CurvePoints"/>.
/// </summary>
public sealed record FanCurveProfileSnapshot
{
    public required int Slot { get; init; }

    public string? Name { get; init; }

    public bool IsConfigured { get; init; }

    public ImmutableSortedDictionary<int, double> CurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; }

    public ImmutableArray<int> DrivingSensorIndices { get; init; } = [];

    public int? FollowFanIndex { get; init; }
}
