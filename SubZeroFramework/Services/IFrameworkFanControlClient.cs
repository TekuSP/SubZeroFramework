using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Services;

/// <summary>
/// Provides local gRPC fan-control commands through the service boundary.
/// </summary>
public interface IFrameworkFanControlClient
{
    /// <summary>
    /// Sets the fan speed target in RPM.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="targetSpeedRpm">The requested fan speed in RPM.</param>
    Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the fan duty cycle percent.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="dutyPercent">The requested duty cycle percent.</param>
    /// <param name="preview">When true, actuate the EC live without persisting the override (a volatile preview).</param>
    Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, bool preview = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces the fan to 100% duty (Max mode).
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="preview">When true, actuate the EC live without persisting the override (a volatile preview).</param>
    Task<FrameworkFanMaxCommandResult> SetFanMaxAsync(int fanIndex, bool preview = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a custom fan curve for the specified fan and records the driving sensors and aggregation mode in the service-side state store.
    /// </summary>
    /// <param name="preview">When true, actuate the EC live without persisting the curve (a volatile preview).</param>
    Task<FrameworkFanCustomCurveCommandResult> SetCustomCurveAsync(
        int fanIndex,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregationMode,
        bool preview = false,
        bool treatMissingSensorsAsZero = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores automatic fan control for the specified fan.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="preview">When true, actuate the EC live without persisting the change (a volatile preview).</param>
    Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, bool preview = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves (or overwrites) one of a fan's curve profile slots, optionally activating it. A follow slot
    /// (<paramref name="followFanIndex"/> set) mirrors another fan's active curve and may omit curve points.
    /// </summary>
    Task<FrameworkFanCurveProfileCommandResult> SaveCurveProfileAsync(
        int fanIndex,
        int slot,
        string? name,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregationMode,
        int? followFanIndex,
        bool activate,
        bool treatMissingSensorsAsZero = false,
        CancellationToken cancellationToken = default);

    /// <summary>Activates a stored curve profile slot for the specified fan.</summary>
    Task<FrameworkFanCurveProfileCommandResult> SetActiveCurveProfileAsync(int fanIndex, int slot, CancellationToken cancellationToken = default);

    /// <summary>Clears a curve profile slot back to empty.</summary>
    Task<FrameworkFanCurveProfileCommandResult> ClearCurveProfileAsync(int fanIndex, int slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears, when <paramref name="linkedLeaderIndex"/> is null) which fan this one is grouped under
    /// for the "Applies to" link. Persisted by the service and streamed back via the control state's
    /// linked-leader, so the grouping survives restarts.
    /// </summary>
    Task<FrameworkFanCurveProfileCommandResult> SetFanLinkAsync(int fanIndex, int? linkedLeaderIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arms a fan into Adaptive mode against the given driving sensors.
    /// </summary>
    /// <remarks>
    /// Arming does not require a calibration: an uncalibrated fan runs on the conservative bootstrap model and
    /// improves as it learns. Failures come back as a message rather than an exception — a fan with no driving
    /// sensor is an expected state the UI turns into a call to action, not an error.
    /// </remarks>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="drivingSensorIndices">The sensors whose aggregate temperature the loop holds.</param>
    /// <param name="aggregation">How to combine them.</param>
    /// <param name="settings">Target and safety floor, or null to keep what the fan already had.</param>
    /// <param name="preview">When true, arm the loop live without persisting — the volatile preview contract every other mode's command carries.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FrameworkFanCurveProfileCommandResult> SetAdaptiveModeAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregation,
        AdaptiveFanSettings? settings,
        bool preview = false,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a fan's Adaptive target and safety floor without changing its mode.</summary>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="settings">The new settings.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FrameworkFanCurveProfileCommandResult> SetAdaptiveSettingsAsync(
        int fanIndex,
        AdaptiveFanSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears a latched throttle escalation on a fan.
    /// </summary>
    /// <remarks>
    /// Immediate and never staged. If the processor is still throttling the controller latches again on its
    /// next tick, which is the correct outcome — the escalation exists because cooling already lost once.
    /// </remarks>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FrameworkFanCurveProfileCommandResult> ReleaseThrottleLatchAsync(int fanIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards what a fan identified from ordinary use, returning it to its calibration or to safe defaults.
    /// </summary>
    /// <remarks>
    /// Destructive and immediate, never staged. For a machine that changed physically — a repaste, a new
    /// heatsink, a cleaned vent — where the identified model describes hardware that no longer exists.
    /// </remarks>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FrameworkFanCurveProfileCommandResult> ForgetAdaptiveLearningAsync(int fanIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets all fan control to factory defaults: every fan returns to the controller's automatic mode and
    /// every persisted fan setting is deleted (curve profile slots, active slot, "Applies to" links,
    /// manual / max overrides), including entries for fans that no longer enumerate.
    /// </summary>
    Task<FrameworkFanControlResetCommandResult> ResetFanControlToFactoryDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a preview safety hold for a fan and returns once the service has captured its pre-preview state.
    /// The hold stays open until <paramref name="cancellationToken"/> is cancelled (commit / revert / app
    /// exit); if it drops before the preview is committed, the service reverts the fan to its prior state.
    /// </summary>
    Task OpenPreviewHoldAsync(int fanIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the calibration hot test for a fan and returns the model it identified.
    /// </summary>
    /// <param name="fanIndex">The fan to calibrate.</param>
    /// <param name="drivingSensorIndices">The sensors the run measures against — the ones Adaptive will hold.</param>
    /// <param name="progress">Receives each update as it arrives, for the live plot. May be null.</param>
    /// <param name="cancellationToken">
    /// Cancels the run. This is the ONLY way to stop it: the call is the run's lease, so cancelling — or the
    /// app exiting — aborts the test, stops the CPU load, and hands the fan back.
    /// </param>
    /// <returns>
    /// The identified model, or a result describing why it could not be produced. A failed run is a normal
    /// outcome carried in the result, not an exception: the machine not getting hot enough is something the
    /// user can act on, and the measured values come back so the UI can tell them what to change.
    /// </returns>
    /// <remarks>
    /// This takes several minutes and deliberately heats the machine, running every core at full load and
    /// driving the fan to both extremes. Callers must show it as the deliberate, interruptible operation it
    /// is — never start one implicitly.
    /// </remarks>
    /// <param name="loadTarget">
    /// What to heat, or <see cref="ThermalLoadTarget.None"/> to let the service decide from the fan's cooling
    /// role. Worth passing whenever the user has been asked, because the role is inferred and a wrong guess
    /// costs a full run that could never have measured anything.
    /// </param>
    Task<FanCalibrationRunResult> RunCalibrationAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        IProgress<FanCalibrationProgress>? progress,
        CancellationToken cancellationToken,
        ThermalLoadTarget loadTarget = ThermalLoadTarget.None);

    /// <summary>Reads the battery charge floor/ceiling from the EC.</summary>
    Task<FrameworkChargeLimitsResult> GetChargeLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the battery charge floor/ceiling to the EC (gated by service authorization).</summary>
    Task<FrameworkChargeLimitsResult> SetChargeLimitsAsync(int minimumPercent, int maximumPercent, CancellationToken cancellationToken = default);
}
