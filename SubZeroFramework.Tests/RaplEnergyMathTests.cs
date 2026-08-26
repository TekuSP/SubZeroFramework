using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers the RAPL energy arithmetic directly, so the wrap handling — the part most likely to be wrong and
/// least likely to be noticed — is tested on every platform rather than only where <c>/sys</c> exists.
/// </summary>
[TestFixture]
public class RaplEnergyMathTests
{
    [Test]
    public void IsPackageZoneName_AcceptsTopLevelZones()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl:0"), Is.True);
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl:1"), Is.True);
        });
    }

    /// <summary>
    /// An AMD part registers its package zone under either spelling depending on the kernel.
    /// </summary>
    /// <remarks>
    /// Matching only the Intel name left AMD machines with no package power on the kernels where it does
    /// work — which on a Framework 16, an AMD laptop, is the machine this app exists for.
    /// </remarks>
    [Test]
    public void IsPackageZoneName_AcceptsAmdZones()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.IsPackageZoneName("amd-rapl:0"), Is.True);
            Assert.That(RaplEnergyMath.IsPackageZoneName("amd-rapl:0:0"), Is.False);
        });
    }

    [Test]
    public void IsPackageZoneName_RejectsNestedSubzones()
    {
        // intel-rapl:0:0 and friends are core / uncore / dram slices of the same package budget. Treating one
        // as the package total under-reports; summing them alongside the package double counts.
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl:0:0"), Is.False);
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl:0:1"), Is.False);
        });
    }

    [Test]
    public void IsPackageZoneName_RejectsUnrelatedNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.IsPackageZoneName("dtpm"), Is.False);
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl-mmio:0"), Is.False);
            Assert.That(RaplEnergyMath.IsPackageZoneName(string.Empty), Is.False);
        });
    }

    [Test]
    public void ComputeWatts_DividesEnergyByTheWindow()
    {
        // 30 J over 2 s is 15 W.
        var watts = RaplEnergyMath.ComputeWatts(
            previousMicrojoules: 1_000_000,
            currentMicrojoules: 31_000_000,
            elapsedSeconds: 2d,
            rangeMicrojoules: 60_000_000d);

        Assert.That(watts, Is.EqualTo(15d).Within(1e-9));
    }

    [Test]
    public void ComputeWatts_AddsTheRangeBackWhenTheCounterWraps()
    {
        // 1 J to the top of the range, then 2 J past it: 3 J over 1 s. Read naively this is a large NEGATIVE
        // power, and on a laptop the counter wraps every few minutes — so the naive form would be wrong
        // repeatedly, and most often while the machine is busy.
        var watts = RaplEnergyMath.ComputeWatts(
            previousMicrojoules: 59_000_000,
            currentMicrojoules: 2_000_000,
            elapsedSeconds: 1d,
            rangeMicrojoules: 60_000_000d);

        Assert.That(watts, Is.EqualTo(3d).Within(1e-9));
    }

    [Test]
    public void ComputeWatts_ReturnsNothingWhenAWrapCannotBeMeasured()
    {
        var watts = RaplEnergyMath.ComputeWatts(
            previousMicrojoules: 59_000_000,
            currentMicrojoules: 2_000_000,
            elapsedSeconds: 1d,
            rangeMicrojoules: null);

        Assert.That(watts, Is.Null);
    }

    [Test]
    public void ComputeWatts_ReturnsNothingWhenTheCounterMovesBackFurtherThanOneWrap()
    {
        // A reset or a zone swapped underneath us. Adding one range still leaves it negative, and a negative
        // power is not a reading worth reporting.
        var watts = RaplEnergyMath.ComputeWatts(
            previousMicrojoules: 90_000_000,
            currentMicrojoules: 1_000_000,
            elapsedSeconds: 1d,
            rangeMicrojoules: 60_000_000d);

        Assert.That(watts, Is.Null);
    }

    [Test]
    public void ComputeWatts_ReturnsNothingForANonPositiveWindow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.ComputeWatts(0, 1_000_000, 0d, null), Is.Null);
            Assert.That(RaplEnergyMath.ComputeWatts(0, 1_000_000, -1d, null), Is.Null);
            Assert.That(RaplEnergyMath.ComputeWatts(0, 1_000_000, double.NaN, null), Is.Null);
        });
    }

    [Test]
    public void ComputeWatts_ReportsZeroForAnIdlePackage()
    {
        // The counter genuinely not advancing IS zero watts here, unlike the utilisation case where a zero
        // window means "no information".
        var watts = RaplEnergyMath.ComputeWatts(
            previousMicrojoules: 5_000_000,
            currentMicrojoules: 5_000_000,
            elapsedSeconds: 1d,
            rangeMicrojoules: 60_000_000d);

        Assert.That(watts, Is.Zero);
    }

    /// <summary>
    /// The GPU plane is a NESTED zone, and the package reader must keep ignoring it while the Intel GPU
    /// reader specifically looks for it. Confusing the two would either report the iGPU's draw as whole-CPU
    /// power or double count it.
    /// </summary>
    [Test]
    public void SubzoneAndPackageZoneNames_AreMutuallyExclusive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl:0"), Is.True);
            Assert.That(RaplEnergyMath.IsSubzoneName("intel-rapl:0"), Is.False);

            Assert.That(RaplEnergyMath.IsSubzoneName("intel-rapl:0:1"), Is.True);
            Assert.That(RaplEnergyMath.IsPackageZoneName("intel-rapl:0:1"), Is.False);
        });
    }

    /// <summary>
    /// PP1 is spelled "uncore" in powercap even though the kernel documents the matching perf event as
    /// energy_gpu. Renaming this constant to something more descriptive would stop it matching the sysfs
    /// name file, which is what the lookup keys on.
    /// </summary>
    [Test]
    public void GpuDomainName_IsThePowercapSpellingOfPp1()
    {
        Assert.That(RaplEnergyMath.GpuDomainName, Is.EqualTo("uncore"));
    }

    [Test]
    public void SubzoneName_RejectsUnrelatedDirectories()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaplEnergyMath.IsSubzoneName("intel-rapl-mmio:0"), Is.False);
            Assert.That(RaplEnergyMath.IsSubzoneName(""), Is.False);
            Assert.That(RaplEnergyMath.IsSubzoneName("dtpm"), Is.False);
        });
    }
}
