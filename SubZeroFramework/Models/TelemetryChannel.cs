namespace SubZeroFramework.Models;

public sealed partial record TelemetryChannel
{
    public required TelemetryChannelId Id { get; init; }

    public required string DisplayName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset FirstObservedAt { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }

    public bool IsAvailable { get; init; }
}