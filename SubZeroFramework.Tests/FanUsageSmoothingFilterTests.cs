using NUnit.Framework;

using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FanUsageSmoothingFilterTests
{
    private static readonly TimeSpan HalfLife = TimeSpan.FromSeconds(5);

    [Test]
    public void Ctor_WithNonPositiveHalfLife_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FanUsageSmoothingFilter(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FanUsageSmoothingFilter(TimeSpan.FromSeconds(-1)));
        });
    }

    [Test]
    public void Current_BeforeAnySample_IsNull()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        Assert.That(filter.Current, Is.Null);
    }

    [Test]
    public void Sample_WithOnlyMissingReadings_StaysNull()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        Assert.Multiple(() =>
        {
            Assert.That(filter.Sample(null, TimeSpan.FromSeconds(1)), Is.Null);
            Assert.That(filter.Sample(double.NaN, TimeSpan.FromSeconds(1)), Is.Null);
        });
    }

    [Test]
    public void Sample_RisingLoad_IsTakenInstantly()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        filter.Sample(0.2d, TimeSpan.Zero);
        var smoothed = filter.Sample(0.9d, TimeSpan.FromSeconds(1));

        // Fast attack: a spike must ramp the boost immediately, before heat reaches the sensors.
        Assert.That(smoothed, Is.EqualTo(0.9d));
    }

    [Test]
    public void Sample_FallingLoad_DecaysWithTheHalfLife()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        filter.Sample(0.8d, TimeSpan.Zero);
        var smoothed = filter.Sample(0d, HalfLife);

        // Slow decay: one half-life after the spike ends, half the smoothed usage remains.
        Assert.That(smoothed, Is.EqualTo(0.4d).Within(1e-9));
    }

    [Test]
    public void Sample_RawBelowDecayedValue_KeepsTheDecayedValue()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        filter.Sample(0.8d, TimeSpan.Zero);
        var smoothed = filter.Sample(0.1d, TimeSpan.FromSeconds(1));

        // After 1s of a 5s half-life, the held value (~0.7) still exceeds the low raw sample.
        Assert.That(smoothed, Is.GreaterThan(0.1d));
    }

    [Test]
    public void Sample_MissingReading_StillDecays()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        filter.Sample(0.8d, TimeSpan.Zero);
        var smoothed = filter.Sample(null, HalfLife);

        // A stalled usage source fades the boost out instead of freezing it at the last reading.
        Assert.That(smoothed, Is.EqualTo(0.4d).Within(1e-9));
    }

    [Test]
    public void Sample_ClampsRawReadingsIntoTheUnitRange()
    {
        var filter = new FanUsageSmoothingFilter(HalfLife);

        Assert.Multiple(() =>
        {
            Assert.That(filter.Sample(1.7d, TimeSpan.Zero), Is.EqualTo(1d));
            Assert.That(new FanUsageSmoothingFilter(HalfLife).Sample(-0.3d, TimeSpan.Zero), Is.Zero);
        });
    }
}
