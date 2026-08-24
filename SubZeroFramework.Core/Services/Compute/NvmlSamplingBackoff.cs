namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Decides how long to leave a discrete GPU alone between NVML samples.
/// </summary>
/// <remarks>
/// <para>
/// A per-device NVML call takes a runtime-PM reference: on a laptop dGPU that is powering down, the call
/// WAKES it. Measured on a Framework 16 with an RTX 5070 — a call to a GPU that is awake returns in 0.02 ms,
/// while one that has to wake it takes 480-600 ms and the board jumps from ~17.9 W to ~29 W. Sampling every
/// second regardless therefore pins an idle dGPU awake and burns roughly 19 W for telemetry nobody is
/// reading.
/// </para>
/// <para>
/// The call duration IS the signal, which is what makes this possible without any new interop: a fast call
/// means the GPU was already awake and there is nothing to protect, so sample freely. A slow call means the
/// GPU was asleep, so back off hard and give it a long stretch to power down again.
/// </para>
/// <para>
/// This is a MITIGATION, not a cure. It cuts the disturbance from once a second to once a minute; it cannot
/// eliminate it. The real fix is to ask Windows for the device's power state (D0 vs D3) via CfgMgr32 and skip
/// NVML entirely while it is down — the direct equivalent of the Linux reader's <c>power/runtime_status</c>
/// gate. That needs interop this does not.
/// </para>
/// </remarks>
public static class NvmlSamplingBackoff
{
    /// <summary>
    /// Above this, the call is taken to have woken the GPU rather than merely queried it.
    /// </summary>
    /// <remarks>
    /// An order of magnitude above the measured awake case (0.02 ms) and an order below the measured wake
    /// case (480-600 ms), so neither ordinary jitter nor a slow-but-awake call trips it.
    /// </remarks>
    public static readonly TimeSpan WakeCostThreshold = TimeSpan.FromMilliseconds(50);

    /// <summary>No extra wait when the GPU is already awake — the tier's own interval paces it.</summary>
    public static readonly TimeSpan AwakeInterval = TimeSpan.Zero;

    /// <summary>How long to leave a sleeping GPU alone after a call that had to wake it.</summary>
    /// <remarks>
    /// Long enough for the driver to power the GPU back down and stay there for a useful stretch, short
    /// enough that a user who starts a game sees telemetry within about a minute — and once it IS busy, calls
    /// go fast again and sampling returns to the tier rate immediately.
    /// </remarks>
    public static readonly TimeSpan SleepingInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The minimum time to wait before sampling again, given how long the last sample took.
    /// </summary>
    /// <param name="lastSampleDuration">Wall-clock duration of the most recent NVML sample.</param>
    public static TimeSpan GetMinimumInterval(TimeSpan lastSampleDuration)
        => lastSampleDuration >= WakeCostThreshold ? SleepingInterval : AwakeInterval;

    /// <summary>
    /// Whether enough time has passed to sample again.
    /// </summary>
    /// <param name="sinceLastSample">Time since the last completed sample.</param>
    /// <param name="lastSampleDuration">Duration of that sample.</param>
    public static bool ShouldSample(TimeSpan sinceLastSample, TimeSpan lastSampleDuration)
        => sinceLastSample >= GetMinimumInterval(lastSampleDuration);
}
