namespace SubZeroFramework.Models;

/// <summary>
/// What more fan actually bought: sustained speed at a low duty against sustained speed at full duty.
/// </summary>
/// <remarks>
/// <para>
/// The rest of a calibration answers "how much does duty cool it?". This answers the question the user
/// actually has, which is "what do I get for the noise?". They are not the same question and the answer is
/// not always encouraging: a machine limited by its power budget rather than its heatsink will run several
/// degrees cooler at full fan and not one megahertz faster, and the honest thing is to be able to say so.
/// </para>
/// <para>
/// Free to collect. The step response already holds the load constant and sweeps duty from the pre-step
/// value to 100%, which is exactly the experiment this needs — it only had to be recorded.
/// </para>
/// </remarks>
public sealed record FanPerformanceResponse
{
    /// <summary>
    /// The smallest speed gain worth calling a gain.
    /// </summary>
    /// <remarks>
    /// Below this the difference is inside the noise of two averaged clock readings, and reporting it as
    /// "+1% sustained speed" would dress measurement scatter up as a benefit. Saying "no extra speed" instead
    /// is both more honest and more useful — it tells the user the fan is not their limit.
    /// </remarks>
    public const double MeaningfulSpeedGainFraction = 0.02d;

    /// <summary>Nothing measured — an older calibration, or a machine that reports no speed at all.</summary>
    public static FanPerformanceResponse None { get; } = new();

    /// <summary>The duty held while heat built, before the step.</summary>
    public double LowDutyPercent { get; init; }

    /// <summary>The duty stepped to, effectively always 100.</summary>
    public double FullDutyPercent { get; init; }

    /// <summary>Clock as a fraction of base at the low duty; below 1 means the processor was held back.</summary>
    public double? CpuPerformanceRatioAtLowDuty { get; init; }

    /// <summary>Clock as a fraction of base at full duty.</summary>
    public double? CpuPerformanceRatioAtFullDuty { get; init; }

    /// <summary>Graphics core clock at the low duty, in MHz.</summary>
    public double? GpuCoreClockAtLowDutyMegahertz { get; init; }

    /// <summary>Graphics core clock at full duty, in MHz.</summary>
    public double? GpuCoreClockAtFullDutyMegahertz { get; init; }

    /// <summary>True when at least one component reported a usable pair of speeds.</summary>
    public bool HasMeasurement
        => (CpuPerformanceRatioAtLowDuty is not null && CpuPerformanceRatioAtFullDuty is not null)
            || (GpuCoreClockAtLowDutyMegahertz is not null && GpuCoreClockAtFullDutyMegahertz is not null);

    /// <summary>
    /// The sustained speed gained by going from the low duty to full, as a fraction — 0.06 being six per cent
    /// more clock. Null when nothing usable was measured.
    /// </summary>
    /// <remarks>
    /// Reported from whichever component the run actually loaded. Both are never populated at once, because a
    /// run loads exactly one: CPU and GPU share a power budget, and heating both would leave neither at the
    /// operating point being described.
    /// </remarks>
    public double? SustainedSpeedGainFraction
    {
        get
        {
            if (CpuPerformanceRatioAtLowDuty is double cpuLow and > 0d && CpuPerformanceRatioAtFullDuty is double cpuFull)
            {
                return (cpuFull - cpuLow) / cpuLow;
            }

            if (GpuCoreClockAtLowDutyMegahertz is double gpuLow and > 0d && GpuCoreClockAtFullDutyMegahertz is double gpuFull)
            {
                return (gpuFull - gpuLow) / gpuLow;
            }

            return null;
        }
    }
}
