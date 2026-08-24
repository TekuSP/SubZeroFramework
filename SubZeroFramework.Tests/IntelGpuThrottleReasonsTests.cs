using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the Intel throttle-reason mapping.
/// </summary>
/// <remarks>
/// The stems below are the kernel ABI verbatim — i915's <c>throttle_reason_*</c> set from
/// <c>intel_gt_sysfs_pm.c</c> and xe's <c>reason_*</c> set documented in <c>xe_gt_throttle.c</c>. This is the
/// one extended signal an Intel iGPU on Linux can report, and it feeds the fan controller's escalation
/// decision directly, so the thermal-versus-not split is what these tests actually guard.
/// </remarks>
[TestFixture]
public class IntelGpuThrottleReasonsTests
{
    [Test]
    public void PackageAndPlatformPowerLimits_MapToPowerLimit()
    {
        Assert.Multiple(() =>
        {
            foreach (var stem in new[] { "pl1", "pl2", "pl4", "psys_pl1", "psys_pl2" })
            {
                Assert.That(IntelGpuThrottleReasons.Map(stem), Is.EqualTo(ComputeThrottleReasons.PowerLimit), stem);
            }
        });
    }

    /// <summary>Every thermal spelling either driver uses has to reach ThermalLimit — that is the flag more airflow answers.</summary>
    [Test]
    public void EveryThermalSpelling_MapsToThermalLimit()
    {
        Assert.Multiple(() =>
        {
            foreach (var stem in new[] { "thermal", "soc_thermal", "mem_thermal", "vr_thermal", "vr_thermalert", "soc_avg_thermal", "ratl" })
            {
                Assert.That(IntelGpuThrottleReasons.Map(stem), Is.EqualTo(ComputeThrottleReasons.ThermalLimit), stem);
            }
        });
    }

    /// <summary>PROCHOT is an externally asserted over-temperature signal, so it is thermal, not "other".</summary>
    [Test]
    public void Prochot_MapsToThermalLimit()
    {
        Assert.That(IntelGpuThrottleReasons.Map("prochot"), Is.EqualTo(ComputeThrottleReasons.ThermalLimit));
    }

    /// <summary>
    /// Voltage-regulator current protection is NOT a power budget. Mapping it to PowerLimit would have the
    /// controller treat an electrical limit — which airflow cannot relieve — as a coolable condition.
    /// </summary>
    [Test]
    public void VoltageRegulatorLimits_MapToOther_NotPowerLimit()
    {
        Assert.Multiple(() =>
        {
            foreach (var stem in new[] { "vr_tdc", "iccmax", "fastvmode" })
            {
                Assert.That(IntelGpuThrottleReasons.Map(stem), Is.EqualTo(ComputeThrottleReasons.Other), stem);
            }
        });
    }

    [Test]
    public void Combine_UnionsEveryAssertedReason()
    {
        var reasons = IntelGpuThrottleReasons.Combine(["pl1", "thermal", "vr_tdc"]);

        Assert.Multiple(() =>
        {
            Assert.That(reasons.HasFlag(ComputeThrottleReasons.PowerLimit), Is.True);
            Assert.That(reasons.HasFlag(ComputeThrottleReasons.ThermalLimit), Is.True);
            Assert.That(reasons.HasFlag(ComputeThrottleReasons.Other), Is.True);
        });
    }

    /// <summary>
    /// Nothing asserted is None — "asked, and it is not throttling". The caller uses null for "could not
    /// ask", and the controller is allowed to relax on the first but must not on the second.
    /// </summary>
    [Test]
    public void Combine_WithNothingAsserted_IsNoneNotOther()
    {
        Assert.That(IntelGpuThrottleReasons.Combine([]), Is.EqualTo(ComputeThrottleReasons.None));
    }

    /// <summary>Every stem the reader probes must have a mapping, or it would silently fold into Other.</summary>
    [Test]
    public void EveryProbedStem_HasAnExplicitMapping()
    {
        var unmapped = IntelGpuThrottleReasons.ReasonStems
            .Where(stem => IntelGpuThrottleReasons.Map(stem) == ComputeThrottleReasons.Other)
            .Except(["vr_tdc", "iccmax", "fastvmode"])
            .ToArray();

        Assert.That(unmapped, Is.Empty, "these stems fell through to Other without being intended to");
    }
}
