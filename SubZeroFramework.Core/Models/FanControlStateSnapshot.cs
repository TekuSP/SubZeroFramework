namespace SubZeroFramework.Models;

public sealed record FanControlStateSnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public required FanControlMode Mode { get; init; }

    public ImmutableSortedDictionary<int, double> CustomCurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; }

    public ImmutableArray<int> DrivingSensorIndices { get; init; } = [];

    public bool HasActiveOverride { get; init; }

    public double? LastDutyPercent { get; init; }

    public bool LastAutoRestoreAttemptFailed { get; init; }

    public DateTimeOffset? LastAutoRestoreAttemptAt { get; init; }

    public string? LastAutoRestoreError { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }
}