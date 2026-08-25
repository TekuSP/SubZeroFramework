using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for how calibration and self-learning COMPOSE: the learner identifies what live operation can
/// resolve, and merges it over whatever a hot test measured.
/// </summary>
[TestFixture]
public class AdaptiveModelLearnerTests
{
    private const double CalibratedGain = 0.42d;
    private const double CalibratedFeedForward = 0.9d;

    // The machine actually in front of us, which differs from what calibration recorded — dust, a warmer
    // room, degraded paste. Identification should find THIS.
    private const double TrueIntercept = 35d;
    private const double TrueCelsiusPerWatt = 1.1d;
    private const double TrueProcessGain = 0.30d;

    [Test]
    public void EffectiveModel_BeforeIdentifying_IsTheCalibrationUnchanged()
    {
        // A fan that has just been calibrated runs on exactly what the hot test measured. Self-learning must
        // not perturb a fresh, controlled measurement on the strength of no evidence.
        var learner = Anchored();

        var model = learner.EffectiveModel(Calibration());

        Assert.Multiple(() =>
        {
            Assert.That(model.ProcessGainCelsiusPerPercent, Is.EqualTo(CalibratedGain));
            Assert.That(model.FeedForwardDutyPerWatt, Is.EqualTo(CalibratedFeedForward));
        });
    }

    [Test]
    public void EffectiveModel_AfterIdentifying_OverridesTheCalibratedGain()
    {
        // The point of the pair: calibration got the machine into the right ballpark, live operation found
        // where it actually is now.
        var learner = Anchored();

        RunVariedOperation(learner);

        var model = learner.EffectiveModel(Calibration());

        Assert.Multiple(() =>
        {
            Assert.That(learner.State.HasLearned, Is.True);
            Assert.That(model.ProcessGainCelsiusPerPercent, Is.EqualTo(TrueProcessGain).Within(0.06d));
            Assert.That(model.ProcessGainCelsiusPerPercent, Is.Not.EqualTo(CalibratedGain));
        });
    }

    [Test]
    public void EffectiveModel_KeepsTheParametersLiveOperationCannotResolve()
    {
        // A settled machine carries NO information about dead time, time constant, stall point or whether the
        // EC tracks speed. Those must survive identification untouched — they are exactly what the hot test is
        // for, and inventing them from steady-state data would be fabrication.
        var learner = Anchored();
        var calibration = Calibration();

        RunVariedOperation(learner);
        var model = learner.EffectiveModel(calibration);

        Assert.Multiple(() =>
        {
            Assert.That(model.DeadTimeSeconds, Is.EqualTo(calibration.DeadTimeSeconds));
            Assert.That(model.TimeConstantSeconds, Is.EqualTo(calibration.TimeConstantSeconds));
            Assert.That(model.MinimumSpinRpm, Is.EqualTo(calibration.MinimumSpinRpm));
            Assert.That(model.MinimumSpinDutyPercent, Is.EqualTo(calibration.MinimumSpinDutyPercent));
            Assert.That(model.TrackingMode, Is.EqualTo(calibration.TrackingMode));
        });
    }

    [Test]
    public void EffectiveModel_WithNoCalibrationAtAll_StillRunsOnWhatItIdentified()
    {
        // Self-learning standing on its own. Nothing to anchor against, so the identified values are simply
        // believed — the estimator's own physical-plausibility limits are what reject nonsense.
        var learner = new AdaptiveModelLearner();
        learner.Anchor(0d);

        RunVariedOperation(learner);

        var model = learner.EffectiveModel(FanCalibrationSnapshot.None);

        Assert.That(model.ProcessGainCelsiusPerPercent, Is.EqualTo(TrueProcessGain).Within(0.06d));
    }

    [Test]
    public void EffectiveModel_CannotStrayFarFromACalibratedAnchor()
    {
        // Calibration measured this chassis under controlled excitation. Identification refines it; it does
        // not get to overrule it by an order of magnitude. A pinned value means "recalibrate", not "keep going".
        var learner = Anchored();

        // A machine reporting an absurdly effective fan — far more likely a degenerate fit than a discovery.
        RunVariedOperation(learner, processGain: 2.5d);

        var model = learner.EffectiveModel(Calibration());

        Assert.That(
            model.ProcessGainCelsiusPerPercent,
            Is.LessThanOrEqualTo(CalibratedGain * AdaptiveModelLearner.MaximumDeviationRatio + 1e-9d));
    }

    [Test]
    public void Observe_DuringATransient_LearnsNothing()
    {
        // The fit assumes each sample sits on the steady-state surface. A machine mid-ramp does not.
        var learner = Anchored();

        RunVariedOperation(learner, slope: 0.9d);

        Assert.That(learner.State.HasLearned, Is.False);
    }

    [Test]
    public void Observe_WhileSaturated_LearnsNothing()
    {
        // A fan pinned at 100% is not holding the temperature, so the point is not on the steady-state surface.
        var learner = Anchored();

        RunVariedOperation(learner, saturated: true);

        Assert.That(learner.State.HasLearned, Is.False);
    }

    [Test]
    public void Observe_WhileThrottleLatched_LearnsNothing()
    {
        // The escalation is adding duty the model did not ask for; attributing it to the model would bias it.
        var learner = Anchored();

        RunVariedOperation(learner, latched: true);

        Assert.That(learner.State.HasLearned, Is.False);
    }

    [Test]
    public void Observe_WithoutSufficientDwell_LearnsNothing()
    {
        var learner = Anchored();

        learner.Observe(Observation(40d, 45d), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);

        Assert.That(learner.State.HasLearned, Is.False, "Steady for one tick is not steady.");
    }

    [Test]
    public void Observe_ATransientMidDwell_RestartsTheDwell()
    {
        var learner = Anchored();

        for (var i = 0; i < 25; i++)
        {
            learner.Observe(Observation(40d, 45d), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        }

        learner.Observe(Observation(40d, 45d, slope: 0.9d), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);

        for (var i = 0; i < 25; i++)
        {
            learner.Observe(Observation(40d, 45d), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        }

        Assert.That(learner.State.ObservationCount, Is.Zero, "The dwell must restart after a disturbance, not resume.");
    }

    [Test]
    public void Observe_WhenThePowerSourceChanges_RefusesTheSample()
    {
        // Unplugging the charger swaps system power for component power. They have completely different
        // couplings to zone temperature, so folding both into one fit would move b toward a value describing
        // neither — with no symptom until the fan behaves oddly days later.
        var learner = Anchored();
        RunVariedOperation(learner);
        var identified = learner.State.IdentifiedProcessGainCelsiusPerPercent;
        Assert.That(identified, Is.Not.Null, "Precondition: a fit exists.");

        RunVariedOperation(learner, source: ThermalLoadSource.System);

        Assert.That(
            learner.State.IdentifiedProcessGainCelsiusPerPercent,
            Is.EqualTo(identified),
            "Samples from a different power source must not touch the fit.");
    }

    [Test]
    public void Observe_WithNoPowerReading_RefusesTheSample()
    {
        // Nothing to attribute the duty to. Feeding a zero would teach the model that this machine runs its
        // fan for no reason.
        var learner = Anchored();

        RunVariedOperation(learner, source: ThermalLoadSource.None);

        Assert.That(learner.State.HasLearned, Is.False);
    }

    [Test]
    public void Observe_OnSystemPowerAlone_StillIdentifiesTheMachine()
    {
        // The Windows path: no CPU package power, so the fit is built on charger-derived system draw. Coarser,
        // but the estimator learns whatever coupling that figure has — which is the whole point of
        // identifying b rather than assuming it.
        var learner = Anchored();

        RunVariedOperation(learner, source: ThermalLoadSource.System);

        Assert.Multiple(() =>
        {
            Assert.That(learner.State.HasLearned, Is.True);
            Assert.That(learner.State.ThermalLoadSource, Is.EqualTo(ThermalLoadSource.System));
        });
    }

    [Test]
    public void Anchor_OnRecalibration_DiscardsWhatWasIdentifiedAroundTheOldModel()
    {
        // A fresh hot test is a controlled re-measurement of the machine as it is NOW. Identification made
        // around the model it replaced describes a machine that measurement just superseded.
        var learner = Anchored();
        RunVariedOperation(learner);
        Assert.That(learner.State.HasLearned, Is.True, "Precondition.");

        learner.Anchor(CalibratedFeedForward * 1.4d);

        Assert.That(learner.State.HasLearned, Is.False);
    }

    [Test]
    public void Anchor_WithTheSameCalibration_KeepsWhatWasIdentified()
    {
        // Anchor runs every tick. If an unchanged calibration reset anything, nothing could ever be learned.
        var learner = Anchored();
        RunVariedOperation(learner);
        var gain = learner.EffectiveModel(Calibration()).ProcessGainCelsiusPerPercent;

        for (var i = 0; i < 100; i++)
        {
            learner.Anchor(CalibratedFeedForward);
        }

        Assert.That(learner.EffectiveModel(Calibration()).ProcessGainCelsiusPerPercent, Is.EqualTo(gain));
    }

    [Test]
    public void State_ResumesAcrossRestarts()
    {
        var first = Anchored();
        RunVariedOperation(first);
        var learned = first.State;

        var resumed = new AdaptiveModelLearner(learned);
        resumed.Anchor(CalibratedFeedForward);

        Assert.That(
            resumed.EffectiveModel(Calibration()).ProcessGainCelsiusPerPercent,
            Is.EqualTo(first.EffectiveModel(Calibration()).ProcessGainCelsiusPerPercent).Within(1e-6d),
            "A restart must not throw away a converged model.");
    }

    [Test]
    public void ResetDwell_KeepsWhatWasIdentified()
    {
        // Leaving Adaptive for a while does not un-learn the chassis.
        var learner = Anchored();
        RunVariedOperation(learner);
        var gain = learner.EffectiveModel(Calibration()).ProcessGainCelsiusPerPercent;

        learner.ResetDwell();

        Assert.That(learner.EffectiveModel(Calibration()).ProcessGainCelsiusPerPercent, Is.EqualTo(gain));
    }

    private static AdaptiveModelLearner Anchored(AdaptiveLearningState? state = null)
    {
        var learner = new AdaptiveModelLearner(state);
        learner.Anchor(CalibratedFeedForward);
        return learner;
    }

    /// <summary>Drives a spread of realistic operating points through the learner, in real time.</summary>
    private static void RunVariedOperation(
        AdaptiveModelLearner learner,
        double processGain = TrueProcessGain,
        double slope = 0d,
        bool saturated = false,
        bool latched = false,
        ThermalLoadSource source = ThermalLoadSource.CpuAndGpu)
    {
        (double Power, double Duty)[] points =
        [
            (18d, 22d), (32d, 38d), (45d, 52d), (58d, 66d), (26d, 30d), (51d, 60d),
        ];

        // Each accepted sample needs both the dwell and the spacing interval, so time must actually pass.
        var secondsPerSample = (int)Math.Ceiling(
            Math.Max(AdaptiveModelLearner.SteadyStateDwell.TotalSeconds, AdaptiveModelLearner.ObservationInterval.TotalSeconds)) + 1;

        for (var round = 0; round < 14; round++)
        {
            foreach (var (power, duty) in points)
            {
                for (var tick = 0; tick < secondsPerSample; tick++)
                {
                    learner.Observe(
                        Observation(power, duty, slope, saturated, latched, processGain, source),
                        TimeSpan.FromSeconds(1),
                        DateTimeOffset.UnixEpoch);
                }
            }
        }
    }

    private static AdaptiveLearningObservation Observation(
        double powerWatts,
        double dutyPercent,
        double slope = 0d,
        bool saturated = false,
        bool latched = false,
        double processGain = TrueProcessGain,
        ThermalLoadSource source = ThermalLoadSource.CpuAndGpu)
        => new()
        {
            PackagePowerWatts = powerWatts,
            ThermalLoadSource = source,
            TemperatureCelsius = TrueIntercept + (TrueCelsiusPerWatt * powerWatts) - (processGain * dutyPercent),
            TemperatureErrorCelsius = 0d,
            CommandedDutyPercent = dutyPercent,
            TemperatureSlopeCelsiusPerSecond = slope,
            FeedForwardDutyPercent = dutyPercent * 0.8d,
            ProportionalIntegralDutyPercent = dutyPercent * 0.2d,
            IsSaturated = saturated,
            IsThrottleLatched = latched,
        };

    private static FanCalibrationSnapshot Calibration()
        => new()
        {
            State = FanCalibrationState.Ok,
            CalibratedAt = DateTimeOffset.UnixEpoch,
            ProcessGainCelsiusPerPercent = CalibratedGain,
            TimeConstantSeconds = 26d,
            DeadTimeSeconds = 4d,
            MinimumSpinRpm = 1_180d,
            MinimumSpinDutyPercent = 17d,
            MaximumRpm = 7_000d,
            FeedForwardDutyPerWatt = CalibratedFeedForward,
            TrackingMode = FanSpeedTrackingMode.Cascade,
        };
}
