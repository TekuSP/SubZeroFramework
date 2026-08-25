using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the self-learning half: identifying a fan's steady-state model from ordinary use.
/// </summary>
/// <remarks>
/// The danger here is not failing to identify — it is identifying confidently from data that cannot support
/// it, and handing the controller a fabricated model. Most of these tests assert that the estimator REFUSES
/// to publish.
/// </remarks>
[TestFixture]
public class AdaptivePlantEstimatorTests
{
    // The machine these tests simulate: T = 35 + 1.1·P − 0.42·duty.
    private const double TrueIntercept = 35d;
    private const double TrueCelsiusPerWatt = 1.1d;
    private const double TrueProcessGain = 0.42d;

    [Test]
    public void Observe_WithVariedOperatingPoints_IdentifiesTheMachine()
    {
        var estimator = new AdaptivePlantEstimator();

        FeedRealisticOperatingPoints(estimator);

        Assert.Multiple(() =>
        {
            Assert.That(estimator.HasSufficientExcitation, Is.True);
            Assert.That(estimator.ProcessGainCelsiusPerPercent, Is.EqualTo(TrueProcessGain).Within(0.05d));
            Assert.That(estimator.CelsiusPerWatt, Is.EqualTo(TrueCelsiusPerWatt).Within(0.15d));
        });
    }

    [Test]
    public void FeedForwardGain_IsConsistentWithTheIdentifiedProcessGain()
    {
        // b/K by construction. Estimating the two separately is how a controller ends up with a feed-forward
        // term that disagrees with the gain the feedback loop was tuned against.
        var estimator = new AdaptivePlantEstimator();

        FeedRealisticOperatingPoints(estimator);

        Assert.That(
            estimator.FeedForwardDutyPerWatt,
            Is.EqualTo(TrueCelsiusPerWatt / TrueProcessGain).Within(0.25d));
    }

    [Test]
    public void Observe_AtASingleOperatingPoint_RefusesToPublish()
    {
        // THE failure mode. One point repeated a hundred times still constrains three unknowns not at all,
        // and least squares will happily return a confident answer to an unanswerable question.
        var estimator = new AdaptivePlantEstimator();

        for (var i = 0; i < 100; i++)
        {
            Observe(estimator, powerWatts: 40d, dutyPercent: 45d);
        }

        Assert.Multiple(() =>
        {
            Assert.That(estimator.HasSufficientExcitation, Is.False);
            Assert.That(estimator.ProcessGainCelsiusPerPercent, Is.Null);
            Assert.That(estimator.FeedForwardDutyPerWatt, Is.Null);
        });
    }

    [Test]
    public void Observe_WithPowerVariationButConstantDuty_RefusesToPublish()
    {
        // Load varied, fan pinned. b·P and K·duty cannot be separated when duty never moves.
        var estimator = new AdaptivePlantEstimator();

        for (var i = 0; i < 60; i++)
        {
            Observe(estimator, powerWatts: 20d + (i % 40), dutyPercent: 45d);
        }

        Assert.That(estimator.ProcessGainCelsiusPerPercent, Is.Null);
    }

    [Test]
    public void Observe_WithTooFewSamples_RefusesToPublish()
    {
        var estimator = new AdaptivePlantEstimator();

        Observe(estimator, powerWatts: 20d, dutyPercent: 30d);
        Observe(estimator, powerWatts: 60d, dutyPercent: 70d);

        Assert.That(estimator.HasSufficientExcitation, Is.False, "Two points fit three unknowns exactly, and mean nothing.");
    }

    [Test]
    public void Observe_RejectsAPhysicallyImpossibleFit()
    {
        // A machine where more fan means MORE heat is a broken fit, not a discovery. Publishing it would
        // invert the controller.
        var estimator = new AdaptivePlantEstimator();

        for (var i = 0; i < 40; i++)
        {
            var power = 20d + (i % 5 * 15d);
            var duty = 20d + (i % 7 * 12d);

            // Deliberately inverted sign on the duty term.
            estimator.Observe(TrueIntercept + (TrueCelsiusPerWatt * power) + (TrueProcessGain * duty), power, duty);
        }

        Assert.That(estimator.ProcessGainCelsiusPerPercent, Is.Null);
    }

    [Test]
    public void Observe_TracksAMachineThatPhysicallyChanges()
    {
        // The reason self-learning exists alongside calibration: dust, degraded paste, a warmer room. The
        // estimator must converge to the machine as it is now, not stay anchored to how it was.
        var estimator = new AdaptivePlantEstimator();

        FeedRealisticOperatingPoints(estimator);
        Assert.That(estimator.ProcessGainCelsiusPerPercent, Is.EqualTo(TrueProcessGain).Within(0.05d), "Precondition.");

        // The fan is now half as effective — a blocked vent.
        const double degradedGain = 0.21d;
        for (var round = 0; round < 25; round++)
        {
            foreach (var (power, duty) in OperatingPoints())
            {
                estimator.Observe(TrueIntercept + (TrueCelsiusPerWatt * power) - (degradedGain * duty), power, duty);
            }
        }

        Assert.That(estimator.ProcessGainCelsiusPerPercent, Is.EqualTo(degradedGain).Within(0.05d));
    }

    [Test]
    public void Restore_ResumesAConvergedFit()
    {
        var estimator = new AdaptivePlantEstimator();
        FeedRealisticOperatingPoints(estimator);

        var resumed = new AdaptivePlantEstimator();
        resumed.Restore(new AdaptiveLearningState
        {
            IdentifiedProcessGainCelsiusPerPercent = estimator.ProcessGainCelsiusPerPercent,
            IdentifiedCelsiusPerWatt = estimator.CelsiusPerWatt,
            IdentifiedInterceptCelsius = estimator.InterceptCelsius,
            FeedForwardDutyPerWatt = estimator.FeedForwardDutyPerWatt,
            ObservationCount = estimator.ObservationCount,
        });

        Assert.That(
            resumed.ProcessGainCelsiusPerPercent,
            Is.EqualTo(estimator.ProcessGainCelsiusPerPercent!.Value).Within(1e-6d),
            "A restart must not throw away a converged model.");
    }

    private static IEnumerable<(double Power, double Duty)> OperatingPoints()
    {
        // A plausible spread of real use: idle-ish, browsing, a build, a game — each holding a different duty.
        yield return (18d, 22d);
        yield return (32d, 38d);
        yield return (45d, 52d);
        yield return (58d, 66d);
        yield return (26d, 30d);
        yield return (51d, 60d);
    }

    private static void FeedRealisticOperatingPoints(AdaptivePlantEstimator estimator)
    {
        for (var round = 0; round < 12; round++)
        {
            foreach (var (power, duty) in OperatingPoints())
            {
                Observe(estimator, power, duty);
            }
        }
    }

    private static void Observe(AdaptivePlantEstimator estimator, double powerWatts, double dutyPercent)
        => estimator.Observe(
            TrueIntercept + (TrueCelsiusPerWatt * powerWatts) - (TrueProcessGain * dutyPercent),
            powerWatts,
            dutyPercent);
}
