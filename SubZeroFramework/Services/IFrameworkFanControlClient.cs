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
    /// Sets (or clears, when <paramref name="cpuUsageModifierStrength"/> is null) the fan's CPU usage
    /// modifier: the duty points added on top of the active custom curve at 100% smoothed CPU usage,
    /// ramping exponentially so light load adds almost nothing. Applies only while a custom curve drives
    /// the fan. Persisted by the service and streamed back via the control state.
    /// </summary>
    Task<FrameworkFanUsageModifierCommandResult> SetUsageModifierAsync(int fanIndex, double? cpuUsageModifierStrength, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a preview safety hold for a fan and returns once the service has captured its pre-preview state.
    /// The hold stays open until <paramref name="cancellationToken"/> is cancelled (commit / revert / app
    /// exit); if it drops before the preview is committed, the service reverts the fan to its prior state.
    /// </summary>
    Task OpenPreviewHoldAsync(int fanIndex, CancellationToken cancellationToken);

    /// <summary>Reads the battery charge floor/ceiling from the EC.</summary>
    Task<FrameworkChargeLimitsResult> GetChargeLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the battery charge floor/ceiling to the EC (gated by service authorization).</summary>
    Task<FrameworkChargeLimitsResult> SetChargeLimitsAsync(int minimumPercent, int maximumPercent, CancellationToken cancellationToken = default);
}
