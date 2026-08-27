using System.Collections.Immutable;

using FrameworkDotnet.Enums;

using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// Whether a saved profile counts as the one currently in effect.
/// </summary>
/// <remarks>
/// This decides what the dashboard claims. Too strict and a profile stops reading as active the moment after
/// it is applied, so the row goes blank and the Modified prompt appears over a machine nobody touched. Too
/// loose and it keeps claiming a profile is in effect after the user has changed a fan by hand — the more
/// damaging direction, because the claim is then used to label every fan card.
/// </remarks>
[TestFixture]
public class FanProfileMatchingTests
{
    [Test]
    public void Matches_WhenEveryFanIsDoingWhatTheProfileAsks()
    {
        var profile = Profile(
            Entry(0, FanControlMode.Auto),
            Entry(1, FanControlMode.Manual, duty: 60d));

        var states = States(
            State(0, FanControlMode.Auto),
            State(1, FanControlMode.Manual, duty: 60d));

        Assert.That(profile.Matches(states), Is.True);
    }

    [Test]
    public void DoesNotMatch_WhenAnyOneFanDiffers()
    {
        var profile = Profile(
            Entry(0, FanControlMode.Auto),
            Entry(1, FanControlMode.Manual, duty: 60d));

        // The second fan alone is wrong. A profile is a statement about the whole machine, so one fan out of
        // place is enough to make it false — an "almost" match is what the Modified prompt exists for.
        var states = States(
            State(0, FanControlMode.Auto),
            State(1, FanControlMode.Max));

        Assert.That(profile.Matches(states), Is.False);
    }

    /// <summary>
    /// A duty that came back a whole percent off still counts.
    /// </summary>
    /// <remarks>
    /// The controller reports duty rounded to whole percent, so a profile saved at 62.4% reads back as 62%.
    /// Exact comparison would leave every profile reading as modified the instant after it was applied.
    /// </remarks>
    [TestCase(62.4d, 62d, true)]
    [TestCase(60d, 60d, true)]
    [TestCase(60d, 65d, false)]
    public void ManualDuty_TolerateRounding_ButNotARealDifference(double saved, double reported, bool expected)
    {
        var profile = Profile(Entry(0, FanControlMode.Manual, duty: saved));
        var states = States(State(0, FanControlMode.Manual, duty: reported));

        Assert.That(profile.Matches(states), Is.EqualTo(expected));
    }

    /// <summary>
    /// The same allowance for an adaptive target, which survives a round trip through Fahrenheit.
    /// </summary>
    /// <remarks>
    /// A target set in Fahrenheit converts to Celsius and back a fraction of a degree off. Half a degree of
    /// slack absorbs that; a whole degree of difference is a different setting and must not.
    /// </remarks>
    [TestCase(78d, 78.2d, true)]
    [TestCase(78d, 72d, false)]
    public void AdaptiveTarget_ToleratesAUnitRoundTrip(double saved, double reported, bool expected)
    {
        var profile = Profile(Entry(0, FanControlMode.Adaptive, adaptiveTarget: saved));
        var states = States(State(0, FanControlMode.Adaptive, adaptiveTarget: reported));

        Assert.That(profile.Matches(states), Is.EqualTo(expected));
    }

    [Test]
    public void AdaptiveDoesNotMatch_WhenTheFanIsInADifferentModeAtTheSameTemperature()
    {
        // The target is irrelevant if the loop is not running. Comparing only the number would call a fan on
        // a fixed duty "Adaptive 78°C" purely because that is what it would use if it were switched on.
        var profile = Profile(Entry(0, FanControlMode.Adaptive, adaptiveTarget: 78d));
        var states = States(State(0, FanControlMode.Manual, duty: 50d, adaptiveTarget: 78d));

        Assert.That(profile.Matches(states), Is.False);
    }

    [Test]
    public void CurveMatchesOnTheSlot_NotMerelyOnBeingACurve()
    {
        var profile = Profile(Entry(0, FanControlMode.CustomCurve, curveSlot: 2));

        Assert.Multiple(() =>
        {
            Assert.That(profile.Matches(States(State(0, FanControlMode.CustomCurve, curveSlot: 2))), Is.True);
            Assert.That(profile.Matches(States(State(0, FanControlMode.CustomCurve, curveSlot: 1))), Is.False);
        });
    }

    /// <summary>
    /// A fan that has gone away is skipped rather than counted as a mismatch.
    /// </summary>
    /// <remarks>
    /// A profile written while an expansion module was attached should still be usable once it is removed.
    /// Treating the missing fan as a failure would leave the user unable to select any of their profiles on a
    /// machine that is working perfectly well.
    /// </remarks>
    [Test]
    public void IgnoresFansTheMachineNoLongerHas()
    {
        var profile = Profile(
            Entry(0, FanControlMode.Auto),
            Entry(7, FanControlMode.Max));

        Assert.That(profile.Matches(States(State(0, FanControlMode.Auto))), Is.True);
    }

    /// <summary>
    /// ...but a profile whose fans have ALL gone away matches nothing.
    /// </summary>
    /// <remarks>
    /// Skipping every entry leaves the question empty, and "all zero of the entries matched" would light up
    /// every stale profile at once — several cards claiming to be active simultaneously.
    /// </remarks>
    [Test]
    public void DoesNotMatch_WhenNoneOfItsFansExistAnyMore()
    {
        var profile = Profile(Entry(6, FanControlMode.Auto), Entry(7, FanControlMode.Auto));

        Assert.That(profile.Matches(States(State(0, FanControlMode.Auto))), Is.False);
    }

    [Test]
    public void AnEmptyProfileMatchesNothing()
    {
        var profile = new FanProfile { Id = "empty", Name = "Empty" };

        Assert.That(profile.Matches(States(State(0, FanControlMode.Auto))), Is.False);
    }

    private static FanProfile Profile(params FanProfileEntry[] entries) => new()
    {
        Id = "test",
        Name = "Test",
        Fans = [.. entries],
    };

    private static FanProfileEntry Entry(
        int fanIndex,
        FanControlMode mode,
        double duty = 0d,
        int curveSlot = 0,
        double adaptiveTarget = AdaptiveFanSettings.DefaultTargetCelsius)
        => new()
        {
            FanIndex = fanIndex,
            Mode = mode,
            DutyPercent = duty,
            CurveSlot = curveSlot,
            AdaptiveTargetCelsius = adaptiveTarget,
        };

    private static FanControlStateSnapshot State(
        int fanIndex,
        FanControlMode mode,
        double? duty = null,
        int curveSlot = 0,
        double adaptiveTarget = AdaptiveFanSettings.DefaultTargetCelsius)
        => new()
        {
            FanIndex = fanIndex,
            DisplayName = $"Fan {fanIndex}",
            Mode = mode,
            LastDutyPercent = duty,
            ActiveCurveSlot = curveSlot,
            AdaptiveSettings = AdaptiveFanSettings.Default with { TargetTemperatureCelsius = adaptiveTarget },
            IsAvailable = true,
        };

    private static Dictionary<int, FanControlStateSnapshot> States(params FanControlStateSnapshot[] states)
        => states.ToDictionary(static state => state.FanIndex);
}
