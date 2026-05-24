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
    Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces the fan to 100% duty (Max mode).
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    Task<FrameworkFanMaxCommandResult> SetFanMaxAsync(int fanIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a custom fan curve for the specified fan and records the driving sensors and aggregation mode in the service-side state store.
    /// </summary>
    Task<FrameworkFanCustomCurveCommandResult> SetCustomCurveAsync(
        int fanIndex,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregationMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores automatic fan control for the specified fan.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, CancellationToken cancellationToken = default);
}
