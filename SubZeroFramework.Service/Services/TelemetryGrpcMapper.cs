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
        var reply = new FanCapabilityChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            FanIndex = change.Key,
            DisplayName = change.Current.DisplayName,
            Features = (uint)change.Current.Features,
            SupportsFanControl = change.Current.SupportsFanControl,
            SupportsThermalReporting = change.Current.SupportsThermalReporting,
            MaximumSpeedRpm = change.Current.MaximumSpeedRpm,
            ObservedAtUnixTimeMilliseconds = change.Current.ObservedAt.ToUnixTimeMilliseconds(),
            IsAvailable = change.Current.IsAvailable,
        };

        if (change.Current.CoolingDetails is { } coolingDetails)
        {
            reply.CoolingDetails = MapCoolingDetails(coolingDetails);
        }

        return reply;
    }

    private static FrameworkCoolingDetailsReply MapCoolingDetails(FrameworkCoolingDetails coolingDetails)
    {
        return coolingDetails switch
        {
            FrameworkLaptop12CoolingDetails details => new FrameworkCoolingDetailsReply
            {
                FrameworkLaptop12 = new FrameworkLaptop12CoolingDetailsReply
                {
                    ProcessorSupport = details.ProcessorSupport,
                    ThermalCapacity = details.ThermalCapacity,
                    HeatPipeConfiguration = details.HeatPipeConfiguration,
                    FanDimensions = MapCoolingFanDimensions(details.FanDimensions),
                    ThermalInterfaceMaterial = details.ThermalInterfaceMaterial,
                    FirmwareOperatingRangeRpm = MapFanSpeedRange(details.FirmwareOperatingRangeRpm),
                    MaximumPhysicalLimitRpm = details.MaximumPhysicalLimitRpm,
                },
            },
            FrameworkLaptop13CoolingDetails details => new FrameworkCoolingDetailsReply
            {
                FrameworkLaptop13 = new FrameworkLaptop13CoolingDetailsReply
                {
                    ProcessorSupport = details.ProcessorSupport,
                    ChassisMaterial = details.ChassisMaterial,
                    ApproximateFirmwareIdleSpeedRpm = details.ApproximateFirmwareIdleSpeedRpm,
                    ApproximateUserTunedIdleSpeedRpm = details.ApproximateUserTunedIdleSpeedRpm,
                    MaximumFirmwareLimitRpm = details.MaximumFirmwareLimitRpm,
                    ApproximatePhysicalMaximumRpm = details.ApproximatePhysicalMaximumRpm,
                },
            },
            FrameworkLaptop16CoolingDetails details => new FrameworkCoolingDetailsReply
            {
                FrameworkLaptop16 = new FrameworkLaptop16CoolingDetailsReply
                {
                    ProcessorSupport = details.ProcessorSupport,
                    PrimaryCpuThermalInterfaceMaterial = details.PrimaryCpuThermalInterfaceMaterial,
                    ShellFanDimensions = MapCoolingFanDimensions(details.ShellFanDimensions),
                    GraphicsFanDimensions = MapCoolingFanDimensions(details.GraphicsFanDimensions),
                    ExpansionBayPowerLimitWatts = details.ExpansionBayPowerLimitWatts,
                    StandardFirmwareMaximumRpm = details.StandardFirmwareMaximumRpm,
                    ApproximateThermalStressMaximumRpm = details.ApproximateThermalStressMaximumRpm,
                },
            },
            FrameworkDesktopCoolingDetails details => new FrameworkCoolingDetailsReply
            {
                FrameworkDesktop = new FrameworkDesktopCoolingDetailsReply
                {
                    Platform = details.Platform,
                    SupportedFanOptions = { details.SupportedFanOptions.Select(MapDesktopFanOption) },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(coolingDetails), coolingDetails.GetType().FullName, "Unsupported cooling details type."),
        };
    }

    private static CoolingFanDimensionsReply MapCoolingFanDimensions(CoolingFanDimensions dimensions)
    {
        return new CoolingFanDimensionsReply
        {
            WidthMillimeters = dimensions.WidthMillimeters,
            HeightMillimeters = dimensions.HeightMillimeters,
            ThicknessMillimeters = dimensions.ThicknessMillimeters,
            IsCircular = dimensions.IsCircular,
        };
    }

    private static FanSpeedRangeReply MapFanSpeedRange(FanSpeedRange range)
    {
        return new FanSpeedRangeReply
        {
            MinimumRpm = range.MinimumRpm,
            MaximumRpm = range.MaximumRpm,
        };
    }

    private static FrameworkDesktopFanOptionReply MapDesktopFanOption(FrameworkDesktopFanOption option)
    {
        FrameworkDesktopFanOptionReply reply = new()
        {
            ModelName = option.ModelName,
            FanDimensions = MapCoolingFanDimensions(option.FanDimensions),
            ConnectorType = option.ConnectorType,
            MaximumAirflowCfm = option.MaximumAirflowCfm,
            AlternateAirflowDisplay = option.AlternateAirflowDisplay ?? string.Empty,
            AcousticNoiseDisplay = option.AcousticNoiseDisplay,
            MaximumFanSpeedRpm = option.MaximumFanSpeedRpm,
        };

        if (option.AcousticNoiseDecibels is double acousticNoiseDecibels)
        {
            reply.AcousticNoiseDecibels = acousticNoiseDecibels;
        }

        if (option.MaximumAcousticNoiseDecibels is double maximumAcousticNoiseDecibels)
        {
            reply.MaximumAcousticNoiseDecibels = maximumAcousticNoiseDecibels;
        }

        return reply;
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
            HasActiveOverride = change.Current.HasActiveOverride,
            LastAutoRestoreAttemptFailed = change.Current.LastAutoRestoreAttemptFailed,
            DrivingTemperatureAggregation = MapTemperatureAggregationMode(change.Current.DrivingTemperatureAggregation),
        };
        if (change.Current.LastAutoRestoreAttemptAt is not null)
        {
            reply.HasLastAutoRestoreAttempt = true;
            reply.LastAutoRestoreAttemptAtUnixTimeMilliseconds = change.Current.LastAutoRestoreAttemptAt.Value.ToUnixTimeMilliseconds();
        }

        if (!string.IsNullOrWhiteSpace(change.Current.LastAutoRestoreError))
        {
            reply.LastAutoRestoreError = change.Current.LastAutoRestoreError;
        }

        if (change.Current.LastDutyPercent is double lastDutyPercent)
        {
            reply.LastDutyPercent = lastDutyPercent;
        }

        reply.DrivingSensorIndices.AddRange(change.Current.DrivingSensorIndices);
        reply.CustomCurvePoints.AddRange(change.Current.CustomCurvePoints.Select(point => new FanCurvePointReply
        {
            TemperatureCelsius = point.Key,
            FanDutyPercent = point.Value,
        }));

        reply.ActiveCurveSlot = change.Current.ActiveCurveSlot;
        foreach (var profile in change.Current.CurveProfiles)
        {
            reply.CurveProfiles.Add(MapCurveProfile(change.Key, profile));
        }

        if (change.Current.LinkedLeaderIndex is int linkedLeaderIndex)
        {
            reply.LinkedLeaderIndex = linkedLeaderIndex;
        }

        return reply;
    }

    private static FanCurveProfileReply MapCurveProfile(int fanIndex, FanCurveProfileSnapshot profile)
    {
        var reply = new FanCurveProfileReply
        {
            FanIndex = fanIndex,
            Slot = profile.Slot,
            Name = profile.Name ?? string.Empty,
            IsConfigured = profile.IsConfigured,
            Aggregation = MapTemperatureAggregationMode(profile.DrivingTemperatureAggregation),
        };

        reply.DrivingSensorIndices.AddRange(profile.DrivingSensorIndices);
        reply.Points.AddRange(profile.CurvePoints.Select(point => new FanCurvePointReply
        {
            TemperatureCelsius = point.Key,
            FanDutyPercent = point.Value,
        }));

        if (profile.FollowFanIndex is int followFanIndex)
        {
            reply.HasFollowTarget = true;
            reply.FollowFanIndex = followFanIndex;
        }

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
            TemperatureState = MapTemperatureState(change.Current.TemperatureState),
            SensorName = MapSensorName(change.Current.SensorName),
            FanName = MapFanName(change.Current.FanName),
            PowerSourceState = MapPowerSourceState(change.Current.PowerSourceState),
            BatteryState = MapBatteryState(change.Current.BatteryState),
            BatteryManufacturer = change.Current.BatteryManufacturer ?? string.Empty,
            BatteryModelNumber = change.Current.BatteryModelNumber ?? string.Empty,
            BatterySerialNumber = change.Current.BatterySerialNumber ?? string.Empty,
            BatteryType = change.Current.BatteryType ?? string.Empty,
            HasBatteryRemainingCapacityAmpereHours = change.Current.BatteryRemainingCapacityAmpereHours is not null,
            BatteryRemainingCapacityAmpereHours = change.Current.BatteryRemainingCapacityAmpereHours ?? 0d,
            HasBatteryDesignCapacityAmpereHours = change.Current.BatteryDesignCapacityAmpereHours is not null,
            BatteryDesignCapacityAmpereHours = change.Current.BatteryDesignCapacityAmpereHours ?? 0d,
            HasBatteryLastFullChargeCapacityAmpereHours = change.Current.BatteryLastFullChargeCapacityAmpereHours is not null,
            BatteryLastFullChargeCapacityAmpereHours = change.Current.BatteryLastFullChargeCapacityAmpereHours ?? 0d,
            HasBatteryDesignVoltageVolts = change.Current.BatteryDesignVoltageVolts is not null,
            BatteryDesignVoltageVolts = change.Current.BatteryDesignVoltageVolts ?? 0d,
            HasBatteryCycleCount = change.Current.BatteryCycleCount is not null,
            BatteryCycleCount = change.Current.BatteryCycleCount ?? 0u,
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
            FanControlModeValue.Max => FanControlMode.Max,
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

    private static TemperatureStateValue MapTemperatureState(FrameworkTemperatureState? temperatureState)
    {
        return temperatureState switch
        {
            FrameworkTemperatureState.Ok => TemperatureStateValue.Ok,
            FrameworkTemperatureState.NotPresent => TemperatureStateValue.NotPresent,
            FrameworkTemperatureState.Error => TemperatureStateValue.Error,
            FrameworkTemperatureState.NotPowered => TemperatureStateValue.NotPowered,
            FrameworkTemperatureState.NotCalibrated => TemperatureStateValue.NotCalibrated,
            _ => TemperatureStateValue.Unspecified,
        };
    }

    // FD0001 (platform-specific enum members) is intentionally suppressed: we translate whatever fan name the
    // device itself reported, so only the cases valid for the running platform are ever hit; the rest are inert.
#pragma warning disable FD0001
    private static FanNameValue MapFanName(FrameworkFanName? fanName)
    {
        return fanName switch
        {
            FrameworkFanName.Generic => FanNameValue.Generic,
            FrameworkFanName.ApuFan => FanNameValue.ApuFan,
            FrameworkFanName.LeftFan => FanNameValue.LeftFan,
            FrameworkFanName.RightFan => FanNameValue.RightFan,
            FrameworkFanName.FrontFan => FanNameValue.FrontFan,
            FrameworkFanName.ThirdFan => FanNameValue.ThirdFan,
            _ => FanNameValue.Unspecified,
        };
    }
#pragma warning restore FD0001

    private static TemperatureSensorNameValue MapSensorName(FrameworkSensorName? sensorName)
    {
        return sensorName switch
        {
            FrameworkSensorName.Generic => TemperatureSensorNameValue.Generic,
            FrameworkSensorName.F75303Local => TemperatureSensorNameValue.F75303Local,
            FrameworkSensorName.F75303Cpu => TemperatureSensorNameValue.F75303Cpu,
            FrameworkSensorName.F75303Ddr => TemperatureSensorNameValue.F75303Ddr,
            FrameworkSensorName.Battery => TemperatureSensorNameValue.Battery,
            FrameworkSensorName.Peci => TemperatureSensorNameValue.Peci,
            FrameworkSensorName.F57397VccGt => TemperatureSensorNameValue.F57397VccGt,
            FrameworkSensorName.F75303Skin => TemperatureSensorNameValue.F75303Skin,
            FrameworkSensorName.ChargerIc => TemperatureSensorNameValue.ChargerIc,
            FrameworkSensorName.Apu => TemperatureSensorNameValue.Apu,
            FrameworkSensorName.DgpuVr => TemperatureSensorNameValue.DgpuVr,
            FrameworkSensorName.DgpuVram => TemperatureSensorNameValue.DgpuVram,
            FrameworkSensorName.DgpuAmb => TemperatureSensorNameValue.DgpuAmb,
            FrameworkSensorName.DgpuTemp => TemperatureSensorNameValue.DgpuTemp,
            FrameworkSensorName.F75303Apu => TemperatureSensorNameValue.F75303Apu,
            FrameworkSensorName.F75303Amb => TemperatureSensorNameValue.F75303Amb,
            FrameworkSensorName.Virtual => TemperatureSensorNameValue.Virtual,
            _ => TemperatureSensorNameValue.Unspecified,
        };
    }

    private static PowerSourceStateValue MapPowerSourceState(FrameworkPowerSourceState? powerSourceState)
    {
        return powerSourceState switch
        {
            FrameworkPowerSourceState.None => PowerSourceStateValue.None,
            FrameworkPowerSourceState.AcOnly => PowerSourceStateValue.AcOnly,
            FrameworkPowerSourceState.BatteryOnly => PowerSourceStateValue.BatteryOnly,
            FrameworkPowerSourceState.AcAndBattery => PowerSourceStateValue.AcAndBattery,
            _ => PowerSourceStateValue.Unspecified,
        };
    }

    private static BatteryStateValue MapBatteryState(FrameworkBatteryState? batteryState)
    {
        return batteryState switch
        {
            FrameworkBatteryState.NotPresent => BatteryStateValue.NotPresent,
            FrameworkBatteryState.Idle => BatteryStateValue.Idle,
            FrameworkBatteryState.Charging => BatteryStateValue.Charging,
            FrameworkBatteryState.Discharging => BatteryStateValue.Discharging,
            FrameworkBatteryState.ChargingAndDischarging => BatteryStateValue.ChargingAndDischarging,
            FrameworkBatteryState.Critical => BatteryStateValue.Critical,
            _ => BatteryStateValue.Unspecified,
        };
    }

    private static FanControlModeValue MapFanControlMode(FanControlMode mode)
    {
        return mode switch
        {
            FanControlMode.Auto => FanControlModeValue.Auto,
            FanControlMode.Manual => FanControlModeValue.Manual,
            FanControlMode.CustomCurve => FanControlModeValue.CustomCurve,
            FanControlMode.Max => FanControlModeValue.Max,
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
