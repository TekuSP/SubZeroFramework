using System.Collections.Immutable;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for gain scheduling — the measured shape of a fan's cooling, and what the tuning rule does with it.
/// </summary>
/// <remarks>
/// The numbers here matter more than most: the tuning rule DIVIDES by the process gain, so a gain that comes
/// out too small produces a controller gain that is too large, and the failure mode is an audibly hunting fan
/// rather than a wrong number in a log.
/// </remarks>
[TestFixture]
public class FanGainCurveTests
{
    /// <summary>
    /// A realistically nonlinear chassis: steep at the bottom, flattening toward the top.
    /// </summary>
    /// <remarks>
    /// 22→40 gives 0.5 °C per point; 80→100 gives 0.1. A five-to-one spread across the range is what makes a
    /// single averaged gain wrong at both ends.
    /// </remarks>
    private static FanGainCurve Realistic => new()
    {
        Points =
        [
            new FanGainPoint(22d, 85d),
            new FanGainPoint(40d, 76d),
            new FanGainPoint(60d, 70d),
            new FanGainPoint(80d, 66d),
            new FanGainPoint(100d, 64d),
        ],
    };

    [Test]
    public void GainAt_IsLargerAtLowDutyThanHigh()
    {
        var curve = Realistic;

        var low = curve.GainAt(30d, fallbackGain: 0.42d);
        var high = curve.GainAt(90d, fallbackGain: 0.42d);

        // The whole reason the curve exists. If these came out equal, scheduling would be busywork.
        Assert.Multiple(() =>
        {
            Assert.That(low, Is.EqualTo(0.5d).Within(0.01d));
            Assert.That(high, Is.EqualTo(0.1d).Within(0.01d));
            Assert.That(low, Is.GreaterThan(high * 3d));
        });
    }

    [Test]
    public void GainAt_UsesTheNearestSegment_OutsideTheMeasuredRange()
    {
        var curve = Realistic;

        // Below the lowest measured point and above the highest. Extrapolating a decaying curve outward
        // would drive the gain toward zero, and the tuning rule divides by it — a near-zero gain becomes an
        // enormous controller gain derived from a region nobody measured.
        Assert.Multiple(() =>
        {
            Assert.That(curve.GainAt(5d, fallbackGain: 0.42d), Is.EqualTo(0.5d).Within(0.01d));
            Assert.That(curve.GainAt(120d, fallbackGain: 0.42d), Is.EqualTo(0.1d).Within(0.01d));
        });
    }

    [Test]
    public void GainAt_FallsBackToTheAveragedGain_WithoutEnoughPoints()
    {
        // Two points describe a straight line, which is the assumption the curve exists to replace.
        var curve = new FanGainCurve { Points = [new FanGainPoint(22d, 85d), new FanGainPoint(100d, 64d)] };

        Assert.Multiple(() =>
        {
            Assert.That(curve.IsUsable, Is.False);
            Assert.That(curve.GainAt(50d, fallbackGain: 0.42d), Is.EqualTo(0.42d));
            Assert.That(FanGainCurve.None.GainAt(50d, fallbackGain: 0.42d), Is.EqualTo(0.42d));
        });
    }

    [Test]
    public void GainAt_FallsBack_WhenASegmentShowsNoChange()
    {
        // A flat segment is measurement noise, not a fan that does nothing — and a zero gain would make the
        // tuning rule divide by zero.
        var curve = new FanGainCurve
        {
            Points = [new FanGainPoint(20d, 80d), new FanGainPoint(50d, 80d), new FanGainPoint(100d, 70d)],
        };

        Assert.That(curve.GainAt(30d, fallbackGain: 0.42d), Is.EqualTo(0.42d));
    }

    [Test]
    public void ScheduledGains_AreGentlerAtLowDutyThanAnAverageWouldGive()
    {
        var curve = Realistic;
        const double averaged = 0.27d;

        var atLowDuty = AdaptivePidTuning.Compute(
            processGainCelsiusPerPercent: curve.GainAt(30d, averaged),
            timeConstantSeconds: 26d,
            deadTimeSeconds: 4d);

        var withAverage = AdaptivePidTuning.Compute(
            processGainCelsiusPerPercent: averaged,
            timeConstantSeconds: 26d,
            deadTimeSeconds: 4d);

        // Kc = τ / (K(λ+L)): the real low-duty gain is nearly twice the average, so the scheduled controller
        // gain is nearly half. Without scheduling the loop runs that much hotter than designed, precisely
        // where the fan is quiet enough for the user to hear it hunt.
        Assert.That(atLowDuty.ProportionalGain, Is.LessThan(withAverage.ProportionalGain * 0.7d));
    }

    [Test]
    public void Scaled_KeepsTheShapeWhileMovingTheLevel()
    {
        var curve = Realistic;

        // Cooling that has become 20% less effective: the spread above the cool end grows, the shape does not
        // change. What ongoing learning tracks is drift — dust, dried paste — not a different chassis.
        var scaled = curve.Scaled(1.2d);

        var originalRatio = curve.GainAt(30d, 0.42d) / curve.GainAt(90d, 0.42d);
        var scaledRatio = scaled.GainAt(30d, 0.42d) / scaled.GainAt(90d, 0.42d);

        Assert.Multiple(() =>
        {
            Assert.That(scaledRatio, Is.EqualTo(originalRatio).Within(0.01d), "the shape changed");
            Assert.That(scaled.GainAt(30d, 0.42d), Is.EqualTo(curve.GainAt(30d, 0.42d) * 1.2d).Within(0.01d));
        });
    }

    [Test]
    public void Scaled_IsInertWithoutAUsableCurve()
    {
        Assert.That(FanGainCurve.None.Scaled(1.5d).Points, Is.EqualTo(ImmutableArray<FanGainPoint>.Empty));
    }
}
