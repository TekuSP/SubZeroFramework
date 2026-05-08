namespace SubZeroFramework.Models;

public sealed record FanTelemetrySnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public required string UnitSymbol { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required double SpeedRpm { get; init; }

    public required bool IsAvailable { get; init; }
}
