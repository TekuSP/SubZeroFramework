using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the step-response fit — the numbers a calibration run exists to produce.
/// </summary>
/// <remarks>
/// These feed SIMC directly, so an error here becomes a mis-tuned controller on real hardware. The tests
/// generate responses from a KNOWN plant and check the fit recovers it, then check that every way the run can
/// go wrong is reported rather than papered over with a plausible-looking answer.
/// </remarks>
[TestFixture]
public class FopdtIdentificationTests
{
    private const double DutyStep = 78d;

    [Test]
    public void Identify_RecoversAKnownPlant()
    {
        // The design's worked example: K 0.42, τ 26 s, L 4 s over a 78-point duty step.
        var samples = SimulateStep(processGain: 0.42d, timeConstant: 26d, deadTime: 4d);

        var result = FopdtIdentification.Identify(samples, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ProcessGainCelsiusPerPercent, Is.EqualTo(0.42d).Within(0.02d));
            Assert.That(result.TimeConstantSeconds, Is.EqualTo(26d).Within(3d));
            Assert.That(result.DeadTimeSeconds, Is.EqualTo(4d).Within(1.5d));
        });
    }

    [Test]
    public void Identify_RecoversASluggishPlant()
    {
        // A thicker chassis with a slower heatsink. The fit must not be tuned to one machine's numbers.
        var samples = SimulateStep(processGain: 0.28d, timeConstant: 55d, deadTime: 9d, durationSeconds: 320d);

        var result = FopdtIdentification.Identify(samples, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimeConstantSeconds, Is.EqualTo(55d).Within(6d));
            Assert.That(result.DeadTimeSeconds, Is.EqualTo(9d).Within(2.5d));
        });
    }

    [Test]
    public void Identify_RecoversAFastPlant()
    {
        var samples = SimulateStep(processGain: 0.6d, timeConstant: 12d, deadTime: 2d, durationSeconds: 90d);

        var result = FopdtIdentification.Identify(samples, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.TimeConstantSeconds, Is.EqualTo(12d).Within(2.5d));
            Assert.That(result.DeadTimeSeconds, Is.EqualTo(2d).Within(1.5d));
        });
    }

    [Test]
    public void Identify_SurvivesSensorNoise()
    {
        // A real EC reports whole or half degrees with jitter. The fit has to be robust to that, because
        // every run on real hardware looks like this.
        var clean = SimulateStep(processGain: 0.42d, timeConstant: 26d, deadTime: 4d);
        var noisy = clean
            .Select((sample, index) => (sample.Seconds, Celsius: sample.Celsius + (index % 3 == 0 ? 0.4d : -0.3d)))
            .ToArray();

        var result = FopdtIdentification.Identify(noisy, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ProcessGainCelsiusPerPercent, Is.EqualTo(0.42d).Within(0.05d));
            Assert.That(result.TimeConstantSeconds, Is.EqualTo(26d).Within(6d));
        });
    }

    [Test]
    public void Identify_WithATinySwing_ReportsItRatherThanFitting()
    {
        // A cool room. Under the EC's whole-degree quantisation the timing points would land essentially at
        // random, so a confident answer here would be worse than no answer. The gain is chosen to land the
        // swing under the ABSOLUTE floor — a swing this small is unfittable however quiet the signal.
        var samples = SimulateStep(processGain: 0.03d, timeConstant: 26d, deadTime: 4d);

        var result = FopdtIdentification.Identify(samples, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientTemperatureSwing));
            Assert.That(result.TemperatureSwingCelsius, Is.GreaterThan(0d), "The failure screen has to report how small the swing actually was.");
        });
    }

    /// <summary>
    /// The swing gate is relative to the measured noise, not a fixed number.
    /// </summary>
    /// <remarks>
    /// The SAME modest swing, twice: quiet, it must fit — this is one fan of a dual-fan shared-heatpipe
    /// chassis, whose genuine 4-5 °C authority a flat 8 °C gate refused on real hardware. Noisy — the load
    /// bouncing, as a governor oscillation really produced — the identical swing must be refused, because
    /// tail noise that large can fake the crossings the timing points come from.
    /// </remarks>
    [Test]
    public void Identify_GatesTheSwingAgainstTheMeasuredNoise()
    {
        // ~4.7 °C of swing across a 78-point step: real numbers from a Framework 16 left fan.
        var clean = SimulateStep(processGain: 0.06d, timeConstant: 26d, deadTime: 4d);

        // The same response under a SLOW disturbance — a load oscillation with a period far wider than the
        // fit's smoothing window, which is what real un-smoothable noise looks like. A fast alternating
        // pattern was used here once, and it was a hollow test: the centred average removes period-two
        // noise almost perfectly, so the refusal it asserted was one smoothing legitimately prevents.
        var noisy = AddSlowDisturbance(clean, amplitudeCelsius: 1.7d);

        var quiet = FopdtIdentification.Identify(clean, DutyStep);
        var disturbed = FopdtIdentification.Identify(noisy, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(quiet.IsSuccess, Is.True, "a quiet 4-5 °C response is genuinely fittable and must not be refused");
            Assert.That(quiet.ProcessGainCelsiusPerPercent, Is.EqualTo(0.06d).Within(0.01d));

            Assert.That(disturbed.IsSuccess, Is.False, "the same swing buried in comparable noise must be refused");
            Assert.That(disturbed.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientTemperatureSwing));
        });
    }

    /// <summary>
    /// Quantisation-grade jitter is smoothed away before the gate judges the swing.
    /// </summary>
    /// <remarks>
    /// The regression this pins: a real run measured a genuine 6.5 °C swing and was refused, because the
    /// raw tail — whole-degree EC quantisation on a maximum over several sensors — wobbled ±1 °C and the
    /// noise gate demanded more swing than the machine's fans can produce. The fit now reads a zero-phase
    /// smoothed curve, so the gate sees the residual wobble instead of the raw flicker.
    /// </remarks>
    [Test]
    public void Identify_FitsAModestSwing_OnceQuantisationJitterIsSmoothed()
    {
        // 6.5 °C of swing with fast ±1.8 °C jitter on every reading — the EC's flicker at the amplitude the
        // real refusal measured (raw tail sigma ≈ 1.2 °C, making the unsmoothed gate demand 7.2 °C).
        var samples = SimulateStep(processGain: 6.5d / DutyStep, timeConstant: 26d, deadTime: 4d)
            .Select(static (sample, index) => (sample.Seconds, Celsius: sample.Celsius + (((index * 31) % 7) - 3) * 0.6d))
            .ToArray();

        var result = FopdtIdentification.Identify(samples, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, "a genuine 6.5 °C swing under sensor flicker must fit — this refusal happened on real hardware");
            Assert.That(result.ProcessGainCelsiusPerPercent, Is.EqualTo(6.5d / DutyStep).Within(0.015d));
        });
    }

    /// <summary>A disturbance the smoothing window cannot absorb: a slow sinusoid, like an oscillating load.</summary>
    private static (double Seconds, double Celsius)[] AddSlowDisturbance(
        IReadOnlyList<(double Seconds, double Celsius)> samples,
        double amplitudeCelsius)
        => [.. samples.Select(static (sample, index) => (sample.Seconds, sample.Celsius))
            .Select((sample, index) => (sample.Item1, sample.Item2 + (amplitudeCelsius * Math.Sin(index * 2d * Math.PI / 14d))))];

    [Test]
    public void Identify_WhenTemperatureRose_RefusesToFit()
    {
        // More fan means cooler. A rise means a workload grew during the measurement, so the response is not
        // the fan's — fitting it would attribute someone else's heat to this fan's model.
        var samples = Enumerable.Range(0, 120)
            .Select(i => ((double)i, 70d + (i * 0.2d)))
            .ToArray();

        var result = FopdtIdentification.Identify(samples, DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientTemperatureSwing));
        });
    }

    [Test]
    public void Identify_WithTooFewSamples_Fails()
    {
        var result = FopdtIdentification.Identify([(0d, 90d), (1d, 80d)], DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientData));
        });
    }

    [Test]
    public void Identify_WithNoDutyStep_Fails()
    {
        // Dividing the swing by a zero step would hand back an infinite gain, and SIMC divides BY the gain —
        // so the controller would come out with zero gain and never move the fan.
        var samples = SimulateStep(processGain: 0.42d, timeConstant: 26d, deadTime: 4d);

        Assert.That(FopdtIdentification.Identify(samples, 0d).IsSuccess, Is.False);
    }

    [Test]
    public void Identify_ProducesGainsThatAreActuallyUsable()
    {
        // The end-to-end contract: whatever comes out of a run must survive the tuning law and produce a
        // controller that can drive a fan. A fit that SIMC then rejects would leave Adaptive silently inert.
        var samples = SimulateStep(processGain: 0.42d, timeConstant: 26d, deadTime: 4d);
        var result = FopdtIdentification.Identify(samples, DutyStep);

        var gains = AdaptivePidTuning.Compute(
            result.ProcessGainCelsiusPerPercent,
            result.TimeConstantSeconds,
            result.DeadTimeSeconds);

        Assert.That(gains.IsUsable, Is.True);
    }

    [Test]
    public void Identify_IsInsensitiveToTheSampleRate()
    {
        // The primary polling tier is user-configurable, so the same machine can be measured at very
        // different cadences. The identified plant is a property of the hardware and must not move with it.
        var fast = FopdtIdentification.Identify(
            SimulateStep(0.42d, 26d, 4d, sampleIntervalSeconds: 0.25d), DutyStep);
        var slow = FopdtIdentification.Identify(
            SimulateStep(0.42d, 26d, 4d, sampleIntervalSeconds: 2d), DutyStep);

        Assert.Multiple(() =>
        {
            Assert.That(slow.TimeConstantSeconds, Is.EqualTo(fast.TimeConstantSeconds).Within(4d));
            Assert.That(slow.DeadTimeSeconds, Is.EqualTo(fast.DeadTimeSeconds).Within(2d));
        });
    }

    /// <summary>
    /// Generates the temperature fall a FOPDT plant produces when the fan is stepped up at t = 0.
    /// </summary>
    private static (double Seconds, double Celsius)[] SimulateStep(
        double processGain,
        double timeConstant,
        double deadTime,
        double durationSeconds = 180d,
        double sampleIntervalSeconds = 1d)
    {
        const double startCelsius = 92d;
        var totalDrop = processGain * DutyStep;

        List<(double, double)> samples = [];
        for (var t = 0d; t <= durationSeconds; t += sampleIntervalSeconds)
        {
            var celsius = t < deadTime
                ? startCelsius
                : startCelsius - (totalDrop * (1d - Math.Exp(-(t - deadTime) / timeConstant)));

            samples.Add((t, celsius));
        }

        return [.. samples];
    }
}
