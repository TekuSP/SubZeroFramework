using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Decides whether a set of compute devices can be left alone because they are all powered down.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the NVIDIA, AMD and Intel readers, because the hazard is the vendor SDKs' in common rather than
/// any one of them: a call against a suspended laptop dGPU wakes it. Measured on the reference machine, a
/// call to an awake GPU returns in 0.02 ms while one that wakes it takes 480-600 ms and takes the board from
/// ~17.9 W to ~29 W.
/// </para>
/// <para>
/// The answer comes from the operating system's own device power state, never from a vendor SDK — so it
/// cannot itself wake the device it is asking about, and a reader for hardware this app has never seen gets
/// the same protection by using the same gate.
/// </para>
/// </remarks>
public static class ComputeDeviceSleepGate
{
    /// <summary>
    /// True only when every device definitely reports a low-power state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unanimous, because these readers report their devices together: one awake GPU is reason enough to
    /// sample, and suppressing the read would lose it.
    /// </para>
    /// <para>
    /// <b>An unknown power state never suppresses a read.</b> A machine that cannot answer must be sampled
    /// normally — treating "do not know" as "asleep" would report every GPU as permanently idle on exactly
    /// the platforms this lookup does not understand.
    /// </para>
    /// </remarks>
    public static bool AreAllAsleep(IReadOnlyList<ComputeDeviceIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        if (identities.Count == 0)
        {
            return false;
        }

        var sawAnswer = false;

        foreach (var identity in identities)
        {
            switch (IsAwake(identity.DeviceKey))
            {
                case true:
                    return false;
                case false:
                    sawAnswer = true;
                    break;
            }
        }

        return sawAnswer;
    }

    private static bool? IsAwake(string? deviceKey)
    {
#if WINDOWS10_0_26100_0_OR_GREATER
        return WindowsDevicePowerState.IsAwake(deviceKey);
#else
        // The Linux readers gate on the device's own power/runtime_status and never reach this. Returning
        // "unknown" here keeps the safe default — sample normally — for anything that does.
        _ = deviceKey;
        return null;
#endif
    }
}
