using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Behavioural cover for the adaptive controller. These assert what the FAN does, not what the code does —
/// every one of them describes a way an automatic fan controller is unpleasant to live with.
/// </summary>
[TestFixture]
public class AdaptiveFanControllerTests
{
    private const double Tick = 1d;

    [Test]
    public void Evaluate_WithoutCalibration_StillDrivesTheFanOnSafeDefaults()
    {
        // The inversion: a fan that has never been calibrated is a WORKING fan, running on the conservative
        // bootstrap while identification gathers evidence. Refusing to drive it was the old design.
        var controller = new AdaptiveFanController();

        var decision = Step(controller, FanCalibrationSnapshot.None, Settings(), temperature: 90d);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsDriven, Is.True);
            Assert.That(decision.DutyPercent, Is.GreaterThan(0d), "10 degrees over target must move the fan even on defaults.");
        });
    }

    [Test]
    public void Evaluate_OnSafeDefaults_IsCalmerThanWhenCalibrated()
    {
        // The bootstrap is deliberately wrong in the directions that REDUCE gain: overestimated K,
        // underestimated tau, overestimated L. So an uncalibrated fan must answer the same error more gently
        // than a calibrated one, never more aggressively.
        var uncalibrated = Step(new AdaptiveFanController(), FanCalibrationSnapshot.None, Settings(), temperature: 88d);
        var calibrated = Step(new AdaptiveFanController(), Calibrated(), Settings(), temperature: 88d);

        Assert.That(
            uncalibrated.ProportionalIntegralDutyPercent,
            Is.LessThan(calibrated.ProportionalIntegralDutyPercent),
            "Defaults must err toward sluggish, never toward aggressive.");
    }

    [Test]
    public void Evaluate_WithNonsenseCalibration_FallsBackToSafeDefaults()
    {
        // A failed run can produce a zero gain. Trusting it would divide by zero; refusing to drive would
        // strand the fan over a number the user cannot see. The bootstrap is the honest third answer.
        var controller = new AdaptiveFanController();
        var broken = Calibrated() with { ProcessGainCelsiusPerPercent = 0d };

        var decision = Step(controller, broken, Settings(), temperature: 90d);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsDriven, Is.True);
            Assert.That(decision.DutyPercent, Is.InRange(0d, 100d));
        });
    }

    [Test]
    public void Evaluate_WhenAboveTarget_AsksForMoreDutyThanWhenBelow()
    {
        var hot = new AdaptiveFanController();
        var cold = new AdaptiveFanController();
        var calibration = Calibrated();

        var hotDecision = Step(hot, calibration, Settings(), temperature: 90d);
        var coldDecision = Step(cold, calibration, Settings(), temperature: 50d);

        Assert.That(hotDecision.DutyPercent, Is.GreaterThan(coldDecision.DutyPercent));
    }

    [Test]
    public void Evaluate_FeedForward_RaisesDutyBeforeTheTemperatureMoves()
    {
        // The entire justification for the feature: at an identical, on-target temperature, a machine whose
        // CPU just started drawing 45 W must already be moving more air than an idle one. A curve cannot do
        // this — it has nothing but the temperature, which has not changed yet.
        var busy = new AdaptiveFanController();
        var idle = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        var busyDecision = Step(busy, calibration, settings, temperature: 78d, packageWatts: 45d);
        var idleDecision = Step(idle, calibration, settings, temperature: 78d, packageWatts: 3d);

        Assert.Multiple(() =>
        {
            Assert.That(busyDecision.FeedForwardDutyPercent, Is.GreaterThan(idleDecision.FeedForwardDutyPercent));
            Assert.That(busyDecision.DutyPercent, Is.GreaterThan(idleDecision.DutyPercent));
            Assert.That(busyDecision.IsFeedForwardUnavailable, Is.False);
        });
    }

    [Test]
    public void Evaluate_WithoutPowerReading_StillControlsAndSaysSo()
    {
        // Windows reports no package power. Adaptive must still work there — on feedback alone — and must
        // report the degradation rather than silently behaving like a worse curve.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();

        var decision = Step(controller, calibration, Settings(), temperature: 88d, packageWatts: null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsDriven, Is.True);
            Assert.That(decision.IsFeedForwardUnavailable, Is.True);
            Assert.That(decision.DutyPercent, Is.GreaterThan(0d), "Feedback alone must still cool a machine that is 10 °C over target.");
        });
    }

    [Test]
    public void Evaluate_HoldingAboveTarget_IntegratesUpwardOverTime()
    {
        // Proportional action alone leaves a standing offset — the fan settles at a speed that is not quite
        // enough and the machine sits permanently above target. The integral term is what removes that.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        var first = Step(controller, calibration, settings, temperature: 84d);
        for (var i = 0; i < 20; i++)
        {
            Step(controller, calibration, settings, temperature: 84d);
        }

        var later = Step(controller, calibration, settings, temperature: 84d);

        Assert.That(later.DutyPercent, Is.GreaterThan(first.DutyPercent));
    }

    [Test]
    public void Evaluate_AfterSustainedSaturation_ReleasesTheFanPromptlyWhenTheLoadEnds()
    {
        // THE anti-windup test, and the single most common way a PI fan controller becomes infuriating: a
        // long pinned-at-100% load accumulates integral the whole time, and the fan then roars for minutes
        // after the machine goes idle. Ten minutes of saturation, then cold — the fan must come down fast.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        for (var i = 0; i < 600; i++)
        {
            Step(controller, calibration, settings, temperature: 99d, packageWatts: 60d);
        }

        AdaptiveControlDecision? cooled = null;
        for (var i = 0; i < 15; i++)
        {
            cooled = Step(controller, calibration, settings, temperature: 55d, packageWatts: 2d);
        }

        Assert.That(
            cooled!.DutyPercent,
            Is.LessThan(30d),
            $"After the load ended the fan was still at {cooled.DutyPercent:0.#}% — the integrator wound up and did not unwind.");
    }

    [Test]
    public void Evaluate_WhenRising_AddsLeadButOnlyWhileClimbing()
    {
        var rising = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        Step(rising, calibration, settings, temperature: 70d);
        var climbing = Step(rising, calibration, settings, temperature: 76d);

        var falling = new AdaptiveFanController();
        Step(falling, calibration, settings, temperature: 76d);
        var cooling = Step(falling, calibration, settings, temperature: 70d);

        Assert.Multiple(() =>
        {
            Assert.That(climbing.LeadDutyPercent, Is.GreaterThan(0d), "A climbing temperature should anticipate.");
            Assert.That(cooling.LeadDutyPercent, Is.Zero, "A falling temperature must not subtract duty — that is how hunting starts.");
        });
    }

    [Test]
    public void Evaluate_LeadTerm_IsCappedAgainstSensorNoise()
    {
        // A one-sample sensor glitch differentiates into an enormous slope. It must not slam the fan.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        Step(controller, calibration, settings, temperature: 60d);
        var spike = Step(controller, calibration, settings, temperature: 200d);

        Assert.That(
            spike.LeadDutyPercent,
            Is.LessThanOrEqualTo(AdaptiveFanController.MaximumLeadSlopeCelsiusPerSecond * AdaptiveFanController.LeadDutyPerCelsiusPerSecond + 0.001d));
    }

    [Test]
    public void Evaluate_OnSustainedThrottling_LatchesEscalation()
    {
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        AdaptiveControlDecision? decision = null;
        for (var i = 0; i < AdaptiveFanController.ThrottleLatchSampleThreshold; i++)
        {
            decision = Step(controller, calibration, settings, temperature: 92d, performanceRatio: 0.6d);
        }

        Assert.Multiple(() =>
        {
            Assert.That(decision!.IsThrottleLatched, Is.True);
            Assert.That(decision.ThrottleEscalationDutyPercent, Is.EqualTo(AdaptiveFanController.ThrottleEscalationDutyPercent));
            Assert.That(decision.ThrottleLatchedAt, Is.Not.Null);
        });
    }

    [Test]
    public void Evaluate_OnASingleThrottleSample_DoesNotLatch()
    {
        // One low sample is far more likely to be a power-state transition than a thermal event, and a latch
        // pins the fan up for a full minute.
        var controller = new AdaptiveFanController();

        var decision = Step(controller, Calibrated(), Settings(), temperature: 92d, performanceRatio: 0.6d);

        Assert.That(decision.IsThrottleLatched, Is.False);
    }

    [Test]
    public void Evaluate_IdleDownclocking_IsNotMistakenForThrottling()
    {
        // A cold, idle machine downclocks hard. Reading that as thermal throttling would latch the fans up
        // on the quietest machine in the worst possible situation.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        AdaptiveControlDecision? decision = null;
        for (var i = 0; i < 30; i++)
        {
            decision = Step(controller, calibration, settings, temperature: 42d, performanceRatio: 0.95d, packageWatts: 2d);
        }

        Assert.That(decision!.IsThrottleLatched, Is.False);
    }

    [Test]
    public void Evaluate_ReleasesTheLatchOnlyAfterSustainedTimeBelowTarget()
    {
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        for (var i = 0; i < AdaptiveFanController.ThrottleLatchSampleThreshold; i++)
        {
            Step(controller, calibration, settings, temperature: 92d, performanceRatio: 0.6d);
        }

        // Below target, but not for long enough yet.
        var halfway = Step(controller, calibration, settings, temperature: 70d);
        for (var i = 0; i < 20; i++)
        {
            halfway = Step(controller, calibration, settings, temperature: 70d);
        }

        Assert.That(halfway.IsThrottleLatched, Is.True, "The latch must not release on a brief dip.");

        for (var i = 0; i < 60; i++)
        {
            Step(controller, calibration, settings, temperature: 70d);
        }

        var released = Step(controller, calibration, settings, temperature: 70d);
        Assert.That(released.IsThrottleLatched, Is.False);
        Assert.That(released.ThrottleEscalationDutyPercent, Is.Zero);
    }

    [Test]
    public void Evaluate_WhileStillHot_DoesNotReleaseTheLatchOnATimer()
    {
        // The release clock must only run while the temperature is genuinely below target, or a machine
        // pinned AT target would drop its escalation after 60 s and immediately throttle again.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        for (var i = 0; i < AdaptiveFanController.ThrottleLatchSampleThreshold; i++)
        {
            Step(controller, calibration, settings, temperature: 92d, performanceRatio: 0.6d);
        }

        AdaptiveControlDecision? decision = null;
        for (var i = 0; i < 200; i++)
        {
            decision = Step(controller, calibration, settings, temperature: 80d);
        }

        Assert.That(decision!.IsThrottleLatched, Is.True);
    }

    [Test]
    public void ReleaseThrottleLatch_ClearsItImmediately()
    {
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        for (var i = 0; i < AdaptiveFanController.ThrottleLatchSampleThreshold; i++)
        {
            Step(controller, calibration, settings, temperature: 92d, performanceRatio: 0.6d);
        }

        controller.ReleaseThrottleLatch();

        Assert.That(Step(controller, calibration, settings, temperature: 70d).IsThrottleLatched, Is.False);
    }

    [Test]
    public void Evaluate_WithSafetyFloor_NeverDropsBelowIt()
    {
        var controller = new AdaptiveFanController();
        var settings = Settings() with { SafetyFloorEnabled = true, SafetyFloorPercent = 30d };

        var decision = Step(controller, Calibrated(), settings, temperature: 40d, packageWatts: 1d);

        Assert.That(decision.DutyPercent, Is.GreaterThanOrEqualTo(30d));
    }

    /// <summary>
    /// A fan the user has not configured keeps turning on a cold machine.
    /// </summary>
    /// <remarks>
    /// The default is ON, which is a real trade rather than a free win: a binding floor stops the loop
    /// reaching its target at low load, so the machine runs cooler and louder than asked. That is accepted
    /// because a fan which stops completely reads as a fault to most people, and the floor is one toggle away
    /// for anyone who disagrees. Pinned here so the trade cannot be reversed by accident.
    /// </remarks>
    [Test]
    public void Evaluate_OnAnUnconfiguredFan_KeepsItTurningWhenCold()
    {
        var controller = new AdaptiveFanController();

        var decision = Step(
            controller,
            Calibrated(),
            AdaptiveFanSettings.Default,
            temperature: 30d,
            packageWatts: 1d);

        Assert.That(decision.DutyPercent, Is.GreaterThanOrEqualTo(AdaptiveFanSettings.DefaultSafetyFloorPercent));
    }

    /// <summary>
    /// An enabled floor below the fan's measured stall point must never resolve to a stopped fan.
    /// </summary>
    /// <remarks>
    /// The stall guard snaps to whichever end is nearer, which is right when nobody asked for a floor. With a
    /// floor enabled it can only snap UP: the floor exists precisely to promise the fan never stops, and
    /// resolving it downward would stop the fan while the editor still displayed the floor as on.
    /// </remarks>
    [Test]
    public void Evaluate_WithAFloorBelowTheStallPoint_StillKeepsTheFanTurning()
    {
        var calibration = Calibrated() with { MinimumSpinDutyPercent = 30d };
        var settings = Settings() with { SafetyFloorEnabled = true, SafetyFloorPercent = 12d };
        var controller = new AdaptiveFanController();

        var decision = Step(controller, calibration, settings, temperature: 30d, packageWatts: 1d);

        Assert.That(decision.DutyPercent, Is.GreaterThanOrEqualTo(30d), "an enabled safety floor let the fan stop");
    }

    [Test]
    public void Evaluate_NeverCommandsADutyTheFanWouldStallAt()
    {
        // Between "off" and "the slowest it can actually turn" a fan buzzes or stops while still commanded.
        // Any nonzero output must be at or above the measured minimum spin.
        var calibration = Calibrated() with { MinimumSpinDutyPercent = 17d };
        var settings = Settings();

        for (var temperature = 30d; temperature <= 95d; temperature += 0.5d)
        {
            var controller = new AdaptiveFanController();
            var decision = Step(controller, calibration, settings, temperature: temperature, packageWatts: 0d);

            Assert.That(
                decision.DutyPercent is 0d || decision.DutyPercent >= 17d,
                Is.True,
                $"At {temperature} °C the controller asked for {decision.DutyPercent:0.##}% — inside the stall band.");
        }
    }

    [Test]
    public void Evaluate_ClampsIntoTheRangeTheEcAccepts()
    {
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        for (var i = 0; i < 300; i++)
        {
            var decision = Step(controller, calibration, settings, temperature: 130d, packageWatts: 200d);
            Assert.That(decision.DutyPercent, Is.InRange(0d, 100d));
        }
    }

    /// <summary>
    /// The controller saying "not throttled" must beat a performance ratio that merely fell because the
    /// work stopped. That ratio drops for parked cores and idle workloads, and escalating the fan for those
    /// is noise the user cannot account for.
    /// </summary>
    [Test]
    public void Evaluate_WhenEcReportsNoThrottle_IgnoresALowPerformanceRatio()
    {
        var decision = StepMany(
            new AdaptiveFanController(), Calibrated(), Settings(), temperature: 90d,
            performanceRatio: 0.2d, ecThrottle: EcThrottleSeverity.None, ticks: 12);

        Assert.That(decision.IsThrottleLatched, Is.False);
    }

    [Test]
    public void Evaluate_WhenEcReportsHardThrottle_LatchesDespiteAHealthyPerformanceRatio()
    {
        var decision = StepMany(
            new AdaptiveFanController(), Calibrated(), Settings(), temperature: 90d,
            performanceRatio: 1.0d, ecThrottle: EcThrottleSeverity.Hard, ticks: 12);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsThrottleLatched, Is.True);
            Assert.That(decision.ThrottleEscalationDutyPercent, Is.GreaterThan(0d));
        });
    }

    /// <summary>
    /// Soft throttling is a limit being managed, not a protection acting, and it is common under sustained
    /// load. Answering it at full strength would keep the fan escalated most of the time.
    /// </summary>
    [Test]
    public void Evaluate_SoftThrottleEscalatesLessThanHard()
    {
        var soft = StepMany(
            new AdaptiveFanController(), Calibrated(), Settings(), 90d, 1.0d, EcThrottleSeverity.Soft, ticks: 12);
        var hard = StepMany(
            new AdaptiveFanController(), Calibrated(), Settings(), 90d, 1.0d, EcThrottleSeverity.Hard, ticks: 12);

        Assert.Multiple(() =>
        {
            Assert.That(soft.ThrottleEscalationDutyPercent, Is.GreaterThan(0d));
            Assert.That(soft.ThrottleEscalationDutyPercent, Is.LessThan(hard.ThrottleEscalationDutyPercent));
        });
    }

    /// <summary>Firmware that cannot answer must keep the old behaviour, not lose throttle handling.</summary>
    [Test]
    public void Evaluate_WhenEcThrottleIsUnknown_FallsBackToThePerformanceRatio()
    {
        var decision = StepMany(
            new AdaptiveFanController(), Calibrated(), Settings(), temperature: 90d,
            performanceRatio: 0.2d, ecThrottle: null, ticks: 12);

        Assert.That(decision.IsThrottleLatched, Is.True);
    }

    [Test]
    public void Evaluate_ReportsAnExpectedSpeedInsideTheFanRange()
    {
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();

        var decision = Step(controller, calibration, Settings(), temperature: 90d);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ExpectedRpm, Is.Not.Null);
            Assert.That(decision.ExpectedRpm!.Value, Is.InRange(calibration.MinimumSpinRpm, calibration.MaximumRpm));
        });
    }

    /// <summary>
    /// The expected speed is a DISPLAY value derived from the duty, never something the fan is commanded to.
    /// A fan with no measured speeds has nothing to derive it from and must say so rather than guess.
    /// </summary>
    [Test]
    public void Evaluate_WithoutAMeasuredMaximum_ReportsNoExpectedSpeed()
    {
        var controller = new AdaptiveFanController();
        var calibration = Calibrated() with { MaximumRpm = 0d };

        var decision = Step(controller, calibration, Settings(), temperature: 90d);

        Assert.That(decision.ExpectedRpm, Is.Null);
    }

    [Test]
    public void Evaluate_TermsSumToTheRawDemand()
    {
        // The UI renders these four as shares of a whole. If they did not sum, the bar would be a lie.
        var controller = new AdaptiveFanController();
        var calibration = Calibrated();
        var settings = Settings();

        for (var i = 0; i < 30; i++)
        {
            var decision = Step(controller, calibration, settings, temperature: 70d + i, packageWatts: i);

            var sum = decision.FeedForwardDutyPercent
                + decision.ProportionalIntegralDutyPercent
                + decision.LeadDutyPercent
                + decision.ThrottleEscalationDutyPercent;

            Assert.That(sum, Is.EqualTo(decision.RawDutyPercent).Within(1e-9));
        }
    }

    [Test]
    public void Evaluate_SettlesTowardTargetAgainstASimulatedPlant()
    {
        // The end-to-end claim: run the controller against a first-order plant with dead time and it should
        // bring the temperature to target and hold it, without sustained oscillation. This is what separates
        // a controller from an arbitrary formula.
        var calibration = Calibrated();
        var settings = Settings();
        var controller = new AdaptiveFanController();

        var plant = new FirstOrderPlant(
            ambientCelsius: 35d,
            loadWatts: 45d,
            celsiusPerWatt: 1.1d,
            celsiusPerDutyPercent: calibration.ProcessGainCelsiusPerPercent,
            timeConstantSeconds: calibration.TimeConstantSeconds,
            deadTimeSeconds: calibration.DeadTimeSeconds,
            stepSeconds: Tick);

        var temperature = plant.Temperature;
        for (var i = 0; i < 4000; i++)
        {
            var decision = Step(controller, calibration, settings, temperature: temperature, packageWatts: 45d);
            temperature = plant.Step(decision.DutyPercent);
        }

        Assert.That(
            temperature,
            Is.EqualTo(settings.TargetTemperatureCelsius).Within(2.5d),
            $"The loop settled at {temperature:0.#} °C against a {settings.TargetTemperatureCelsius} °C target.");

        // And it must be STILL — a loop that averages the target while swinging either side of it is a fan
        // that audibly surges.
        var samples = new List<double>();
        for (var i = 0; i < 300; i++)
        {
            var decision = Step(controller, calibration, settings, temperature: temperature, packageWatts: 45d);
            temperature = plant.Step(decision.DutyPercent);
            samples.Add(temperature);
        }

        Assert.That(samples.Max() - samples.Min(), Is.LessThan(1.5d), "The settled loop is oscillating.");
    }

    [Test]
    public void Evaluate_OnALoadStep_FeedForwardBeatsFeedbackAlone()
    {
        // The measurable payoff of the whole design. Measured as INTEGRATED ABSOLUTE ERROR rather than peak
        // temperature: peak is a single sample and, with the loop tuned this tightly, the two approaches
        // differ there by a fraction of a degree — a difference smaller than the sensor's resolution, which
        // would make this test a coin flip. IAE is the standard disturbance-rejection measure and captures
        // what actually matters: how far off target the machine was, and for how long.
        var calibration = Calibrated();
        var settings = Settings();

        var withFeedForward = SimulateLoadStepError(calibration, settings, providePower: true);
        var feedbackOnly = SimulateLoadStepError(calibration, settings, providePower: false);

        Assert.That(
            withFeedForward,
            Is.LessThan(feedbackOnly),
            $"Feed-forward accumulated {withFeedForward:0.#} °C·s of error against feedback-only's {feedbackOnly:0.#} — the anticipatory term bought nothing.");
    }

    /// <summary>Integrated absolute error over a load step, in °C·seconds. Lower is better rejection.</summary>
    private static double SimulateLoadStepError(FanCalibrationSnapshot calibration, AdaptiveFanSettings settings, bool providePower)
    {
        var controller = new AdaptiveFanController();
        var plant = new FirstOrderPlant(
            ambientCelsius: 35d,
            loadWatts: 4d,
            celsiusPerWatt: 1.1d,
            celsiusPerDutyPercent: calibration.ProcessGainCelsiusPerPercent,
            timeConstantSeconds: calibration.TimeConstantSeconds,
            deadTimeSeconds: calibration.DeadTimeSeconds,
            stepSeconds: Tick);

        var temperature = plant.Temperature;

        // Settle at idle first, so the step starts from a converged state rather than a cold start.
        for (var i = 0; i < 600; i++)
        {
            var decision = Step(controller, calibration, settings, temperature, providePower ? 4d : null);
            temperature = plant.Step(decision.DutyPercent);
        }

        plant.LoadWatts = 55d;

        var integratedError = 0d;
        for (var i = 0; i < 900; i++)
        {
            var decision = Step(controller, calibration, settings, temperature, providePower ? 55d : null);
            temperature = plant.Step(decision.DutyPercent);
            integratedError += Math.Abs(temperature - settings.TargetTemperatureCelsius) * Tick;
        }

        return integratedError;
    }

    private static AdaptiveControlDecision Step(
        AdaptiveFanController controller,
        FanCalibrationSnapshot calibration,
        AdaptiveFanSettings settings,
        double temperature,
        double? packageWatts = 20d,
        double? performanceRatio = 1d,
        EcThrottleSeverity? ecThrottle = null)
        => controller.Evaluate(
            calibration,
            settings,
            temperature,
            new ControlTelemetrySample
            {
                CpuPackagePowerWatts = packageWatts,
                CpuPerformanceRatio = performanceRatio,
                EcThrottle = ecThrottle,
            },
            TimeSpan.FromSeconds(Tick),
            DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Steps the controller repeatedly and returns the LAST decision.
    /// </summary>
    /// <remarks>
    /// The throttle latch needs several consecutive samples before it engages, so a single step can never
    /// show it — a test asserting on latching from one tick would pass or fail for the wrong reason.
    /// </remarks>
    private static AdaptiveControlDecision StepMany(
        AdaptiveFanController controller,
        FanCalibrationSnapshot calibration,
        AdaptiveFanSettings settings,
        double temperature,
        double? performanceRatio,
        EcThrottleSeverity? ecThrottle,
        int ticks)
    {
        AdaptiveControlDecision decision = AdaptiveControlDecision.NotDriven;
        for (var index = 0; index < ticks; index++)
        {
            decision = Step(controller, calibration, settings, temperature, performanceRatio: performanceRatio, ecThrottle: ecThrottle);
        }

        return decision;
    }

    /// <summary>
    /// Settings for testing the LOOP: target set, safety floor explicitly off.
    /// </summary>
    /// <remarks>
    /// The floor is disabled deliberately rather than left to the record's defaults, which now enable it. A
    /// binding floor is not a property of the controller — it overrides the loop by design — so leaving it on
    /// would stop these tests measuring what they claim to, and would do so silently the moment the default
    /// changed. The floor's own behaviour is covered separately.
    /// </remarks>
    private static AdaptiveFanSettings Settings() => new()
    {
        TargetTemperatureCelsius = 78d,
        SafetyFloorEnabled = false,
    };

    /// <summary>A representative Framework 16 fan, in the shape a calibration run would produce.</summary>
    private static FanCalibrationSnapshot Calibrated()
    {
        var snapshot = new FanCalibrationSnapshot
        {
            State = FanCalibrationState.Ok,
            CalibratedAt = DateTimeOffset.UnixEpoch,
            ProcessGainCelsiusPerPercent = 0.42d,
            TimeConstantSeconds = 26d,
            DeadTimeSeconds = 4d,
            MinimumSpinRpm = 1_180d,
            MinimumSpinDutyPercent = 17d,
            MaximumRpm = 7_000d,
            FeedForwardDutyPerWatt = 0.9d,
        };

        return snapshot;
    }

    /// <summary>
    /// A first-order-plus-dead-time thermal plant, for closed-loop tests.
    /// </summary>
    /// <remarks>
    /// Temperature relaxes toward an equilibrium set by load and airflow, with commanded duty delayed by the
    /// dead time. Crude next to a real heatsink, but it is the same model the controller is tuned against, so
    /// a loop that cannot hold THIS steady has no chance on hardware.
    /// </remarks>
    private sealed class FirstOrderPlant(
        double ambientCelsius,
        double loadWatts,
        double celsiusPerWatt,
        double celsiusPerDutyPercent,
        double timeConstantSeconds,
        double deadTimeSeconds,
        double stepSeconds)
    {
        private readonly Queue<double> _dutyPipeline =
            new(Enumerable.Repeat(0d, Math.Max(1, (int)Math.Round(deadTimeSeconds / stepSeconds))));

        public double LoadWatts { get; set; } = loadWatts;

        public double Temperature { get; private set; } = ambientCelsius + (loadWatts * celsiusPerWatt);

        public double Step(double commandedDutyPercent)
        {
            _dutyPipeline.Enqueue(commandedDutyPercent);
            var effectiveDuty = _dutyPipeline.Dequeue();

            var equilibrium = ambientCelsius
                + (LoadWatts * celsiusPerWatt)
                - (effectiveDuty * celsiusPerDutyPercent);

            equilibrium = Math.Max(equilibrium, ambientCelsius);

            var alpha = 1d - Math.Exp(-stepSeconds / timeConstantSeconds);
            Temperature += (equilibrium - Temperature) * alpha;

            return Temperature;
        }
    }
}
