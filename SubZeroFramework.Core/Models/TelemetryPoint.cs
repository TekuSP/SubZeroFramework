namespace SubZeroFramework.Models;

public sealed record TelemetryPoint(
    long SampleId,
    TelemetryChannelId ChannelId,
    DateTimeOffset ObservedAt,
    double NumericValue)
{
    public string ObservedAtDisplay => ObservedAt.LocalDateTime.ToString("T");

    public string NumericValueDisplay => NumericValue.ToString("N1");
}
