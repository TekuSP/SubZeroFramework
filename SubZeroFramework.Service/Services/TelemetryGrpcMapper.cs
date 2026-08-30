using DynamicData;

using FrameworkDotnet.Enums;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.GrpcContracts.Mapping;
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

    public static TelemetryChannelIdReply MapChannelId(TelemetryChannelId channelId) =>
        TelemetryWireMapper.MapChannelId(channelId);

    public static TelemetryChannelChangeReply MapChannelChange(Change<TelemetryChannel, TelemetryChannelId> change)
    {
        var reply = new TelemetryChannelChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            ChannelId = MapChannelId(change.Key),
            DisplayName = change.Current.DisplayName,
            UnitSymbol = change.Current.UnitSymbol ?? string.Empty,
            FirstObservedAtUnixTimeMilliseconds = change.Current.FirstObservedAt.ToUnixTimeMilliseconds(),
            LastObservedAtUnixTimeMilliseconds = change.Current.LastObservedAt.ToUnixTimeMilliseconds(),
            IsAvailable = change.Current.IsAvailable,
        };

        if (change.Current.FirmwareThresholds is { } thresholds)
        {
            var message = new GrpcContracts.FirmwareThermalThresholds();
            if (thresholds.WarnCelsius is double warn)
            {
                message.WarnCelsius = warn;
            }

            if (thresholds.HighCelsius is double high)
            {
                message.HighCelsius = high;
            }

            if (thresholds.HaltCelsius is double halt)
            {
                message.HaltCelsius = halt;
            }

            if (thresholds.FanOffCelsius is double fanOff)
            {
                message.FanOffCelsius = fanOff;
            }

            if (thresholds.FanMaxCelsius is double fanMax)
            {
                message.FanMaxCelsius = fanMax;
            }

            reply.FirmwareThresholds = message;
        }

        return reply;
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

    /// <summary>Maps what a fan cools onto the wire.</summary>
    public static FanCoolingRoleValue MapCoolingRole(FanCoolingRole role) => role switch
    {
        FanCoolingRole.Cpu => FanCoolingRoleValue.Cpu,
        FanCoolingRole.Gpu => FanCoolingRoleValue.Gpu,
        FanCoolingRole.System => FanCoolingRoleValue.System,
        _ => FanCoolingRoleValue.Unknown,
    };

    public static FanControlStateChangeReply MapFanControlStateChange(Change<FanControlStateSnapshot, int> change)
    {
        var reply = new FanControlStateChangeReply
        {
            ChangeKind = MapChangeReason(change.Reason),
            FanIndex = change.Key,
            DisplayName = change.Current.DisplayName,
            CoolingRole = MapCoolingRole(change.Current.CoolingRole),
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
        reply.TreatMissingSensorsAsZero = change.Current.TreatMissingSensorsAsZero;
        foreach (var profile in change.Current.CurveProfiles)
        {
            reply.CurveProfiles.Add(MapCurveProfile(change.Key, profile));
        }

        MapAdaptive(change.Current, reply);

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
            TreatMissingSensorsAsZero = profile.TreatMissingSensorsAsZero,
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
        var reply = new CurrentTelemetryValueChangeReply
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

            HasComputePowerWatts = change.Current.ComputePowerWatts is not null,
            ComputePowerWatts = change.Current.ComputePowerWatts ?? 0d,
            HasComputeTemperatureCelsius = change.Current.ComputeTemperatureCelsius is not null,
            ComputeTemperatureCelsius = change.Current.ComputeTemperatureCelsius ?? 0d,
            HasComputeCoreClockMegahertz = change.Current.ComputeCoreClockMegahertz is not null,
            ComputeCoreClockMegahertz = change.Current.ComputeCoreClockMegahertz ?? 0d,
            HasComputeMaxCoreClockMegahertz = change.Current.ComputeMaxCoreClockMegahertz is not null,
            ComputeMaxCoreClockMegahertz = change.Current.ComputeMaxCoreClockMegahertz ?? 0d,
            // Sent as the raw flags value. "Nothing is throttling" is a zero WITH the has_ bit set, which is a
            // different statement from the source being unable to answer.
            HasComputeThrottleReasons = change.Current.ComputeThrottleReasons is not null,
            ComputeThrottleReasons = (uint)(change.Current.ComputeThrottleReasons ?? 0),
            HasComputeVramUsedBytes = change.Current.ComputeVramUsedBytes is not null,
            ComputeVramUsedBytes = change.Current.ComputeVramUsedBytes ?? 0d,
            HasComputeVramTotalBytes = change.Current.ComputeVramTotalBytes is not null,
            ComputeVramTotalBytes = change.Current.ComputeVramTotalBytes ?? 0d,

            IsAvailable = change.Current.IsAvailable,
        };

        // Assigned separately because the generated setter for a proto3 `optional double` takes a plain
        // double: leaving the field unset is how absence is expressed, not writing a null into it.
        if (change.Current.FirmwareWarnCelsius is double firmwareWarn)
        {
            reply.FirmwareWarnCelsius = firmwareWarn;
        }

        return reply;
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

    // The telemetry enum translation lives in SubZeroFramework.GrpcContracts.Mapping.TelemetryWireMapper so
    // the service and the app share ONE implementation — see that type for why. These forwarders keep the
    // call sites (and the tests that pin the wire contract) reading naturally.

    public static bool TryParseChannelId(TelemetryChannelIdReply reply, out TelemetryChannelId channelId) =>
        TelemetryWireMapper.TryParseChannelId(reply, out channelId);

    public static bool TryParseTelemetryArea(TelemetryAreaValue value, out TelemetryArea area) =>
        TelemetryWireMapper.TryParseTelemetryArea(value, out area);

    public static bool TryParseTelemetryEntityKind(TelemetryEntityKindValue value, out TelemetryEntityKind entityKind) =>
        TelemetryWireMapper.TryParseTelemetryEntityKind(value, out entityKind);

    public static bool TryParseTelemetryMetric(TelemetryMetricValue value, out TelemetryMetric metric) =>
        TelemetryWireMapper.TryParseTelemetryMetric(value, out metric);

    public static bool TryParseFanControlMode(FanControlModeValue value, out FanControlMode mode)
    {
        mode = value switch
        {
            FanControlModeValue.Auto => FanControlMode.Auto,
            FanControlModeValue.Manual => FanControlMode.Manual,
            FanControlModeValue.CustomCurve => FanControlMode.CustomCurve,
            FanControlModeValue.Adaptive => FanControlMode.Adaptive,
            FanControlModeValue.Max => FanControlMode.Max,
            _ => default,
        };

        return value is not FanControlModeValue.Unspecified;
    }

    /// <summary>
    /// Projects a fan's Adaptive state onto its control-state reply.
    /// </summary>
    /// <remarks>
    /// Calibration and settings are CONFIGURATION and are sent whenever present. The controller readout is
    /// LIVE and is sent only while the fan is actually adaptively driven, so a client can tell "Adaptive is
    /// configured" from "Adaptive is running" without a second field to keep in sync.
    /// </remarks>
    /// <summary>
    /// Maps a calibration state onto the wire.
    /// </summary>
    /// <remarks>
    /// <see cref="FanCalibrationState.Bootstrap"/> is mapped explicitly rather than left to a fallback. It
    /// used to fall through to <c>None</c>, which told every client that a fan running happily on the
    /// conservative built-in model had no model at all — the one state the Adaptive UI most needs to name.
    /// </remarks>
    private static FanCalibrationStateValue MapCalibrationState(FanCalibrationState state) => state switch
    {
        FanCalibrationState.Ok => FanCalibrationStateValue.Ok,
        FanCalibrationState.Stale => FanCalibrationStateValue.Stale,
        FanCalibrationState.Bootstrap => FanCalibrationStateValue.Bootstrap,
        _ => FanCalibrationStateValue.None,
    };

    /// <summary>Maps a calibration snapshot onto the wire message the progress stream and fan state share.</summary>
    public static FanCalibrationMessage MapCalibration(FanCalibrationSnapshot calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        var message = new FanCalibrationMessage
        {
            State = MapCalibrationState(calibration.State),
            CalibratedAtUnixTimeMilliseconds = calibration.CalibratedAt?.ToUnixTimeMilliseconds() ?? 0L,
            ProcessGainCelsiusPerPercent = calibration.ProcessGainCelsiusPerPercent,
            TimeConstantSeconds = calibration.TimeConstantSeconds,
            DeadTimeSeconds = calibration.DeadTimeSeconds,
            MinimumSpinRpm = calibration.MinimumSpinRpm,
            MinimumSpinDutyPercent = calibration.MinimumSpinDutyPercent,
            MaximumRpm = calibration.MaximumRpm,
            ProportionalGain = calibration.ProportionalGain,
            IntegralGain = calibration.IntegralGain,
            FeedForwardDutyPerWatt = calibration.FeedForwardDutyPerWatt,
        };

        // Only sent when something was actually measured. An empty message would tell the UI a speed
        // comparison exists and then show it two blanks.
        if (calibration.PerformanceResponse.HasMeasurement)
        {
            message.PerformanceResponse = MapPerformanceResponse(calibration.PerformanceResponse);
        }

        // The measured curve never reached the app at all, so the UI fact describing it could never render.
        foreach (var point in calibration.GainCurve.Points)
        {
            message.GainCurvePoints.Add(new FanGainCurvePointMessage
            {
                DutyPercent = point.DutyPercent,
                SettledCelsius = point.SettledCelsius,
            });
        }

        return message;
    }

    private static FanPerformanceResponseMessage MapPerformanceResponse(FanPerformanceResponse response)
    {
        var message = new FanPerformanceResponseMessage
        {
            LowDutyPercent = response.LowDutyPercent,
            FullDutyPercent = response.FullDutyPercent,
        };

        if (response.CpuPerformanceRatioAtLowDuty is double cpuLow)
        {
            message.CpuPerformanceRatioAtLowDuty = cpuLow;
        }

        if (response.CpuPerformanceRatioAtFullDuty is double cpuFull)
        {
            message.CpuPerformanceRatioAtFullDuty = cpuFull;
        }

        if (response.GpuCoreClockAtLowDutyMegahertz is double gpuLow)
        {
            message.GpuCoreClockAtLowDutyMegahertz = gpuLow;
        }

        if (response.GpuCoreClockAtFullDutyMegahertz is double gpuFull)
        {
            message.GpuCoreClockAtFullDutyMegahertz = gpuFull;
        }

        return message;
    }

    private static void MapAdaptive(FanControlStateSnapshot state, FanControlStateChangeReply reply)
    {
        if (state.Calibration.State != FanCalibrationState.None)
        {
            reply.Calibration = MapCalibration(state.Calibration);
        }

        reply.AdaptiveSettings = new AdaptiveFanSettingsMessage
        {
            TargetTemperatureCelsius = state.AdaptiveSettings.TargetTemperatureCelsius,
            SafetyFloorEnabled = state.AdaptiveSettings.SafetyFloorEnabled,
            SafetyFloorPercent = state.AdaptiveSettings.SafetyFloorPercent,
            LambdaSeconds = state.AdaptiveSettings.LambdaSeconds,
        };

        if (state.AdaptiveLearning.HasLearned)
        {
            var learning = new AdaptiveLearningMessage
            {
                ObservationCount = state.AdaptiveLearning.ObservationCount,
                LastUpdatedAtUnixTimeMilliseconds = state.AdaptiveLearning.LastUpdatedAt?.ToUnixTimeMilliseconds() ?? 0L,
                LastMaterialChangeAtUnixTimeMilliseconds = state.AdaptiveLearning.LastMaterialChangeAt?.ToUnixTimeMilliseconds() ?? 0L,

                // Derived here rather than on each client so every surface agrees on the wording, and so the
                // thresholds live next to the model they describe.
                Confidence = state.AdaptiveLearning.ConfidenceAt(DateTimeOffset.UtcNow) switch
                {
                    AdaptiveConfidence.Confident => AdaptiveConfidenceValue.Confident,
                    AdaptiveConfidence.Converging => AdaptiveConfidenceValue.Converging,
                    _ => AdaptiveConfidenceValue.Learning,
                },
            };

            foreach (var sample in state.AdaptiveLearning.GainHistory)
            {
                learning.GainHistory.Add(new AdaptiveGainSampleMessage
                {
                    AtUnixTimeMilliseconds = sample.At.ToUnixTimeMilliseconds(),
                    ProcessGainCelsiusPerPercent = sample.ProcessGainCelsiusPerPercent,
                });
            }

            if (state.AdaptiveLearning.IdentifiedProcessGainCelsiusPerPercent is double identifiedGain)
            {
                learning.IdentifiedProcessGainCelsiusPerPercent = identifiedGain;
            }

            if (state.AdaptiveLearning.IdentifiedCelsiusPerWatt is double identifiedResistance)
            {
                learning.IdentifiedCelsiusPerWatt = identifiedResistance;
            }

            if (state.AdaptiveLearning.FeedForwardDutyPerWatt is double learnedGain)
            {
                learning.FeedForwardDutyPerWatt = learnedGain;
            }

            if (state.AdaptiveLearning.CalibratedAnchorDutyPerWatt is double anchor)
            {
                learning.CalibratedAnchorDutyPerWatt = anchor;
            }

            reply.AdaptiveLearning = learning;
        }

        if (state.AdaptiveControl is not { } control)
        {
            return;
        }

        var message = new AdaptiveControlMessage
        {
            FeedForwardDutyPercent = control.FeedForwardDutyPercent,
            ProportionalIntegralDutyPercent = control.ProportionalIntegralDutyPercent,
            LeadDutyPercent = control.LeadDutyPercent,
            ThrottleEscalationDutyPercent = control.ThrottleEscalationDutyPercent,
            RawDutyPercent = control.RawDutyPercent,
            DutyPercent = control.DutyPercent,
            DrivingTemperatureCelsius = control.DrivingTemperatureCelsius,
            TargetTemperatureCelsius = control.TargetTemperatureCelsius,
            IsThrottleLatched = control.IsThrottleLatched,
            ThrottleLatchedAtUnixTimeMilliseconds = control.ThrottleLatchedAt?.ToUnixTimeMilliseconds() ?? 0L,
            IsFeedForwardUnavailable = control.IsFeedForwardUnavailable,
        };

        if (control.ExpectedRpm is double expectedRpm)
        {
            message.ExpectedRpm = expectedRpm;
        }

        if (control.ThrottleLatchReleaseSeconds is double releaseSeconds)
        {
            message.ThrottleLatchReleaseSeconds = releaseSeconds;
        }

        reply.AdaptiveControl = message;
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

    private static TelemetryAreaValue MapTelemetryArea(TelemetryArea area) =>
        TelemetryWireMapper.MapTelemetryArea(area);

    private static TelemetryEntityKindValue MapTelemetryEntityKind(TelemetryEntityKind entityKind) =>
        TelemetryWireMapper.MapTelemetryEntityKind(entityKind);

    private static TelemetryMetricValue MapTelemetryMetric(TelemetryMetric metric) =>
        TelemetryWireMapper.MapTelemetryMetric(metric);

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
            FanControlMode.Adaptive => FanControlModeValue.Adaptive,
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
