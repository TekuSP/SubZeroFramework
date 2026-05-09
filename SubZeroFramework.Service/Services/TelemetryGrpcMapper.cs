using DynamicData;

using FrameworkDotnet.Enums;

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
            Area = MapTelemetryArea(channelId.Area),
            EntityKind = MapTelemetryEntityKind(channelId.EntityKind),
            Index = channelId.Index,
            Metric = MapTelemetryMetric(channelId.Metric),
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

    public static FanCapabilityChangeReply MapFanCapabilityChange(Change<FanCapabilityState, int> change)
    {
        return new FanCapabilityChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            FanIndex = change.Key,
            DisplayName = change.Current.DisplayName,
            Features = (uint)change.Current.Features,
            SupportsFanControl = change.Current.SupportsFanControl,
            SupportsThermalReporting = change.Current.SupportsThermalReporting,
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
            IsAvailable = change.Current.IsAvailable,
        };
    }

    public static FanControlStateChangeReply MapFanControlStateChange(Change<FanControlStateSnapshot, int> change)
    {
        var reply = new FanControlStateChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            FanIndex = change.Key,
            DisplayName = change.Current.DisplayName,
            ControlMode = MapFanControlMode(change.Current.Mode),
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
            IsAvailable = change.Current.IsAvailable,
            DrivingTemperatureAggregation = MapTemperatureAggregationMode(change.Current.DrivingTemperatureAggregation),
        };
        reply.DrivingSensorIndices.AddRange(change.Current.DrivingSensorIndices);
        reply.CustomCurvePoints.AddRange(change.Current.CustomCurvePoints.Select(point => new FanCurvePointReply
        {
            TemperatureCelsius = point.Key,
            FanDutyPercent = point.Value,
        }));
        return reply;
    }

    public static FanStateChangeReply MapFanStateChange(Change<FanStateSnapshot, int> change)
    {
        return new FanStateChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            FanIndex = change.Key,
            DisplayName = change.Current.DisplayName,
            FanState = MapFanState(change.Current.FanState),
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
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

    public static TelemetryChannelChangeBatchReply MapChannelBatch(IReadOnlyList<TelemetryChannelChangeReply> replies)
    {
        var batch = new TelemetryChannelChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    public static FanCapabilityChangeBatchReply MapFanCapabilityBatch(IReadOnlyList<FanCapabilityChangeReply> replies)
    {
        var batch = new FanCapabilityChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    public static FanControlStateChangeBatchReply MapFanControlStateBatch(IReadOnlyList<FanControlStateChangeReply> replies)
    {
        var batch = new FanControlStateChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    public static FanStateChangeBatchReply MapFanStateBatch(IReadOnlyList<FanStateChangeReply> replies)
    {
        var batch = new FanStateChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    public static CurrentTelemetryValueChangeBatchReply MapCurrentValueBatch(IReadOnlyList<CurrentTelemetryValueChangeReply> replies)
    {
        var batch = new CurrentTelemetryValueChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    public static TelemetrySeriesPointChangeBatchReply MapTelemetryPointBatch(IReadOnlyList<TelemetrySeriesPointChangeReply> replies)
    {
        var batch = new TelemetrySeriesPointChangeBatchReply();
        batch.Changes.AddRange(replies);
        return batch;
    }

    public static bool TryParseChannelId(TelemetryChannelIdReply reply, out TelemetryChannelId channelId)
    {
        if (!TryParseTelemetryArea(reply.Area, out var area)
            || !TryParseTelemetryEntityKind(reply.EntityKind, out var entityKind)
            || !TryParseTelemetryMetric(reply.Metric, out var metric))
        {
            channelId = default;
            return false;
        }

        channelId = new TelemetryChannelId(area, entityKind, reply.Index, metric);
        return true;
    }

    public static bool TryParseTelemetryArea(TelemetryAreaValue value, out TelemetryArea area)
    {
        area = value switch
        {
            TelemetryAreaValue.Thermal => TelemetryArea.Thermal,
            TelemetryAreaValue.Power => TelemetryArea.Power,
            _ => default,
        };

        return value is not TelemetryAreaValue.Unspecified;
    }

    public static bool TryParseTelemetryEntityKind(TelemetryEntityKindValue value, out TelemetryEntityKind entityKind)
    {
        entityKind = value switch
        {
            TelemetryEntityKindValue.TemperatureSensor => TelemetryEntityKind.TemperatureSensor,
            TelemetryEntityKindValue.Fan => TelemetryEntityKind.Fan,
            TelemetryEntityKindValue.Battery => TelemetryEntityKind.Battery,
            _ => default,
        };

        return value is not TelemetryEntityKindValue.Unspecified;
    }

    public static bool TryParseTelemetryMetric(TelemetryMetricValue value, out TelemetryMetric metric)
    {
        metric = value switch
        {
            TelemetryMetricValue.TemperatureCelsius => TelemetryMetric.TemperatureCelsius,
            TelemetryMetricValue.FanSpeedRpm => TelemetryMetric.FanSpeedRpm,
            TelemetryMetricValue.BatteryChargePercent => TelemetryMetric.BatteryChargePercent,
            TelemetryMetricValue.BatteryPresentRateAmperes => TelemetryMetric.BatteryPresentRateAmperes,
            TelemetryMetricValue.BatteryPresentVoltageVolts => TelemetryMetric.BatteryPresentVoltageVolts,
            _ => default,
        };

        return value is not TelemetryMetricValue.Unspecified;
    }

    public static bool TryParseFanControlMode(FanControlModeValue value, out FanControlMode mode)
    {
        mode = value switch
        {
            FanControlModeValue.Auto => FanControlMode.Auto,
            FanControlModeValue.Manual => FanControlMode.Manual,
            FanControlModeValue.CustomCurve => FanControlMode.CustomCurve,
            _ => default,
        };

        return value is not FanControlModeValue.Unspecified;
    }

    public static bool TryParseTemperatureAggregationMode(TemperatureAggregationModeValue value, out TemperatureAggregationMode mode)
    {
        mode = value switch
        {
            TemperatureAggregationModeValue.Average => TemperatureAggregationMode.Average,
            TemperatureAggregationModeValue.Median => TemperatureAggregationMode.Median,
            TemperatureAggregationModeValue.Maximum => TemperatureAggregationMode.Maximum,
            TemperatureAggregationModeValue.Minimum => TemperatureAggregationMode.Minimum,
            _ => default,
        };

        return value is not TemperatureAggregationModeValue.Unspecified;
    }

    private static TelemetryAreaValue MapTelemetryArea(TelemetryArea area)
    {
        return area switch
        {
            TelemetryArea.Thermal => TelemetryAreaValue.Thermal,
            TelemetryArea.Power => TelemetryAreaValue.Power,
            _ => TelemetryAreaValue.Unspecified,
        };
    }

    private static TelemetryEntityKindValue MapTelemetryEntityKind(TelemetryEntityKind entityKind)
    {
        return entityKind switch
        {
            TelemetryEntityKind.TemperatureSensor => TelemetryEntityKindValue.TemperatureSensor,
            TelemetryEntityKind.Fan => TelemetryEntityKindValue.Fan,
            TelemetryEntityKind.Battery => TelemetryEntityKindValue.Battery,
            _ => TelemetryEntityKindValue.Unspecified,
        };
    }

    private static TelemetryMetricValue MapTelemetryMetric(TelemetryMetric metric)
    {
        return metric switch
        {
            TelemetryMetric.TemperatureCelsius => TelemetryMetricValue.TemperatureCelsius,
            TelemetryMetric.FanSpeedRpm => TelemetryMetricValue.FanSpeedRpm,
            TelemetryMetric.BatteryChargePercent => TelemetryMetricValue.BatteryChargePercent,
            TelemetryMetric.BatteryPresentRateAmperes => TelemetryMetricValue.BatteryPresentRateAmperes,
            TelemetryMetric.BatteryPresentVoltageVolts => TelemetryMetricValue.BatteryPresentVoltageVolts,
            _ => TelemetryMetricValue.Unspecified,
        };
    }

    private static FanStateValue MapFanState(FrameworkFanState fanState)
    {
        return fanState switch
        {
            FrameworkFanState.Ok => FanStateValue.Ok,
            FrameworkFanState.NotPresent => FanStateValue.NotPresent,
            FrameworkFanState.Stalled => FanStateValue.Stalled,
            _ => FanStateValue.Unspecified,
        };
    }

    private static FanControlModeValue MapFanControlMode(FanControlMode mode)
    {
        return mode switch
        {
            FanControlMode.Auto => FanControlModeValue.Auto,
            FanControlMode.Manual => FanControlModeValue.Manual,
            FanControlMode.CustomCurve => FanControlModeValue.CustomCurve,
            _ => FanControlModeValue.Unspecified,
        };
    }

    private static TemperatureAggregationModeValue MapTemperatureAggregationMode(TemperatureAggregationMode mode)
    {
        return mode switch
        {
            TemperatureAggregationMode.Average => TemperatureAggregationModeValue.Average,
            TemperatureAggregationMode.Median => TemperatureAggregationModeValue.Median,
            TemperatureAggregationMode.Maximum => TemperatureAggregationModeValue.Maximum,
            TemperatureAggregationMode.Minimum => TemperatureAggregationModeValue.Minimum,
            _ => TemperatureAggregationModeValue.Unspecified,
        };
    }
}
