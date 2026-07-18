using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FanUsageModifierMathTests
{
    [Test]
    public void ComputeBoost_WithDisabledStrength_ReturnsZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FanUsageModifierMath.ComputeBoost(null, 1d), Is.Zero);
            Assert.That(FanUsageModifierMath.ComputeBoost(double.NaN, 1d), Is.Zero);
            Assert.That(FanUsageModifierMath.ComputeBoost(0d, 1d), Is.Zero);
            Assert.That(FanUsageModifierMath.ComputeBoost(-10d, 1d), Is.Zero);
        });
    }

    [Test]
    public void ComputeBoost_WithoutUsageReading_ReturnsZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FanUsageModifierMath.ComputeBoost(40d, null), Is.Zero);
            Assert.That(FanUsageModifierMath.ComputeBoost(40d, double.NaN), Is.Zero);
        });
    }

    [Test]
    public void ComputeBoost_AtIdle_ReturnsZero()
    {
        Assert.That(FanUsageModifierMath.ComputeBoost(40d, 0d), Is.Zero);
    }

    [Test]
    public void ComputeBoost_AtFullUsage_ReturnsExactlyTheStrength()
    {
        Assert.That(FanUsageModifierMath.ComputeBoost(40d, 1d), Is.EqualTo(40d).Within(1e-9));
    }

    [Test]
    public void ComputeBoost_ClampsUsageAboveOne()
    {
        Assert.That(FanUsageModifierMath.ComputeBoost(40d, 1.5d), Is.EqualTo(40d).Within(1e-9));
    }

    [Test]
    public void ComputeBoost_IsExponential_NotLinear()
    {
        // The whole point of the modifier: half usage contributes far less than half the strength,
        // so fans only surge when the CPU is genuinely pinned.
        var atHalfUsage = FanUsageModifierMath.ComputeBoost(40d, 0.5d);

        Assert.Multiple(() =>
        {
            Assert.That(atHalfUsage, Is.GreaterThan(0d));
            Assert.That(atHalfUsage, Is.LessThan(20d * 0.75d));
        });
    }

    [Test]
    public void ComputeBoost_IsMonotonicInUsage()
    {
        var previous = 0d;
        for (var usage = 0.1d; usage <= 1.0d; usage += 0.1d)
        {
            var boost = FanUsageModifierMath.ComputeBoost(40d, usage);
            Assert.That(boost, Is.GreaterThan(previous), $"Boost must keep rising with usage (usage={usage:0.0}).");
            previous = boost;
        }
    }
}
