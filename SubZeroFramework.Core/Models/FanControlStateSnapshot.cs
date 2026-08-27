namespace SubZeroFramework.Models;

public sealed record FanControlStateSnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>
    /// What this fan cools, as opposed to where it sits.
    /// </summary>
    /// <remarks>
    /// Decides which component a calibration has to heat: on a Framework 16 the right fan cools the discrete
    /// GPU, and loading the processor for it would heat something it does not cool. Also what lets the UI say
    /// "GPU fan" rather than only "Right fan".
    /// </remarks>
    public FanCoolingRole CoolingRole { get; init; } = FanCoolingRole.Unknown;

    public required FanControlMode Mode { get; init; }

    public ImmutableSortedDictionary<int, double> CustomCurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    public TemperatureAggregationMode DrivingTemperatureAggregation { get; init; }

    public ImmutableArray<int> DrivingSensorIndices { get; init; } = [];

    /// <summary>
    /// Active slot's setting: a driving sensor with no reading counts as 0 °C instead of being skipped.
    /// See <see cref="FanCurveProfileSnapshot.TreatMissingSensorsAsZero"/>.
    /// </summary>
    public bool TreatMissingSensorsAsZero { get; init; }

    /// <summary>Which curve profile slot (0-based) is currently active for this fan.</summary>
    public int ActiveCurveSlot { get; init; }

    /// <summary>The fan's curve profile slots. Fields above reflect the active slot's curve.</summary>
    public ImmutableArray<FanCurveProfileSnapshot> CurveProfiles { get; init; } = [];

    /// <summary>The fan this one is grouped under ("Applies to" link), or null when independent / a leader itself. Persisted by the service so the grouping survives restarts.</summary>
    public int? LinkedLeaderIndex { get; init; }

    /// <summary>The fan's learned thermal model. <see cref="FanCalibrationSnapshot.None"/> when never calibrated.</summary>
    public FanCalibrationSnapshot Calibration { get; init; } = FanCalibrationSnapshot.None;

    /// <summary>The user's Adaptive target and safety floor. Persisted per fan, kept across mode switches.</summary>
    public AdaptiveFanSettings AdaptiveSettings { get; init; } = AdaptiveFanSettings.Default;

    /// <summary>
    /// The most recent adaptive controller tick, or null when this fan is not adaptively driven.
    /// </summary>
    /// <remarks>
    /// Live telemetry, not configuration: it is republished every evaluation and is what the UI's controller
    /// readout binds to. It is deliberately NOT persisted — an integrator value from before a restart
    /// describes a machine in a different thermal state.
    /// </remarks>
    public AdaptiveControlDecision? AdaptiveControl { get; init; }

    /// <summary>
    /// What continuous operation has taught this fan since calibration. Persisted, unlike
    /// <see cref="AdaptiveControl"/> — the learned gain describes the chassis and should survive a restart.
    /// </summary>
    public AdaptiveLearningState AdaptiveLearning { get; init; } = AdaptiveLearningState.None;

    public bool HasActiveOverride { get; init; }

    public double? LastDutyPercent { get; init; }

    public bool LastAutoRestoreAttemptFailed { get; init; }

    public DateTimeOffset? LastAutoRestoreAttemptAt { get; init; }

    public string? LastAutoRestoreError { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }
}