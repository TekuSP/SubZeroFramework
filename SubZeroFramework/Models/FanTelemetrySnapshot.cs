using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record FanTelemetrySnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Platform role of the fan slot (e.g. LeftFan, ApuFan); null when not identified.</summary>
    public FrameworkFanName? FanName { get; init; }

    public required string UnitSymbol { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required double SpeedRpm { get; init; }

    public required bool IsAvailable { get; init; }
}
