using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

[TestFixture]
public class CustomCurveSnapshotTests
{
    private static CustomCurveSnapshot SelfDriven(
        TemperatureAggregationMode aggregation = TemperatureAggregationMode.Maximum,
        int[]? sensors = null,
        (int Temperature, double Duty)[]? points = null)
        => new(aggregation, sensors ?? [0], points ?? [(40, 30d), (60, 60d), (80, 100d)], FollowFanIndex: null);

    [Test]
    public void Matches_IdenticalSelfDrivenDrafts_ReturnsTrue()
    {
        Assert.That(SelfDriven().Matches(SelfDriven()), Is.True);
    }

    [Test]
    public void Matches_SensorOrderDiffers_StillMatches()
    {
        var a = SelfDriven(sensors: [0, 3, 5]);
        var b = SelfDriven(sensors: [5, 0, 3]);

        Assert.That(a.Matches(b), Is.True, "The sensor set is order-independent.");
    }

    [Test]
    public void Matches_CurvePointOrderDiffers_StillMatches()
    {
        var a = SelfDriven(points: [(40, 30d), (60, 60d), (80, 100d)]);
        var b = SelfDriven(points: [(80, 100d), (40, 30d), (60, 60d)]);

        Assert.That(a.Matches(b), Is.True, "Curve points are compared after ordering by temperature.");
    }

    [Test]
    public void Matches_DutyWithinTolerance_TreatedAsEqual()
    {
        var a = SelfDriven(points: [(40, 30d), (60, 60d)]);
        var b = SelfDriven(points: [(40, 30.005d), (60, 60d)]);

        Assert.That(a.Matches(b), Is.True, "Duty differences under 0.01% are not a change.");
    }

    [Test]
    public void Matches_DutyBeyondTolerance_IsDifferent()
    {
        var a = SelfDriven(points: [(40, 30d), (60, 60d)]);
        var b = SelfDriven(points: [(40, 31d), (60, 60d)]);

        Assert.That(a.Matches(b), Is.False);
    }

    [Test]
    public void Matches_DifferentAggregation_IsDifferent()
    {
        var a = SelfDriven(aggregation: TemperatureAggregationMode.Maximum);
        var b = SelfDriven(aggregation: TemperatureAggregationMode.Average);

        Assert.That(a.Matches(b), Is.False);
    }

    [Test]
    public void Matches_DifferentSensorSet_IsDifferent()
    {
        Assert.That(SelfDriven(sensors: [0, 1]).Matches(SelfDriven(sensors: [0, 2])), Is.False);
    }

    [Test]
    public void Matches_DifferentPointCount_IsDifferent()
    {
        var a = SelfDriven(points: [(40, 30d), (60, 60d)]);
        var b = SelfDriven(points: [(40, 30d), (60, 60d), (80, 100d)]);

        Assert.That(a.Matches(b), Is.False);
    }

    [Test]
    public void Matches_SameFollowTarget_IgnoresPointsAndSensors()
    {
        CustomCurveSnapshot a = new(TemperatureAggregationMode.Maximum, [0, 1], [(40, 30d)], FollowFanIndex: 1);
        CustomCurveSnapshot b = new(TemperatureAggregationMode.Average, [9], [(10, 99d)], FollowFanIndex: 1);

        Assert.That(a.Matches(b), Is.True, "A follow slot is defined only by its target fan.");
    }

    [Test]
    public void Matches_DifferentFollowTarget_IsDifferent()
    {
        CustomCurveSnapshot a = new(TemperatureAggregationMode.Maximum, [0], [(40, 30d)], FollowFanIndex: 1);
        CustomCurveSnapshot b = new(TemperatureAggregationMode.Maximum, [0], [(40, 30d)], FollowFanIndex: 2);

        Assert.That(a.Matches(b), Is.False);
    }

    [Test]
    public void Matches_FollowVersusSelfDriven_IsDifferent()
    {
        CustomCurveSnapshot follow = new(TemperatureAggregationMode.Maximum, [0], [(40, 30d)], FollowFanIndex: 1);

        Assert.That(follow.Matches(SelfDriven()), Is.False);
        Assert.That(SelfDriven().Matches(follow), Is.False);
    }

    [Test]
    public void InterpolateDuty_AtAnExactPoint_ReturnsThatDuty()
    {
        Assert.That(SelfDriven().InterpolateDuty(40), Is.EqualTo(30d).Within(0.0001));
    }

    [Test]
    public void InterpolateDuty_BetweenPoints_IsLinear()
    {
        // Halfway between (40,30) and (60,60).
        Assert.That(SelfDriven().InterpolateDuty(50), Is.EqualTo(45d).Within(0.0001));
    }

    [Test]
    public void InterpolateDuty_BelowFirstPoint_RampsFromZeroAnchor()
    {
        // Between the implicit (0,0) anchor and (40,30): 20/40 * 30 = 15.
        Assert.That(SelfDriven().InterpolateDuty(20), Is.EqualTo(15d).Within(0.0001));
    }

    [Test]
    public void InterpolateDuty_AtZero_IsZero()
    {
        Assert.That(SelfDriven().InterpolateDuty(0), Is.EqualTo(0d).Within(0.0001));
    }

    [Test]
    public void InterpolateDuty_AboveLastPoint_HoldsFlat()
    {
        // Last point (80,100); the (130,100) anchor holds it flat above 80.
        Assert.That(SelfDriven().InterpolateDuty(95), Is.EqualTo(100d).Within(0.0001));
    }

    [Test]
    public void InterpolateDuty_EmptyCurve_RampsZeroToHundredOverZeroTo130()
    {
        CustomCurveSnapshot empty = new(TemperatureAggregationMode.Maximum, [0], [], FollowFanIndex: null);

        // Anchored series is (0,0)->(130,100): at 65°C that is 50%.
        Assert.That(empty.InterpolateDuty(65), Is.EqualTo(50d).Within(0.0001));
    }

    [Test]
    public void InterpolateDuty_ClampedTo0To100()
    {
        var snapshot = SelfDriven(points: [(40, 30d), (80, 100d)]);

        Assert.That(snapshot.InterpolateDuty(-10), Is.InRange(0d, 100d));
        Assert.That(snapshot.InterpolateDuty(200), Is.InRange(0d, 100d));
    }
}
