using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the controller actually USING the measured gain curve, not merely storing it.
/// </summary>
/// <remarks>
/// Separate from <see cref="FanGainCurveTests"/>, which proves the curve and the tuning rule are each right
/// on their own. Both can be correct while nothing connects them — and a calibration that spends minutes
/// measuring a curve the loop ignores is worse than one that never measured it, because it looks like it
/// worked.
/// </remarks>
[TestFixture]
public class AdaptiveGainSchedulingTests
{
    /// <summary>Steep at low duty, flat at high — a realistic chassis, and the reason scheduling exists.</summary>
    private static FanGainCurve NonlinearCurve => new()
    {
        Points =
        [
            new FanGainPoint(20d, 88d),
            new FanGainPoint(40d, 78d),
            new FanGainPoint(60d, 72d),
            new FanGainPoint(80d, 68d),
            new FanGainPoint(100d, 66d),
        ],
    };

    private static FanCalibrationSnapshot Calibration(FanGainCurve curve) => new()
    {
        State = FanCalibrationState.Ok,
        CalibratedAt = DateTimeOffset.UnixEpoch,
        ProcessGainCelsiusPerPercent = 0.275d,
        TimeConstantSeconds = 26d,
        DeadTimeSeconds = 4d,
        MinimumSpinDutyPercent = 20d,
        MinimumSpinRpm = 800d,
        MaximumRpm = 5000d,

        // Zero, so the whole response below comes from the feedback path rather than being dominated by a
        // feed-forward term that scheduling does not touch.
        FeedForwardDutyPerWatt = 0d,
        GainCurve = curve,
    };

    private static AdaptiveFanSettings Settings => new() { TargetTemperatureCelsius = 78d };

    [Test]
    public void Controller_RespondsMoreGently_AtLowDutyThanAnAveragedGainWould()
    {
        var scheduled = ResponseToStep(Calibration(NonlinearCurve));
        var averaged = ResponseToStep(Calibration(FanGainCurve.None));

        // At the fan's floor the real cooling per duty point is roughly double the range average, so the
        // tuning rule — which divides by it — must produce a correspondingly gentler controller. Without
        // scheduling the loop runs that much hotter than designed, exactly where the fan is quiet enough for
        // the user to hear it hunt.
        Assert.That(
            scheduled,
            Is.LessThan(averaged * 0.8d),
            $"scheduled duty {scheduled:0.##} was not meaningfully gentler than the averaged {averaged:0.##}.");
    }

    [Test]
    public void Controller_IgnoresACurveWithTooFewPoints()
    {
        // Two points describe the straight line the curve exists to replace, so the loop must behave exactly
        // as it did before any curve was measured.
        var sparse = new FanGainCurve { Points = [new FanGainPoint(20d, 88d), new FanGainPoint(100d, 66d)] };

        var withSparse = ResponseToStep(Calibration(sparse));
        var withNone = ResponseToStep(Calibration(FanGainCurve.None));

        Assert.That(withSparse, Is.EqualTo(withNone).Within(0.001d));
    }

    /// <summary>
    /// Drives the controller from rest with a fixed error and returns the duty it asks for.
    /// </summary>
    /// <remarks>
    /// One tick from a fresh controller, so the integrator has not accumulated and the answer is dominated by
    /// the proportional gain — which is the thing scheduling changes.
    /// </remarks>
    private static double ResponseToStep(FanCalibrationSnapshot calibration)
    {
        var controller = new AdaptiveFanController();

        var decision = controller.Evaluate(
            calibration,
            Settings,

            // Ten degrees over target, with no power reading so feed-forward contributes nothing.
            drivingTemperatureCelsius: Settings.TargetTemperatureCelsius + 10d,
            controlTelemetry: ControlTelemetrySample.Unavailable,
            elapsed: TimeSpan.FromSeconds(1),
            timestamp: DateTimeOffset.UnixEpoch);

        return decision.ProportionalIntegralDutyPercent;
    }
}
