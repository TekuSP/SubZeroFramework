using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins where the Intel drivers keep the current-clock attribute, and how a PMU directory name maps back to a
/// PCI address.
/// </summary>
/// <remarks>
/// These are tested here rather than through the reader because the reader cannot be exercised off Linux: its
/// devices only exist once perf_event_open has succeeded against a real PMU, so nothing ever reaches the
/// frequency code on a machine without one.
/// </remarks>
[TestFixture]
public class IntelGpuSysfsPathsTests
{
    [Test]
    public void ExtractBusAddress_ReturnsNothingForAnIntegratedGpu()
    {
        // i915 names the integrated PMU with no address at all.
        Assert.Multiple(() =>
        {
            Assert.That(IntelGpuSysfsPaths.ExtractBusAddress("i915"), Is.Null);
            Assert.That(IntelGpuSysfsPaths.ExtractBusAddress("xe"), Is.Null);
            Assert.That(IntelGpuSysfsPaths.ExtractBusAddress(string.Empty), Is.Null);
        });
    }

    [Test]
    public void ExtractBusAddress_RestoresColonsForADiscreteGpu()
    {
        // A PMU name cannot contain colons, so the driver substitutes underscores. Failing to undo that means
        // the address never matches a DRM card and the clock is silently never found.
        Assert.Multiple(() =>
        {
            Assert.That(IntelGpuSysfsPaths.ExtractBusAddress("i915_0000_03_00.0"), Is.EqualTo("0000:03:00.0"));
            Assert.That(IntelGpuSysfsPaths.ExtractBusAddress("xe_0000_c1_00.0"), Is.EqualTo("0000:c1:00.0"));
        });
    }

    [Test]
    public void GetFrequencyAttributePath_UsesTheCardDirectoryForI915()
    {
        var path = IntelGpuSysfsPaths.GetFrequencyAttributePath(
            cardPath: Path.Combine("sys", "class", "drm", "card0"),
            devicePath: Path.Combine("sys", "class", "drm", "card0", "device"),
            driverName: "i915");

        Assert.That(path, Is.EqualTo(Path.Combine("sys", "class", "drm", "card0", "gt_cur_freq_mhz")));
    }

    [Test]
    public void GetFrequencyAttributePath_UsesThePerTileTreeForXe()
    {
        var path = IntelGpuSysfsPaths.GetFrequencyAttributePath(
            cardPath: Path.Combine("sys", "class", "drm", "card0"),
            devicePath: Path.Combine("sys", "class", "drm", "card0", "device"),
            driverName: "xe");

        // xe moved the clock under the device into a per-tile, per-GT tree. Reading the i915 location on an
        // xe machine finds nothing, and the GPU silently reports no clock at all.
        Assert.That(path, Is.EqualTo(Path.Combine("sys", "class", "drm", "card0", "device", "tile0", "gt0", "freq0", "cur_freq")));
    }

    [Test]
    public void GetFrequencyAttributePath_TreatsAnUnknownDriverAsI915()
    {
        // The older layout is the safer default: it is what every pre-xe Intel part uses, and a wrong guess
        // costs a missing optional field rather than a wrong reading.
        var path = IntelGpuSysfsPaths.GetFrequencyAttributePath("card0", "device", "something-else");

        Assert.That(path, Is.EqualTo(Path.Combine("card0", "gt_cur_freq_mhz")));
    }

    [Test]
    public void GetMaximumFrequencyAttributePath_UsesThePolicyCapNotTheSiliconCeiling()
    {
        var i915 = IntelGpuSysfsPaths.GetMaximumFrequencyAttributePath("card0", "device", "i915");
        var xe = IntelGpuSysfsPaths.GetMaximumFrequencyAttributePath("card0", "device", "xe");

        // gt_RP0_freq_mhz / rp0_freq are the silicon ceiling. A part held below that by a deliberate power
        // policy is not throttling in any sense worth spinning fans up over, so the cap is the right divisor.
        Assert.Multiple(() =>
        {
            Assert.That(i915, Is.EqualTo(Path.Combine("card0", "gt_max_freq_mhz")));
            Assert.That(xe, Is.EqualTo(Path.Combine("device", "tile0", "gt0", "freq0", "max_freq")));
        });
    }

    [Test]
    public void FrequencyPaths_DifferBetweenCurrentAndMaximum()
    {
        // Guards a copy-paste that would divide the current clock by itself and report a permanent ratio of 1,
        // which reads as "never throttling" and is invisible in every other test.
        foreach (var driver in new[] { "i915", "xe" })
        {
            Assert.That(
                IntelGpuSysfsPaths.GetFrequencyAttributePath("card0", "device", driver),
                Is.Not.EqualTo(IntelGpuSysfsPaths.GetMaximumFrequencyAttributePath("card0", "device", driver)),
                driver);
        }
    }

    /// <summary>
    /// The throttle attributes live in different places per driver, and the filename prefix differs too —
    /// i915 spells them throttle_reason_* in the GT directory, xe spells them reason_* in freq0/throttle/.
    /// Pairing the wrong prefix with the right directory reads nothing and looks like "not throttling".
    /// </summary>
    [Test]
    public void GetThrottleReasonLocation_UsesTheRightDirectoryAndPrefixPerDriver()
    {
        var (i915Directory, i915Prefix) = IntelGpuSysfsPaths.GetThrottleReasonLocation("/sys/class/drm/card0", "/sys/class/drm/card0/device", "i915");
        var (xeDirectory, xePrefix) = IntelGpuSysfsPaths.GetThrottleReasonLocation("/sys/class/drm/card0", "/sys/class/drm/card0/device", "xe");

        Assert.Multiple(() =>
        {
            // Compared against Path.Combine rather than a literal so the assertion is about the path SHAPE,
            // not about which separator the host platform happens to use.
            Assert.That(i915Directory, Is.EqualTo(Path.Combine("/sys/class/drm/card0", "gt", "gt0")));
            Assert.That(i915Prefix, Is.EqualTo("throttle_reason_"));
            Assert.That(xeDirectory, Is.EqualTo(Path.Combine("/sys/class/drm/card0/device", "tile0", "gt0", "freq0", "throttle")));
            Assert.That(xePrefix, Is.EqualTo("reason_"));
        });
    }
}
