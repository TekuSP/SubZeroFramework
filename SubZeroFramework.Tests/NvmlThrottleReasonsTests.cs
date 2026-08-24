using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the NVML throttle-reason bit mapping. NVML is the only source here that reports throttling outright
/// rather than leaving it to be inferred from a clock ratio, so this mapping is what the adaptive controller's
/// escalation term ultimately keys off.
/// </summary>
/// <remarks>
/// The bit values are nvml.h verbatim. A mis-mapped bit does not fail loudly — it silently classifies a
/// thermally limited GPU as idle, or an idle one as thermally limited, and the controller acts on that.
/// </remarks>
[TestFixture]
public class NvmlThrottleReasonsTests
{
    private const ulong GpuIdle = 0x1;
    private const ulong ApplicationsClocksSetting = 0x2;
    private const ulong SwPowerCap = 0x4;
    private const ulong HwSlowdown = 0x8;
    private const ulong SyncBoost = 0x10;
    private const ulong SwThermalSlowdown = 0x20;
    private const ulong HwThermalSlowdown = 0x40;
    private const ulong HwPowerBrakeSlowdown = 0x80;
    private const ulong DisplayClockSetting = 0x100;

    [Test]
    public void Map_ZeroMeansNothingIsHoldingTheClocksBack()
    {
        // Not the same as "we could not read it" — the reader returns null for that, and the difference
        // decides whether the controller is allowed to escalate.
        Assert.That(NvmlThrottleReasons.Map(0UL), Is.EqualTo(ComputeThrottleReasons.None));
    }

    [Test]
    public void Map_RecognisesThermalSlowdowns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlThrottleReasons.Map(SwThermalSlowdown), Is.EqualTo(ComputeThrottleReasons.ThermalLimit));
            Assert.That(NvmlThrottleReasons.Map(HwThermalSlowdown), Is.EqualTo(ComputeThrottleReasons.ThermalLimit));
        });
    }

    [Test]
    public void Map_RecognisesPowerLimits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlThrottleReasons.Map(SwPowerCap), Is.EqualTo(ComputeThrottleReasons.PowerLimit));
            Assert.That(NvmlThrottleReasons.Map(HwPowerBrakeSlowdown), Is.EqualTo(ComputeThrottleReasons.PowerLimit));
        });
    }

    [Test]
    public void Map_TreatsRequestedCeilingsAsApplicationLimits()
    {
        // Someone asked for these; the hardware did not run into them, so more airflow will not help.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlThrottleReasons.Map(ApplicationsClocksSetting), Is.EqualTo(ComputeThrottleReasons.ApplicationLimit));
            Assert.That(NvmlThrottleReasons.Map(DisplayClockSetting), Is.EqualTo(ComputeThrottleReasons.ApplicationLimit));
        });
    }

    [Test]
    public void Map_TreatsIdleAsIdleRatherThanAsAProblem()
    {
        // The single most damaging confusion available here: an idle GPU has low clocks, and a naive
        // clock-ratio throttle detector reads that as throttling and spins the fans up over nothing.
        Assert.That(NvmlThrottleReasons.Map(GpuIdle), Is.EqualTo(ComputeThrottleReasons.Idle));
    }

    [Test]
    public void Map_DoesNotClaimGenericHardwareSlowdownIsThermal()
    {
        // NVML documents HwSlowdown as thermal OR power brake OR a board fault, and sets the finer-grained
        // bits alongside it when the driver knows which. Calling it thermal on its own would have the
        // controller chase heat that may not be the cause.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlThrottleReasons.Map(HwSlowdown), Is.EqualTo(ComputeThrottleReasons.Other));
            Assert.That(NvmlThrottleReasons.Map(SyncBoost), Is.EqualTo(ComputeThrottleReasons.Other));
        });
    }

    [Test]
    public void Map_CombinesEveryReasonThatIsSet()
    {
        // The real case when a GPU is genuinely cooking: the driver sets the hardware slowdown bit AND the
        // specific thermal bit together.
        var mapped = NvmlThrottleReasons.Map(HwSlowdown | HwThermalSlowdown | SwPowerCap);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.HasFlag(ComputeThrottleReasons.ThermalLimit), Is.True);
            Assert.That(mapped.HasFlag(ComputeThrottleReasons.PowerLimit), Is.True);
            Assert.That(mapped.HasFlag(ComputeThrottleReasons.Other), Is.True);
            Assert.That(mapped.HasFlag(ComputeThrottleReasons.Idle), Is.False);
        });
    }

    [Test]
    public void Map_IgnoresBitsItDoesNotKnow()
    {
        // NVML has added bits over time. An unrecognised one must not be mistaken for a known reason, and
        // must not throw — a future driver is not a reason for the fan controller to fall over.
        var mapped = NvmlThrottleReasons.Map(0x8000_0000_0000_0000UL | HwThermalSlowdown);

        Assert.That(mapped, Is.EqualTo(ComputeThrottleReasons.ThermalLimit));
    }
}
