using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Models;

public sealed record FanControlStateOptions
{
    public int FanIndex { get; init; }

    public FanControlMode Mode { get; init; } = FanControlMode.Auto;

    // Legacy single-curve fields. Retained so existing persisted configs keep loading; on read they are
    // migrated into curve profile slot 0 when no CurveProfiles are present.
    public Dictionary<int, double> CustomCurvePoints { get; init; } = [];

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public int[] DrivingSensorIndices { get; init; } = [];

    /// <summary>Which curve profile slot (0-based) is active for this fan.</summary>
    public int ActiveCurveSlot { get; init; }

    /// <summary>Up to five unique curve profile slots for this fan.</summary>
    public FanCurveProfileOptions[] CurveProfiles { get; init; } = [];

    /// <summary>The fan this one is grouped under ("Applies to" link), or null when independent / a leader itself.</summary>
    public int? LinkedLeaderIndex { get; init; }

    /// <summary>The fan's learned thermal model, or null when it has never been calibrated.</summary>
    /// <remarks>
    /// Persisted because a calibration run costs the user several minutes of a deliberately loaded machine.
    /// Losing it on restart would make Adaptive unusable in practice.
    /// </remarks>
    public FanCalibrationOptions? Calibration { get; init; }

    /// <summary>The user's Adaptive target and safety floor, or null when they never configured it.</summary>
    public AdaptiveFanSettingsOptions? AdaptiveSettings { get; init; }

    /// <summary>What continuous operation has refined since calibration, or null when nothing yet.</summary>
    public AdaptiveLearningOptions? AdaptiveLearning { get; init; }
}

/// <summary>Persisted form of <see cref="FanCalibrationSnapshot"/>.</summary>
/// <remarks>
/// A separate record from the snapshot deliberately: the persisted shape is a file format with compatibility
/// obligations, while the snapshot is free to change. Mapping between them is explicit for that reason.
/// </remarks>
public sealed record FanCalibrationOptions
{
    public FanCalibrationState State { get; init; } = FanCalibrationState.None;

    public DateTimeOffset? CalibratedAt { get; init; }

    public double ProcessGainCelsiusPerPercent { get; init; }

    public double TimeConstantSeconds { get; init; }

    public double DeadTimeSeconds { get; init; }

    public double MinimumSpinRpm { get; init; }

    public double MinimumSpinDutyPercent { get; init; }

    public double MaximumRpm { get; init; }

    public double ProportionalGain { get; init; }

    public double IntegralGain { get; init; }

    public double FeedForwardDutyPerWatt { get; init; }

    public FanSpeedTrackingMode TrackingMode { get; init; } = FanSpeedTrackingMode.Duty;
}

/// <summary>Persisted form of <see cref="AdaptiveFanSettings"/>.</summary>
public sealed record AdaptiveFanSettingsOptions
{
    public double TargetTemperatureCelsius { get; init; } = AdaptiveFanSettings.DefaultTargetCelsius;

    public bool SafetyFloorEnabled { get; init; }

    public double SafetyFloorPercent { get; init; }

    /// <summary>λ, the response pace. Defaulted so configs written before it existed keep loading.</summary>
    public double LambdaSeconds { get; init; } = SubZeroFramework.Services.Control.AdaptivePidTuning.DefaultLambdaSeconds;
}

/// <summary>Persisted form of <see cref="AdaptiveLearningState"/>.</summary>
public sealed record AdaptiveLearningOptions
{
    public double? FeedForwardDutyPerWatt { get; init; }

    /// <summary>
    /// The calibrated gain this refinement was learned around; see
    /// <see cref="AdaptiveLearningState.CalibratedAnchorDutyPerWatt"/>. Without it, a restart cannot tell a
    /// resumed refinement from one a recalibration has superseded.
    /// </summary>
    public double? CalibratedAnchorDutyPerWatt { get; init; }

    public int ObservationCount { get; init; }

    public DateTimeOffset? LastUpdatedAt { get; init; }

    /// <summary>The identified plant, so a restart resumes the fit rather than relearning from scratch.</summary>
    public double? IdentifiedProcessGainCelsiusPerPercent { get; init; }

    public double? IdentifiedCelsiusPerWatt { get; init; }

    public double? IdentifiedInterceptCelsius { get; init; }

    /// <summary>When the model last moved materially; drives the reported confidence after a restart.</summary>
    public DateTimeOffset? LastMaterialChangeAt { get; init; }

    /// <summary>
    /// Which power composition this fit was built from.
    /// </summary>
    /// <remarks>
    /// Persisted so a restart resumes the same composition rather than re-running the capability window and
    /// possibly landing elsewhere — which would leave a stored fit being fed samples that mean something
    /// different from the ones that produced it.
    /// </remarks>
    public ThermalLoadSource ThermalLoadSource { get; init; } = ThermalLoadSource.None;
}

public sealed record FanCurveProfileOptions
{
    public int Slot { get; init; }

    public string? Name { get; init; }

    public Dictionary<int, double> CurvePoints { get; init; } = [];

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; } = TemperatureAggregationMode.Maximum;

    public int[] DrivingSensorIndices { get; init; } = [];

    /// <summary>When set, this slot mirrors the active curve of the given fan instead of its own points.</summary>
    public int? FollowFanIndex { get; init; }

    /// <summary>See <see cref="FanCurveProfileSnapshot.TreatMissingSensorsAsZero"/>.</summary>
    public bool TreatMissingSensorsAsZero { get; init; }
}
