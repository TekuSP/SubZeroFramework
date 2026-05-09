namespace SubZeroFramework.Models;

public sealed record TelemetryHistorySeries
{
    public required TelemetryChannelId ChannelId { get; init; }

    public ImmutableArray<TelemetryPoint> Points { get; init; } = [];
}
