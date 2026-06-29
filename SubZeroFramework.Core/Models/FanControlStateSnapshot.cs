namespace SubZeroFramework.Models;

public sealed record FanControlStateSnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public required FanControlMode Mode { get; init; }

    public ImmutableSortedDictionary<int, double> CustomCurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; }

    public ImmutableArray<int> DrivingSensorIndices { get; init; } = [];

    /// <summary>Which curve profile slot (0-based) is currently active for this fan.</summary>
    public int ActiveCurveSlot { get; init; }

    /// <summary>The fan's curve profile slots. Fields above reflect the active slot's curve.</summary>
    public ImmutableArray<FanCurveProfileSnapshot> CurveProfiles { get; init; } = [];

    /// <summary>The fan this one is grouped under ("Applies to" link), or null when independent / a leader itself. Persisted by the service so the grouping survives restarts.</summary>
    public int? LinkedLeaderIndex { get; init; }

    public bool HasActiveOverride { get; init; }

    public double? LastDutyPercent { get; init; }

    public bool LastAutoRestoreAttemptFailed { get; init; }

    public DateTimeOffset? LastAutoRestoreAttemptAt { get; init; }

    public string? LastAutoRestoreError { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }
}