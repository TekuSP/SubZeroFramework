using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The duty→speed estimate behind every RPM the Adaptive card shows.
/// </summary>
/// <remarks>
/// Display only — the fan is commanded in duty percent. It is covered anyway because it replaced a straight
/// <c>duty ÷ 100 × max</c> that was wrong across the whole lower half of the range, and because a number a
/// user compares against their tachometer has to be defensible.
/// </remarks>
[TestFixture]
public class FanCalibrationSnapshotEstimateRpmTests
{
    /// <summary>A fan that idles at 1,200 RPM on 20% duty and tops out at 6,000.</summary>
    private static FanCalibrationSnapshot Measured() => new()
    {
        State = FanCalibrationState.Ok,
        MinimumSpinDutyPercent = 20d,
        MinimumSpinRpm = 1_200d,
        MaximumRpm = 6_000d,
    };

    [Test]
    public void EstimateRpm_AtFullDuty_IsTheMeasuredMaximum()
        => Assert.That(Measured().EstimateRpm(100d), Is.EqualTo(6_000d).Within(1e-9d));

    [Test]
    public void EstimateRpm_AtTheMinimumSpinDuty_IsTheMeasuredMinimumSpinSpeed()
        => Assert.That(Measured().EstimateRpm(20d), Is.EqualTo(1_200d).Within(1e-9d));

    /// <summary>
    /// The reason this exists. Halfway up the duty range is NOT half the maximum speed, because the fan is
    /// already turning at 1,200 RPM before the range even opens.
    /// </summary>
    [Test]
    public void EstimateRpm_MidRange_AccountsForTheMinimumSpinOffset()
    {
        // 60% is halfway between the 20% floor and 100%, so halfway between 1,200 and 6,000.
        var estimate = Measured().EstimateRpm(60d);

        Assert.Multiple(() =>
        {
            Assert.That(estimate, Is.EqualTo(3_600d).Within(1e-9d));
            Assert.That(estimate, Is.Not.EqualTo(3_000d).Within(1d), "that would be the old duty-fraction-of-maximum answer");
        });
    }

    [Test]
    public void EstimateRpm_BelowTheMinimumSpinDuty_ReportsStopped()
        => Assert.That(Measured().EstimateRpm(10d), Is.EqualTo(0d).Within(1e-9d));

    [Test]
    public void EstimateRpm_AtZeroDuty_ReportsStopped()
        => Assert.That(Measured().EstimateRpm(0d), Is.EqualTo(0d).Within(1e-9d));

    /// <summary>
    /// Nothing measured means nothing to say. Naming a speed here would put a fabricated number next to a
    /// real tachometer reading, which is worse than a dash.
    /// </summary>
    [Test]
    public void EstimateRpm_WithoutAMeasuredMaximum_IsNull()
        => Assert.That((Measured() with { MaximumRpm = 0d }).EstimateRpm(80d), Is.Null);

    /// <summary>
    /// A fan whose stall walk never found a floor leaves no lower anchor, so the estimate falls back to
    /// scaling the maximum rather than dividing by a zero-width range.
    /// </summary>
    [Test]
    public void EstimateRpm_WhenTheMinimumSpinDutyIsFullDuty_StillProducesASpeed()
    {
        var snapshot = Measured() with { MinimumSpinDutyPercent = 100d };

        Assert.That(snapshot.EstimateRpm(100d), Is.EqualTo(6_000d).Within(1e-9d));
    }

    [Test]
    public void EstimateRpm_IsMonotonic_AcrossTheDutyRange()
    {
        var snapshot = Measured();
        var previous = -1d;

        for (var duty = 0d; duty <= 100d; duty += 5d)
        {
            var estimate = snapshot.EstimateRpm(duty);
            Assert.That(estimate, Is.Not.Null);
            Assert.That(estimate!.Value, Is.GreaterThanOrEqualTo(previous), $"speed fell going from below {duty}% to {duty}%");
            previous = estimate.Value;
        }
    }
}
