namespace SubZeroFramework.Service.Services;

/// <summary>
/// Records which fan, if any, a calibration currently owns — so the curve worker stops driving it.
/// </summary>
/// <remarks>
/// <para>
/// Without this the two write to the same fan at once. A calibration commands a duty and then measures what
/// that duty did; the curve worker re-resolves every non-Auto fan on every thermal tick and writes its own
/// answer. The run would be fitting a model to a fan whose duty something else kept changing, and the result
/// would look entirely plausible — a K, a τ, an L, all wrong. Nothing downstream could detect it.
/// </para>
/// <para>
/// A side-channel rather than a mode on the fan's state, deliberately. Calibration is a transient physical
/// operation, not a control mode the user chose: writing it into the state would persist it, publish it to
/// every client as though the user had selected it, and leave a fan stuck in "Calibrating" if the service
/// died mid-run. This lives in memory and dies with the process, which is exactly the lifetime it wants.
/// </para>
/// </remarks>
public sealed class FanCalibrationArbiter
{
    private const int NoFan = -1;

    private readonly Lock _claimLock = new();
    private int _claimedFanIndex = NoFan;

    /// <summary>The fan a calibration currently owns, or null when none does.</summary>
    public int? ClaimedFanIndex
    {
        get
        {
            lock (_claimLock)
            {
                return _claimedFanIndex == NoFan ? null : _claimedFanIndex;
            }
        }
    }

    /// <summary>
    /// Claims a fan for a calibration run.
    /// </summary>
    /// <param name="fanIndex">The fan to claim.</param>
    /// <returns>False when another fan is already claimed.</returns>
    /// <remarks>
    /// One claim machine-wide, not one per fan. Calibrating two fans at once would heat a single chassis while
    /// each run assumed it owned the thermal conditions.
    /// </remarks>
    public bool TryClaim(int fanIndex)
    {
        lock (_claimLock)
        {
            if (_claimedFanIndex != NoFan)
            {
                return false;
            }

            _claimedFanIndex = fanIndex;
            return true;
        }
    }

    /// <summary>
    /// Releases a fan claimed by <see cref="TryClaim"/>.
    /// </summary>
    /// <param name="fanIndex">The fan to release; ignored if it does not hold the claim.</param>
    public void Release(int fanIndex)
    {
        lock (_claimLock)
        {
            if (_claimedFanIndex == fanIndex)
            {
                _claimedFanIndex = NoFan;
            }
        }
    }

    /// <summary>True while a calibration owns this fan, and nothing else may write to it.</summary>
    /// <remarks>
    /// True for EVERY fan while any run is active, not only the measured one. A run pins the fans it is not
    /// measuring at a fixed duty — a sibling on the shared heatpipe left under closed-loop control regulates
    /// against the step being identified — so for the life of the claim the run owns them all, and a worker
    /// that wrote to any of them would be unpinning the run's controlled conditions.
    /// </remarks>
    public bool IsCalibrating(int fanIndex)
    {
        lock (_claimLock)
        {
            return _claimedFanIndex != NoFan;
        }
    }
}
