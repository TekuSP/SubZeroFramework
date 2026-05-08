namespace SubZeroFramework.Services;

internal readonly record struct TelemetrySeriesStreamKey(
    TelemetryChannelId ChannelId,
    TimeSpan HistoryWindow);
