using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The rule bounding the Adaptive target by the firmware's own warning points.
/// </summary>
/// <remarks>
/// A target above where the firmware acts is one the machine will never be left holding — the loop settles
/// there and the firmware intervenes, and the resulting fan behaviour reads as this app misbehaving.
/// </remarks>
[TestFixture]
public class AdaptiveTargetCeilingTests
{
    [Test]
    public void ResolveTargetCeilingCelsius_WithNoThresholds_KeepsTheOfferedMaximum()
        => Assert.That(
            AdaptiveFanSettings.ResolveTargetCeilingCelsius([]),
            Is.EqualTo(AdaptiveFanSettings.MaximumTargetCelsius).Within(1e-9d));

    [Test]
    public void ResolveTargetCeilingCelsius_WithOneThreshold_IsThatThreshold()
        => Assert.That(AdaptiveFanSettings.ResolveTargetCeilingCelsius([88d]), Is.EqualTo(88d).Within(1e-9d));

    /// <summary>
    /// The fan holds every sensor it is driven by, so the FIRST to complain is the one that binds. Taking the
    /// highest would let the target sit above a limit another sensor is already acting on.
    /// </summary>
    [Test]
    public void ResolveTargetCeilingCelsius_TakesTheLowestAcrossSensors()
        => Assert.That(AdaptiveFanSettings.ResolveTargetCeilingCelsius([92d, 84d, 90d]), Is.EqualTo(84d).Within(1e-9d));

    /// <summary>
    /// A sensor warning below the coolest offered target would otherwise collapse the slider to a single
    /// point, leaving the user unable to move it at all.
    /// </summary>
    [Test]
    public void ResolveTargetCeilingCelsius_ClampsUpToTheOfferedMinimum()
        => Assert.That(
            AdaptiveFanSettings.ResolveTargetCeilingCelsius([45d]),
            Is.EqualTo(AdaptiveFanSettings.MinimumTargetCelsius).Within(1e-9d));

    /// <summary>A firmware reporting a threshold above the app's own range must not widen the slider.</summary>
    [Test]
    public void ResolveTargetCeilingCelsius_ClampsDownToTheOfferedMaximum()
        => Assert.That(
            AdaptiveFanSettings.ResolveTargetCeilingCelsius([120d]),
            Is.EqualTo(AdaptiveFanSettings.MaximumTargetCelsius).Within(1e-9d));

    /// <summary>
    /// A sensor reporting NaN must not poison the comparison. Min propagates NaN, which would clamp to the
    /// minimum and silently pin every target to 60 °C.
    /// </summary>
    [Test]
    public void ResolveTargetCeilingCelsius_IgnoresNonFiniteThresholds()
        => Assert.That(
            AdaptiveFanSettings.ResolveTargetCeilingCelsius([double.NaN, 88d]),
            Is.EqualTo(88d).Within(1e-9d));

    [Test]
    public void ResolveTargetCeilingCelsius_WithOnlyNonFiniteThresholds_KeepsTheOfferedMaximum()
        => Assert.That(
            AdaptiveFanSettings.ResolveTargetCeilingCelsius([double.NaN, double.PositiveInfinity]),
            Is.EqualTo(AdaptiveFanSettings.MaximumTargetCelsius).Within(1e-9d));
}
