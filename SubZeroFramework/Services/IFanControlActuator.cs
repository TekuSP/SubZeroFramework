using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>
/// Actuates fans through <see cref="IFrameworkFanControlClient"/> and owns the live preview safety-hold
/// lifecycle. Centralises the "talk to the fan" plumbing (mode dispatch, preview vs persist, the disconnect
/// watchdog hold) so the Fan Control view model is not the one juggling the gRPC client and a hold token.
/// </summary>
public interface IFanControlActuator
{
    /// <summary>Actuates a simple mode (Auto/Manual/Max). When <paramref name="preview"/> is true the service does not persist it.</summary>
    Task ActuateSimpleAsync(int fanIndex, FanControlMode mode, double manualDutyPercent, bool preview, CancellationToken cancellationToken = default);

    /// <summary>Actuates a custom curve. When <paramref name="preview"/> is true the service does not persist it.</summary>
    Task<FrameworkFanCustomCurveCommandResult> ActuateCurveAsync(
        int fanIndex,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregation,
        bool preview,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the preview safety hold for every fan in <paramref name="fanIndices"/> (the linked group) and returns
    /// once the service has captured each one's pre-preview state. Replaces any previously open holds; the service
    /// reverts a held fan if its hold drops before a commit, so the whole previewed group reverts on disconnect.
    /// </summary>
    Task OpenPreviewHoldsAsync(IReadOnlyCollection<int> fanIndices);

    /// <summary>Closes all open preview holds. A committed preview releases the service-side holds first, so this is then a no-op.</summary>
    void CancelPreviewHold();
}
