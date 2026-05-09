using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record TemperatureTelemetrySnapshot
{
    public required int SensorIndex { get; init; }

    public required string DisplayName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public double? TemperatureCelsius { get; init; }

    public FrameworkTemperatureState? TemperatureState { get; init; }

    public bool IsAvailable { get; init; }
}
