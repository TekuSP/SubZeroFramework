namespace SubZeroFramework.Models;

public sealed record FanTelemetrySeriesPoint(
    long SampleId,
    int FanIndex,
    DateTimeOffset ObservedAt,
    double SpeedRpm);
