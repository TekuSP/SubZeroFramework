using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the filter that keeps NVML's mid-power-transition garbage out of the fan controller.
/// </summary>
/// <remarks>
/// The numbers here are measured, not invented. On a Framework 16 with an RTX 5070 whose enforced limit is
/// 100 W, <c>nvmlDeviceGetPowerUsage</c> alternated between ~17.9 W and ~540 W — and returned
/// <c>NVML_SUCCESS</c> for both, so nothing but a plausibility bound can tell them apart. Board power is the
/// adaptive controller's feed-forward input, so an unfiltered 540 W would command maximum fan speed on an
/// idle machine.
/// </remarks>
[TestFixture]
public class NvmlReadingPlausibilityTests
{
    private const double EnforcedLimitWatts = 100d;

    [Test]
    public void IsPlausible_AcceptsTheRealIdleReading()
    {
        Assert.That(NvmlReadingPlausibility.IsPlausible(17.9d, EnforcedLimitWatts), Is.True);
    }

    [Test]
    public void IsPlausible_RejectsTheMeasuredGarbageValue()
    {
        // The exact shape observed: 5.4x the device's own enforced limit, returned with NVML_SUCCESS.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsPlausible(543.1d, EnforcedLimitWatts), Is.False);
            Assert.That(NvmlReadingPlausibility.IsPlausible(536.4d, EnforcedLimitWatts), Is.False);
        });
    }

    [Test]
    public void IsPlausible_AllowsGenuineOvershootAboveTheEnforcedLimit()
    {
        // A board really can exceed its enforced limit briefly. Clipping at exactly 1.0x would hide the very
        // spikes feed-forward exists to react to.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsPlausible(EnforcedLimitWatts, EnforcedLimitWatts), Is.True);
            Assert.That(NvmlReadingPlausibility.IsPlausible(EnforcedLimitWatts * 1.5d, EnforcedLimitWatts), Is.True);
            Assert.That(NvmlReadingPlausibility.IsPlausible(EnforcedLimitWatts * NvmlReadingPlausibility.LimitHeadroomFactor, EnforcedLimitWatts), Is.True);
        });
    }

    [Test]
    public void IsPlausible_RejectsJustPastTheHeadroom()
    {
        var justOver = (EnforcedLimitWatts * NvmlReadingPlausibility.LimitHeadroomFactor) + 0.1d;

        Assert.That(NvmlReadingPlausibility.IsPlausible(justOver, EnforcedLimitWatts), Is.False);
    }

    [Test]
    public void IsPlausible_ScalesWithTheDeviceRatherThanAFixedCeiling()
    {
        // A 450 W desktop card's normal draw must not be rejected just because a laptop module's is not.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsPlausible(430d, 450d), Is.True, "desktop card at load");
            Assert.That(NvmlReadingPlausibility.IsPlausible(430d, EnforcedLimitWatts), Is.False, "same figure on a 100 W module");
        });
    }

    [Test]
    public void IsPlausible_AcceptsAnythingWhenTheLimitIsUnknown()
    {
        // With no limit there is no principled bound. A made-up ceiling would simply be wrong on a different
        // card, so the reading is passed through rather than filtered against a guess.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsPlausible(543.1d, null), Is.True);
            Assert.That(NvmlReadingPlausibility.IsPlausible(17.9d, null), Is.True);
            Assert.That(NvmlReadingPlausibility.IsPlausible(17.9d, 0d), Is.True);
        });
    }

    [Test]
    public void IsPlausible_RejectsValuesThatAreNotMeasurements()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsPlausible(-1d, EnforcedLimitWatts), Is.False);
            Assert.That(NvmlReadingPlausibility.IsPlausible(double.NaN, EnforcedLimitWatts), Is.False);
            Assert.That(NvmlReadingPlausibility.IsPlausible(double.PositiveInfinity, EnforcedLimitWatts), Is.False);
            Assert.That(NvmlReadingPlausibility.IsPlausible(-1d, null), Is.False, "Negative is not a reading even with no limit to compare against.");
        });
    }

    [Test]
    public void IsPlausible_AcceptsZeroForAPoweredDownGpu()
    {
        // Zero is a real reading, not a sentinel — a fully gated dGPU draws nothing.
        Assert.That(NvmlReadingPlausibility.IsPlausible(0d, EnforcedLimitWatts), Is.True);
    }

    [Test]
    public void IsClockPlausible_RejectsAClockAboveTheDeviceMaximum()
    {
        // Measured on the reference RTX 5070: 4575 MHz reported against a stated maximum of 3090 MHz, with
        // NVML_SUCCESS. Unfiltered, the derived clock ratio reads 148% — a healthy boosting GPU, when in fact
        // nothing was measured.
        Assert.That(NvmlReadingPlausibility.IsClockPlausible(4575d, 3090d), Is.False);
    }

    [Test]
    public void IsClockPlausible_AcceptsUpToAndIncludingTheMaximum()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(667d, 3090d), Is.True);
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(3090d, 3090d), Is.True);
        });
    }

    [Test]
    public void IsClockPlausible_AllowsAGpuToBoostPastItsReportedMaximum()
    {
        // GPUs boost, and the maximum NVML reports is not always the highest bin the hardware reaches. An
        // earlier strict comparison would have silently discarded every reading from such a GPU.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(3091d, 3090d), Is.True, "one MHz over is a boost, not garbage");
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(3300d, 3090d), Is.True, "a realistic boost excursion");
        });
    }

    [Test]
    public void IsClockPlausible_StillRejectsTheMeasuredGarbageDespiteTheHeadroom()
    {
        // The headroom must not be so generous that it readmits what it exists to exclude: the observed
        // 4575 MHz against a 3090 MHz maximum is 1.48x, well past the allowance.
        Assert.That(NvmlReadingPlausibility.IsClockPlausible(4575d, 3090d), Is.False);
    }

    [Test]
    public void IsClockPlausible_AcceptsZeroForAnIdleGpu()
    {
        // Zero is a real reading — the reference GPU reported it repeatedly while idle.
        Assert.That(NvmlReadingPlausibility.IsClockPlausible(0d, 3090d), Is.True);
    }

    [Test]
    public void IsClockPlausible_AcceptsAnythingWhenTheMaximumIsUnknown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(4575d, null), Is.True);
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(4575d, 0d), Is.True);
        });
    }

    [Test]
    public void IsClockPlausible_RejectsValuesThatAreNotMeasurements()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(-1d, 3090d), Is.False);
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(double.NaN, 3090d), Is.False);
            Assert.That(NvmlReadingPlausibility.IsClockPlausible(double.PositiveInfinity, 3090d), Is.False);
        });
    }
}
