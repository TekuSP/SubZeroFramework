namespace SubZeroFramework.Models;

public readonly record struct TelemetryChannelId(
    TelemetryArea Area,
    TelemetryEntityKind EntityKind,
    int Index,
    TelemetryMetric Metric);
