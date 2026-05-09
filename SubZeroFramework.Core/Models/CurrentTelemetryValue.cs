using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record CurrentTelemetryValue
{
    public required TelemetryChannelId ChannelId { get; init; }

    public required string DisplayName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public double? NumericValue { get; init; }

    public FrameworkTemperatureState? TemperatureState { get; init; }

    public bool IsAvailable { get; init; }

    public string DisplayValue => NumericValue is double numericValue
        ? UnitSymbol is { Length: > 0 }
            ? $"{numericValue:N1} {UnitSymbol}"
            : $"{numericValue:N1}"
        : "Unavailable";
}
