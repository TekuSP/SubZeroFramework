using NUnit.Framework;

using SubZeroFramework.Tests.Simulation;

namespace SubZeroFramework.Tests;

/// <summary>
/// Verifies the simulated fan behaves like a fan before the calibration procedure is written against it —
/// in particular the start/stop hysteresis and the non-linear duty→RPM curve, which are the two things the
/// first calibration steps exist to measure.
/// </summary>
[TestFixture]
public class FanSimulatorTests
{
    private static readonly TimeSpan TimeStep = TimeSpan.FromSeconds(0.25);

    private static FanSimulator CreateFan(FanSimulatorParameters? parameters = null)
        => new(parameters ?? new FanSimulatorParameters(), TimeStep);

    private static double RunToSteadyState(FanSimulator fan, double dutyPercent)
    {
        for (var step = 0; step < 400; step++)
        {
            fan.AdvanceWithDuty(dutyPercent);
        }

        return fan.CurrentRpm;
    }

    [Test]
    public void SteadyStateRpmForDuty_SpansMinimumSpinToMaximum()
    {
        var parameters = new FanSimulatorParameters();
        var fan = CreateFan(parameters);

        Assert.Multiple(() =>
        {
            Assert.That(fan.SteadyStateRpmForDuty(parameters.StallDutyPercent), Is.EqualTo(parameters.MinimumSpinRpm).Within(1e-9));
            Assert.That(fan.SteadyStateRpmForDuty(100d), Is.EqualTo(parameters.MaximumSpeedRpm).Within(1e-9));
            Assert.That(fan.SteadyStateRpmForDuty(parameters.StallDutyPercent - 0.1d), Is.Zero);
        });
    }

    [Test]
    public void SteadyStateRpmForDuty_IsNonLinear()
    {
        // If this were linear there would be nothing worth measuring, and a controller could assume duty is
        // airflow. The whole duty→RPM calibration step exists because it is not.
        var fan = CreateFan();
        var parameters = new FanSimulatorParameters();

        var midpointDuty = (parameters.StallDutyPercent + 100d) / 2d;
        var linearMidpoint = (parameters.MinimumSpinRpm + parameters.MaximumSpeedRpm) / 2d;

        Assert.That(fan.SteadyStateRpmForDuty(midpointDuty), Is.LessThan(linearMidpoint - 100d));
    }

    [Test]
    public void AdvanceWithDuty_StoppedFanDoesNotStartBelowTheStartThreshold()
    {
        var parameters = new FanSimulatorParameters();
        var fan = CreateFan(parameters);

        // Between stall and start: enough to keep a turning fan alive, not enough to break it free from rest.
        var dutyBetweenThresholds = (parameters.StallDutyPercent + parameters.StartDutyPercent) / 2d;
        RunToSteadyState(fan, dutyBetweenThresholds);

        Assert.That(fan.IsStalled, Is.True);
    }

    [Test]
    public void AdvanceWithDuty_TurningFanKeepsTurningBelowTheStartThreshold()
    {
        var parameters = new FanSimulatorParameters();
        var fan = CreateFan(parameters);
        fan.Settle(60d);

        var dutyBetweenThresholds = (parameters.StallDutyPercent + parameters.StartDutyPercent) / 2d;
        var settled = RunToSteadyState(fan, dutyBetweenThresholds);

        Assert.Multiple(() =>
        {
            Assert.That(fan.IsStalled, Is.False, "Hysteresis means a running fan survives a duty that could not have started it.");
            Assert.That(settled, Is.GreaterThan(parameters.MinimumSpinRpm));
        });
    }

    [Test]
    public void AdvanceWithDuty_TurningFanStopsBelowTheStallThreshold()
    {
        var parameters = new FanSimulatorParameters();
        var fan = CreateFan(parameters);
        fan.Settle(60d);

        RunToSteadyState(fan, parameters.StallDutyPercent - 1d);

        Assert.That(fan.IsStalled, Is.True);
    }

    [Test]
    public void AdvanceWithCommandedRpm_ReachesTheCommandedSpeedWhenTheEcTracksIt()
    {
        var fan = CreateFan();
        fan.Settle(50d);

        const double CommandedRpm = 4200d;
        for (var step = 0; step < 400; step++)
        {
            fan.AdvanceWithCommandedRpm(CommandedRpm);
        }

        Assert.That(fan.CurrentRpm, Is.EqualTo(CommandedRpm).Within(1d));
    }

    [Test]
    public void AdvanceWithCommandedRpm_MissesWidelyWhenTheEcDoesNotTrackRpm()
    {
        // The duty-fallback case. The tracking check in calibration must be able to see this difference,
        // so the error has to be large enough to be unambiguous rather than a rounding artefact.
        var fan = CreateFan(new FanSimulatorParameters { TracksCommandedRpm = false });
        fan.Settle(50d);

        const double CommandedRpm = 4200d;
        for (var step = 0; step < 400; step++)
        {
            fan.AdvanceWithCommandedRpm(CommandedRpm);
        }

        Assert.That(Math.Abs(fan.CurrentRpm - CommandedRpm), Is.GreaterThan(350d));
    }

    [Test]
    public void AdvanceWithDuty_ApproachesTheNewSpeedGraduallyRatherThanJumping()
    {
        var parameters = new FanSimulatorParameters();
        var fan = CreateFan(parameters);
        fan.Settle(30d);
        var startingRpm = fan.CurrentRpm;

        fan.AdvanceWithDuty(100d);

        Assert.Multiple(() =>
        {
            Assert.That(fan.CurrentRpm, Is.GreaterThan(startingRpm), "The rotor must respond.");
            Assert.That(fan.CurrentRpm, Is.LessThan(parameters.MaximumSpeedRpm * 0.5d), "But it must not arrive in a single 250 ms step.");
        });
    }
}
