using SubZeroFramework.Models;

namespace SubZeroFramework.GrpcContracts.Mapping;

/// <summary>
/// The single translation between the domain enums and their wire values, shared by the service and the app.
/// </summary>
/// <remarks>
/// This exists because the two sides used to each carry their own copy. They drifted: compute telemetry was
/// added to the service's copy and not the app's, so every GPU and NPU reading was silently discarded, and the
/// app's <c>TryParse</c> made it worse by mapping an unrecognised value onto the FIRST enum member instead of
/// rejecting it — turning an unknown channel into a valid <c>(Thermal, TemperatureSensor, TemperatureCelsius)</c>
/// identity that collided with a real sensor and fed GPU percentages into the thermal cache as degrees.
///
/// Two rules keep that from recurring, and both are pinned by tests:
/// <list type="number">
/// <item>Every model value must have a wire value — a missing one is a compile-visible omission here rather
/// than a silent drop at runtime.</item>
/// <item>An unknown wire value is REJECTED. Unknown must never mean "guess the first member", because a
/// plausible-but-wrong identity is far more damaging than a dropped reading.</item>
/// </list>
/// </remarks>
public static class TelemetryWireMapper
{
    public static TelemetryAreaValue MapTelemetryArea(TelemetryArea area) => area switch
    {
        TelemetryArea.Thermal => TelemetryAreaValue.Thermal,
        TelemetryArea.Power => TelemetryAreaValue.Power,
        TelemetryArea.Compute => TelemetryAreaValue.Compute,
        _ => TelemetryAreaValue.Unspecified,
    };

    public static TelemetryEntityKindValue MapTelemetryEntityKind(TelemetryEntityKind entityKind) => entityKind switch
    {
        TelemetryEntityKind.TemperatureSensor => TelemetryEntityKindValue.TemperatureSensor,
        TelemetryEntityKind.Fan => TelemetryEntityKindValue.Fan,
        TelemetryEntityKind.Battery => TelemetryEntityKindValue.Battery,
        TelemetryEntityKind.Gpu => TelemetryEntityKindValue.Gpu,
        TelemetryEntityKind.Npu => TelemetryEntityKindValue.Npu,
        _ => TelemetryEntityKindValue.Unspecified,
    };

    public static TelemetryMetricValue MapTelemetryMetric(TelemetryMetric metric) => metric switch
    {
        TelemetryMetric.TemperatureCelsius => TelemetryMetricValue.TemperatureCelsius,
        TelemetryMetric.FanSpeedRpm => TelemetryMetricValue.FanSpeedRpm,
        TelemetryMetric.BatteryChargePercent => TelemetryMetricValue.BatteryChargePercent,
        TelemetryMetric.BatteryPresentRateAmperes => TelemetryMetricValue.BatteryPresentRateAmperes,
        TelemetryMetric.BatteryPresentVoltageVolts => TelemetryMetricValue.BatteryPresentVoltageVolts,
        TelemetryMetric.UtilizationPercent => TelemetryMetricValue.UtilizationPercent,
        TelemetryMetric.VramUtilizationPercent => TelemetryMetricValue.VramUtilizationPercent,
        _ => TelemetryMetricValue.Unspecified,
    };

    public static ComputeDeviceKindValue MapComputeDeviceKind(ComputeDeviceKind kind) => kind switch
    {
        ComputeDeviceKind.Gpu => ComputeDeviceKindValue.Gpu,
        ComputeDeviceKind.Npu => ComputeDeviceKindValue.Npu,
        _ => ComputeDeviceKindValue.Unspecified,
    };

    public static TelemetryChannelIdReply MapChannelId(TelemetryChannelId channelId) => new()
    {
        Area = MapTelemetryArea(channelId.Area),
        EntityKind = MapTelemetryEntityKind(channelId.EntityKind),
        Index = channelId.Index,
        Metric = MapTelemetryMetric(channelId.Metric),
    };

    public static bool TryParseTelemetryArea(TelemetryAreaValue value, out TelemetryArea area)
    {
        switch (value)
        {
            case TelemetryAreaValue.Thermal: area = TelemetryArea.Thermal; return true;
            case TelemetryAreaValue.Power: area = TelemetryArea.Power; return true;
            case TelemetryAreaValue.Compute: area = TelemetryArea.Compute; return true;
            default: area = default; return false;
        }
    }

    public static bool TryParseTelemetryEntityKind(TelemetryEntityKindValue value, out TelemetryEntityKind entityKind)
    {
        switch (value)
        {
            case TelemetryEntityKindValue.TemperatureSensor: entityKind = TelemetryEntityKind.TemperatureSensor; return true;
            case TelemetryEntityKindValue.Fan: entityKind = TelemetryEntityKind.Fan; return true;
            case TelemetryEntityKindValue.Battery: entityKind = TelemetryEntityKind.Battery; return true;
            case TelemetryEntityKindValue.Gpu: entityKind = TelemetryEntityKind.Gpu; return true;
            case TelemetryEntityKindValue.Npu: entityKind = TelemetryEntityKind.Npu; return true;
            default: entityKind = default; return false;
        }
    }

    public static bool TryParseTelemetryMetric(TelemetryMetricValue value, out TelemetryMetric metric)
    {
        switch (value)
        {
            case TelemetryMetricValue.TemperatureCelsius: metric = TelemetryMetric.TemperatureCelsius; return true;
            case TelemetryMetricValue.FanSpeedRpm: metric = TelemetryMetric.FanSpeedRpm; return true;
            case TelemetryMetricValue.BatteryChargePercent: metric = TelemetryMetric.BatteryChargePercent; return true;
            case TelemetryMetricValue.BatteryPresentRateAmperes: metric = TelemetryMetric.BatteryPresentRateAmperes; return true;
            case TelemetryMetricValue.BatteryPresentVoltageVolts: metric = TelemetryMetric.BatteryPresentVoltageVolts; return true;
            case TelemetryMetricValue.UtilizationPercent: metric = TelemetryMetric.UtilizationPercent; return true;
            case TelemetryMetricValue.VramUtilizationPercent: metric = TelemetryMetric.VramUtilizationPercent; return true;
            default: metric = default; return false;
        }
    }

    public static bool TryParseComputeDeviceKind(ComputeDeviceKindValue value, out ComputeDeviceKind kind)
    {
        switch (value)
        {
            case ComputeDeviceKindValue.Gpu: kind = ComputeDeviceKind.Gpu; return true;
            case ComputeDeviceKindValue.Npu: kind = ComputeDeviceKind.Npu; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>
    /// Rebuilds a channel identity from the wire. False when ANY component is unrecognised — a partially
    /// understood channel is not a channel, and publishing it under a defaulted component is how readings end
    /// up attributed to the wrong device.
    /// </summary>
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
}
