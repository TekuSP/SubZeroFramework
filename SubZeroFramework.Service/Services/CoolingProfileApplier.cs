using System.Collections.Immutable;

using FrameworkDotnet.Enums;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// What applying a profile needs of the fan control stack.
/// </summary>
/// <remarks>
/// <para>
/// Narrowed to the handful of commands a profile can issue, so the decision logic — which fans to touch, in
/// what order, and what to do when one refuses — can be tested without a store, a data provider, or hardware.
/// </para>
/// <para>
/// Asynchronous because every one of these reaches the embedded controller. A synchronous seam would have
/// been tidier to write and a lie to use.
/// </para>
/// </remarks>
public interface IFanCommandTarget
{
    bool Exists(int fanIndex);

    string DisplayName(int fanIndex);

    Task<bool> TrySetAutoAsync(int fanIndex, CancellationToken cancellationToken);

    Task<bool> TrySetMaxAsync(int fanIndex, CancellationToken cancellationToken);

    Task<bool> TrySetDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken);

    /// <param name="drivingSensorIndices">
    /// The sensors the loop should hold. Empty means keep whatever the fan already has.
    /// </param>
    Task<bool> TrySetAdaptiveAsync(
        int fanIndex,
        double targetCelsius,
        IReadOnlyList<int> drivingSensorIndices,
        TemperatureAggregationMode aggregation,
        CancellationToken cancellationToken);

    /// <param name="drivingSensorIndices">See <see cref="TrySetAdaptiveAsync"/>.</param>
    Task<bool> TrySetCurveAsync(
        int fanIndex,
        IReadOnlyDictionary<int, double> points,
        TemperatureAggregationMode aggregation,
        IReadOnlyList<int> drivingSensorIndices,
        CancellationToken cancellationToken);
}

/// <summary>Puts every fan a profile mentions into the state that profile asks for.</summary>
public static class CoolingProfileApplier
{
    /// <summary>Applies a profile across the fans.</summary>
    /// <param name="profile">The profile to apply.</param>
    /// <param name="target">The fans to apply it to.</param>
    /// <param name="cancellationToken">Cancels the run between fans.</param>
    /// <returns>The display names of the fans that refused. Empty on complete success.</returns>
    /// <remarks>
    /// BEST EFFORT: one fan refusing must not abandon the rest half-applied, which would leave the machine in
    /// a state no profile describes and no card can label. Ascending fan index, so the outcome never depends
    /// on the order the profile happened to be written in.
    /// </remarks>
    public static async Task<ImmutableArray<string>> ApplyAsync(
        CoolingProfile profile,
        IFanCommandTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);

        var failed = ImmutableArray.CreateBuilder<string>();

        foreach (var entry in profile.Fans.OrderBy(static entry => entry.FanIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Not a failure worth reporting: a profile written while an expansion module was attached should
            // still apply once it is removed, rather than complaining about a fan that is simply gone.
            if (!target.Exists(entry.FanIndex))
            {
                continue;
            }

            var applied = entry.Mode switch
            {
                FanControlMode.Max => await target.TrySetMaxAsync(entry.FanIndex, cancellationToken).ConfigureAwait(false),
                FanControlMode.Manual => await target.TrySetDutyAsync(entry.FanIndex, entry.DutyPercent, cancellationToken).ConfigureAwait(false),
                FanControlMode.Adaptive => await target.TrySetAdaptiveAsync(entry.FanIndex, entry.AdaptiveTargetCelsius, entry.DrivingSensorIndices, entry.Aggregation, cancellationToken).ConfigureAwait(false),
                FanControlMode.CustomCurve => await target.TrySetCurveAsync(entry.FanIndex, entry.CurvePoints, entry.Aggregation, entry.DrivingSensorIndices, cancellationToken).ConfigureAwait(false),

                // Auto is also the fallback for a mode this build does not recognise. A profile written by a
                // newer client should hand the fan back to the firmware rather than leave it wherever the
                // previously applied profile happened to put it.
                _ => await target.TrySetAutoAsync(entry.FanIndex, cancellationToken).ConfigureAwait(false),
            };

            if (!applied)
            {
                failed.Add(target.DisplayName(entry.FanIndex));
            }
        }

        return failed.ToImmutable();
    }
}
