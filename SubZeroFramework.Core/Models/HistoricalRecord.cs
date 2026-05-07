namespace SubZeroFramework.Models;

public sealed record HistoricalRecord<T>(
    long SampleId,
    DateTimeOffset ObservedAt,
    T Value);
