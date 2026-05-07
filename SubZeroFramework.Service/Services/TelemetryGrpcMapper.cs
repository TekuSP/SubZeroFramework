using DynamicData;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

internal static class TelemetryGrpcMapper
{
    public static TelemetryChangeKind MapChangeReason(ChangeReason changeReason)
    {
        return changeReason == ChangeReason.Remove
            ? TelemetryChangeKind.Remove
            : TelemetryChangeKind.Upsert;
    }

    public static TelemetryChannelIdReply MapChannelId(TelemetryChannelId channelId)
    {
        return new TelemetryChannelIdReply
        {
            Area = channelId.Area.ToString(),
            EntityKind = channelId.EntityKind.ToString(),
            Index = channelId.Index,
            Metric = channelId.Metric.ToString(),
        };
    }

    public static TelemetryChannelChangeReply MapChannelChange(Change<TelemetryChannel, TelemetryChannelId> change)
    {
        return new TelemetryChannelChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            ChannelId = MapChannelId(change.Key),
            DisplayName = change.Current.DisplayName,
            UnitSymbol = change.Current.UnitSymbol ?? string.Empty,
            FirstObservedAtUnixTimeMilliseconds = change.Current.FirstObservedAt.ToUnixTimeMilliseconds(),
            LastObservedAtUnixTimeMilliseconds = change.Current.LastObservedAt.ToUnixTimeMilliseconds(),
            IsAvailable = change.Current.IsAvailable,
        };
    }

    public static CurrentTelemetryValueChangeReply MapCurrentValueChange(Change<CurrentTelemetryValue, TelemetryChannelId> change)
    {
        return new CurrentTelemetryValueChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            ChannelId = MapChannelId(change.Key),
            DisplayName = change.Current.DisplayName,
            UnitSymbol = change.Current.UnitSymbol ?? string.Empty,
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
            HasNumericValue = change.Current.NumericValue is not null,
            NumericValue = change.Current.NumericValue ?? 0,
            IsAvailable = change.Current.IsAvailable,
        };
    }

    public static TelemetrySeriesPointChangeReply MapTelemetryPointChange(Change<TelemetryPoint, long> change)
    {
        return new TelemetrySeriesPointChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            SampleId = change.Current.SampleId,
            ChannelId = MapChannelId(change.Current.ChannelId),
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
            NumericValue = change.Current.NumericValue,
        };
    }

    public static bool TryParseChannelId(TelemetryChannelIdReply reply, out TelemetryChannelId channelId)
    {
        if (!Enum.TryParse<TelemetryArea>(reply.Area, out var area)
            || !Enum.TryParse<TelemetryEntityKind>(reply.EntityKind, out var entityKind)
            || !Enum.TryParse<TelemetryMetric>(reply.Metric, out var metric))
        {
            channelId = default;
            return false;
        }

        channelId = new TelemetryChannelId(area, entityKind, reply.Index, metric);
        return true;
    }
}
