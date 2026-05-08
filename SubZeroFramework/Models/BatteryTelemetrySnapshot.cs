namespace SubZeroFramework.Models;

public sealed record BatteryTelemetrySnapshot
{
    public required int BatteryIndex { get; init; }

    public required string DisplayName { get; init; }
    
    public DateTimeOffset ObservedAt { get; init; }

    public double? ChargePercent { get; init; }
    
    public double? Voltage { get; init; }
    
    public double? Amperage { get; init; }

    public bool IsAvailable { get; init; }
}
