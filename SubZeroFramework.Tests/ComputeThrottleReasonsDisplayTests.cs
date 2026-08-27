using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers the wording shown for GPU throttle state on the Device Capabilities page.
/// </summary>
[TestFixture]
public class ComputeThrottleReasonsDisplayTests
{
    [Test]
    public void Describe_SeparatesUnknownFromNotThrottled()
    {
        // The distinction that matters: "--" means the device could not be asked, the other means it answered
        // and is running free. Collapsing them would turn "we do not know" into a reassurance.
        Assert.Multiple(() =>
        {
            Assert.That(ComputeThrottleReasonsDisplay.Describe(null), Is.EqualTo(ComputeThrottleReasonsDisplay.Unknown));
            Assert.That(ComputeThrottleReasonsDisplay.Describe(ComputeThrottleReasons.None), Is.EqualTo(ComputeThrottleReasonsDisplay.NotThrottled));
        });
    }

    [Test]
    public void Describe_NamesASingleReason()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ComputeThrottleReasonsDisplay.Describe(ComputeThrottleReasons.ThermalLimit), Is.EqualTo("Temperature"));
            Assert.That(ComputeThrottleReasonsDisplay.Describe(ComputeThrottleReasons.PowerLimit), Is.EqualTo("Power limit"));
            Assert.That(ComputeThrottleReasonsDisplay.Describe(ComputeThrottleReasons.ApplicationLimit), Is.EqualTo("Applied limit"));
            Assert.That(ComputeThrottleReasonsDisplay.Describe(ComputeThrottleReasons.Idle), Is.EqualTo("Idle"));
        });
    }

    [Test]
    public void Describe_ListsEveryReasonAndPutsTemperatureFirst()
    {
        // The reference RTX 5070 asserts its power limit permanently, even at idle. Showing only one reason
        // would read "Power limit" forever and hide a thermal limit appearing beside it — the one that
        // actually calls for more airflow.
        var described = ComputeThrottleReasonsDisplay.Describe(
            ComputeThrottleReasons.PowerLimit | ComputeThrottleReasons.ThermalLimit);

        Assert.That(described, Is.EqualTo("Temperature, Power limit"));
    }

    [Test]
    public void Describe_HandlesTheMeasuredIdleStateOfTheReferenceGpu()
    {
        // Exactly what the RTX 5070 reports at idle: 0x4, power limit alone.
        Assert.That(ComputeThrottleReasonsDisplay.Describe(ComputeThrottleReasons.PowerLimit), Is.EqualTo("Power limit"));
    }

    [Test]
    public void Describe_StillReportsThrottlingForBitsThisModelDoesNotName()
    {
        // A bitmask carrying only unrecognised bits still means the device IS throttled. Falling through to
        // "running at full speed" would contradict the source.
        var unnamed = (ComputeThrottleReasons)(1 << 20);

        Assert.That(ComputeThrottleReasonsDisplay.Describe(unnamed), Is.EqualTo("Other"));
    }

    [Test]
    public void Describe_CombinesAllKnownReasons()
    {
        var all = ComputeThrottleReasons.ThermalLimit
            | ComputeThrottleReasons.PowerLimit
            | ComputeThrottleReasons.ApplicationLimit
            | ComputeThrottleReasons.Idle
            | ComputeThrottleReasons.Other;

        Assert.That(
            ComputeThrottleReasonsDisplay.Describe(all),
            Is.EqualTo("Temperature, Power limit, Applied limit, Idle, Other"));
    }
}
