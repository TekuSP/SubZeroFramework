namespace SubZeroFramework.Models;

public sealed record FanTelemetrySnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public double? SpeedRpm { get; init; }

    public bool IsAvailable { get; init; }
}
