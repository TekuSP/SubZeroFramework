using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Translates NVML's <c>nvmlClocksThrottleReasons</c> bitmask into the vendor-neutral
/// <see cref="ComputeThrottleReasons"/>.
/// </summary>
/// <remarks>
/// NVML is the only source here that reports throttling OUTRIGHT rather than leaving it to be inferred from a
/// clock ratio, which is what makes this mapping worth getting exactly right — a clock ratio cannot tell an
/// idle GPU apart from a thermally limited one, and this can.
///
/// Kept cross-platform and separate from the reader so the bit mapping is testable without an NVIDIA driver,
/// a GPU, or Linux.
/// </remarks>
public static class NvmlThrottleReasons
{
    // Values from nvml.h. Named here rather than inlined because a bare hex literal in a bitmask test tells a
    // future reader nothing about which condition it stands for.
    private const ulong GpuIdle = 0x0000000000000001UL;
    private const ulong ApplicationsClocksSetting = 0x0000000000000002UL;
    private const ulong SwPowerCap = 0x0000000000000004UL;
    private const ulong HwSlowdown = 0x0000000000000008UL;
    private const ulong SyncBoost = 0x0000000000000010UL;
    private const ulong SwThermalSlowdown = 0x0000000000000020UL;
    private const ulong HwThermalSlowdown = 0x0000000000000040UL;
    private const ulong HwPowerBrakeSlowdown = 0x0000000000000080UL;
    private const ulong DisplayClockSetting = 0x0000000000000100UL;

    /// <summary>
    /// Maps the bitmask. Returns <see cref="ComputeThrottleReasons.None"/> for zero — NVML answered, and
    /// nothing is holding the clocks back.
    /// </summary>
    /// <remarks>
    /// A caller that could not read the bitmask at all must report NULL instead of calling this: null means
    /// "we cannot tell", and <see cref="ComputeThrottleReasons.None"/> means "we asked and it is fine". The
    /// adaptive controller escalates on the second and cannot act on the first.
    /// </remarks>
    public static ComputeThrottleReasons Map(ulong bitmask)
    {
        var reasons = ComputeThrottleReasons.None;

        if ((bitmask & GpuIdle) != 0)
        {
            reasons |= ComputeThrottleReasons.Idle;
        }

        // Both of these are a ceiling somebody asked for, not one the hardware ran into.
        if ((bitmask & (ApplicationsClocksSetting | DisplayClockSetting)) != 0)
        {
            reasons |= ComputeThrottleReasons.ApplicationLimit;
        }

        if ((bitmask & (SwPowerCap | HwPowerBrakeSlowdown)) != 0)
        {
            reasons |= ComputeThrottleReasons.PowerLimit;
        }

        // The reason more airflow actually fixes, and so the one the controller escalates hardest on.
        if ((bitmask & (SwThermalSlowdown | HwThermalSlowdown)) != 0)
        {
            reasons |= ComputeThrottleReasons.ThermalLimit;
        }

        // HwSlowdown is deliberately NOT folded into ThermalLimit. NVML documents it as thermal OR power
        // brake OR a board fault, and the finer-grained bits are set alongside it when the driver knows which.
        // Calling it thermal on its own would have the controller chase heat that may not be the cause.
        if ((bitmask & (HwSlowdown | SyncBoost)) != 0)
        {
            reasons |= ComputeThrottleReasons.Other;
        }

        return reasons;
    }
}
