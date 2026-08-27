using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Fits a first-order-plus-dead-time model to a measured step response.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two-point method.</b> For a FOPDT plant the response to a step reaches 28.3% of its total change at
/// <c>L + τ/3</c> and 63.2% at <c>L + τ</c>. Two readings, two equations:
/// <c>τ = 1.5·(t₆₃ − t₂₈)</c> and <c>L = t₆₃ − τ</c>.
/// </para>
/// <para>
/// Chosen over least-squares curve fitting deliberately. It needs no initial guess and cannot diverge, which
/// matters because this runs unattended on a hot machine and its output goes straight into a controller that
/// drives real fans. A fit that fails should fail obviously, not return a confident-looking wrong answer.
/// </para>
/// <para>
/// <b>The step is the FAN, not the load.</b> The run holds a steady CPU load, lets temperature settle at a low
/// duty, then steps the fan to maximum and watches temperature FALL. That isolates duty→temperature, which is
/// what K means. Stepping the load instead would measure watts→temperature, a different coefficient the online
/// estimator already identifies from ordinary use.
/// </para>
/// </remarks>
public static class FopdtIdentification
{
    /// <summary>Fraction of the total change used for the first timing point.</summary>
    public const double FirstPointFraction = 0.283d;

    /// <summary>Fraction used for the second timing point — one time constant.</summary>
    public const double SecondPointFraction = 0.632d;

    /// <summary>
    /// Smallest temperature swing that can ever be fitted, in °C, however quiet the signal.
    /// </summary>
    /// <remarks>
    /// The floor exists for the EC's whole-degree quantisation: below about three counts of range, the
    /// 28%/63% crossings the timing points come from land between representable values. This used to be 8 °C
    /// flat, which quietly assumed a fan with sole authority over its sensor — on a dual-fan shared-heatpipe
    /// chassis, one fan's step (with the other held still, as it must be) genuinely moves the sensor 4-5 °C,
    /// and the flat gate refused a perfectly fittable response. How much MORE than the floor is required is
    /// decided by the measured noise, below.
    /// </remarks>
    public const double MinimumUsableSwingCelsius = 3d;

    /// <summary>
    /// How many times the settled tail's noise the swing must exceed.
    /// </summary>
    /// <remarks>
    /// The gate the 8 °C constant was standing in for, made explicit. The fit reads two crossings out of the
    /// response; at six sigma of total swing each crossing sits about two sigma clear of the noise, which is
    /// where the timing points stop wandering. A steady machine (tail sigma near the 0.5 °C quantisation
    /// floor) is gated near the absolute minimum; a machine whose load is bouncing — measured ±4 °C on a
    /// governor oscillation — is asked for a swing that noise could not fake, which is the honest refusal.
    /// </remarks>
    public const double NoiseSwingMultiple = 6d;

    /// <summary>
    /// Fits the model from a step response.
    /// </summary>
    /// <param name="samples">
    /// Temperature over time from the moment the step was applied, in order. Time in seconds from the step,
    /// temperature in °C.
    /// </param>
    /// <param name="dutyStepPercent">The size of the duty step applied, in percentage points.</param>
    /// <returns>The identified model, or a failure describing why it could not be fitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    public static FopdtIdentificationResult Identify(
        IReadOnlyList<(double Seconds, double Celsius)> samples,
        double dutyStepPercent)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < 4 || !double.IsFinite(dutyStepPercent) || dutyStepPercent <= 0d)
        {
            return FopdtIdentificationResult.Failed(FanCalibrationFailure.InsufficientData, 0d);
        }

        // Denoised BEFORE anything reads it, with a CENTERED moving average — possible because the fit runs
        // after the recording, and decisive because a centred window has no group delay: a live trailing
        // average would shift the whole curve later and the shift would be read as extra dead time. The EC
        // quantises to whole degrees and the driving reading is a maximum over several such sensors, so the
        // raw trace is jumpy in both directions; a real run measured a genuine 6.5 °C swing and was refused
        // because the raw tail wobbled harder than the gate allows. Smoothing also feeds the noise gate the
        // RESIDUAL wobble, which is the honest input — the crossings are read off the smoothed curve.
        samples = SmoothCentered(samples);

        // The settled end, taken as the mean of the last fifth rather than the final sample, so one noisy
        // reading cannot set the asymptote the whole fit is measured against.
        var tailCount = Math.Max(1, samples.Count / 5);
        var end = samples.Skip(samples.Count - tailCount).Average(static sample => sample.Celsius);

        // The start is the mean of the dead-time plateau — every leading sample that has not yet fallen a
        // twentieth of the (provisional) swing. Inside the dead time the plant has not moved, so these are
        // the same physical value read repeatedly, and averaging denoises the start exactly as the tail mean
        // denoises the end; a single noisy first reading once under-reported a real 4-5 °C response as 2.7
        // and failed the run on it. Found adaptively rather than as a fixed count because the plateau's
        // LENGTH depends on the sample rate — a fast plant sampled coarsely has one flat sample, and
        // averaging past it would eat the response into its own baseline. Capped at a fifth so a sluggish
        // plant cannot swallow half its response either.
        var initial = samples[0].Celsius;
        var provisionalSwing = initial - end;
        var headLimit = Math.Max(1, samples.Count / 5);

        var headCount = 0;
        while (headCount < headLimit
            && initial - samples[headCount].Celsius <= 0.05d * provisionalSwing)
        {
            headCount++;
        }

        var start = samples.Take(Math.Max(1, headCount)).Average(static sample => sample.Celsius);

        // More fan means cooler, so a correct run falls. A rise means something else was happening — a
        // workload that grew during the measurement, most likely — and the fit would be meaningless.
        var swing = start - end;
        if (!double.IsFinite(swing) || swing <= 0d)
        {
            return FopdtIdentificationResult.Failed(FanCalibrationFailure.InsufficientTemperatureSwing, Math.Abs(swing));
        }

        // The settled tail is flat physics plus measurement noise, so its spread IS the noise estimate —
        // measured on the same run, same sensor, same conditions as the swing it gates.
        var tailNoiseSigma = Math.Sqrt(
            samples.Skip(samples.Count - tailCount).Average(sample => Math.Pow(sample.Celsius - end, 2)));

        var requiredSwing = Math.Max(MinimumUsableSwingCelsius, NoiseSwingMultiple * tailNoiseSigma);
        if (swing < requiredSwing)
        {
            return FopdtIdentificationResult.Failed(FanCalibrationFailure.InsufficientTemperatureSwing, swing);
        }

        var firstTime = TimeToFraction(samples, start, swing, FirstPointFraction);
        var secondTime = TimeToFraction(samples, start, swing, SecondPointFraction);

        if (firstTime is not double t28 || secondTime is not double t63 || t63 <= t28)
        {
            return FopdtIdentificationResult.Failed(FanCalibrationFailure.InsufficientData, swing);
        }

        var timeConstant = 1.5d * (t63 - t28);

        // Dead time can come out slightly negative on a fast plant with coarse sampling. That is measurement
        // noise, not a plant that responds before it is asked, so it floors at zero.
        var deadTime = Math.Max(0d, t63 - timeConstant);

        if (!double.IsFinite(timeConstant) || timeConstant <= 0d)
        {
            return FopdtIdentificationResult.Failed(FanCalibrationFailure.InsufficientData, swing);
        }

        return FopdtIdentificationResult.Succeeded(
            processGainCelsiusPerPercent: swing / dutyStepPercent,
            timeConstantSeconds: timeConstant,
            deadTimeSeconds: deadTime,
            temperatureSwingCelsius: swing);
    }

    /// <summary>
    /// A zero-phase moving average: each point becomes the mean of its symmetric neighbourhood.
    /// </summary>
    /// <remarks>
    /// The half-width scales with the RECORDING rather than being a fixed count or a fixed number of
    /// seconds, because both of those break at one sample rate or another — the recording's length is itself
    /// sized to the plant's time constant, so a fixed fraction of it is automatically a small fraction of τ
    /// at every rate. Edges shrink the window rather than padding, so the ends stay unbiased means of real
    /// readings. Short recordings pass through untouched.
    /// </remarks>
    private static IReadOnlyList<(double Seconds, double Celsius)> SmoothCentered(
        IReadOnlyList<(double Seconds, double Celsius)> samples)
    {
        var halfWidth = Math.Min(3, samples.Count / 80);
        if (halfWidth < 1)
        {
            return samples;
        }

        var smoothed = new (double Seconds, double Celsius)[samples.Count];

        for (var i = 0; i < samples.Count; i++)
        {
            var from = Math.Max(0, i - halfWidth);
            var to = Math.Min(samples.Count - 1, i + halfWidth);

            var sum = 0d;
            for (var j = from; j <= to; j++)
            {
                sum += samples[j].Celsius;
            }

            smoothed[i] = (samples[i].Seconds, sum / (to - from + 1));
        }

        return smoothed;
    }

    /// <summary>
    /// Finds when the response first crossed a fraction of its total change, interpolating between samples.
    /// </summary>
    /// <remarks>
    /// Linear interpolation rather than nearest-sample: at a one-second tick against a four-second dead time,
    /// rounding to the nearest sample would put a 25% error straight into L, and L is what bounds how
    /// aggressive the controller may be.
    /// </remarks>
    private static double? TimeToFraction(
        IReadOnlyList<(double Seconds, double Celsius)> samples,
        double start,
        double swing,
        double fraction)
    {
        var threshold = start - (swing * fraction);

        for (var i = 1; i < samples.Count; i++)
        {
            var (previousSeconds, previousCelsius) = samples[i - 1];
            var (seconds, celsius) = samples[i];

            if (celsius > threshold)
            {
                continue;
            }

            var span = previousCelsius - celsius;
            if (span <= 0d)
            {
                return seconds;
            }

            var share = (previousCelsius - threshold) / span;
            return previousSeconds + ((seconds - previousSeconds) * Math.Clamp(share, 0d, 1d));
        }

        return null;
    }
}

/// <summary>The outcome of fitting a step response.</summary>
/// <param name="IsSuccess">Whether a usable model came out.</param>
/// <param name="ProcessGainCelsiusPerPercent">K, when successful.</param>
/// <param name="TimeConstantSeconds">τ, when successful.</param>
/// <param name="DeadTimeSeconds">L, when successful.</param>
/// <param name="TemperatureSwingCelsius">How far the temperature actually moved — reported either way, because a failure caused by too small a swing has to say how small.</param>
/// <param name="Failure">Why it failed, when it did.</param>
public readonly record struct FopdtIdentificationResult(
    bool IsSuccess,
    double ProcessGainCelsiusPerPercent,
    double TimeConstantSeconds,
    double DeadTimeSeconds,
    double TemperatureSwingCelsius,
    FanCalibrationFailure Failure)
{
    /// <summary>A usable fit.</summary>
    public static FopdtIdentificationResult Succeeded(
        double processGainCelsiusPerPercent,
        double timeConstantSeconds,
        double deadTimeSeconds,
        double temperatureSwingCelsius)
        => new(true, processGainCelsiusPerPercent, timeConstantSeconds, deadTimeSeconds, temperatureSwingCelsius, FanCalibrationFailure.None);

    /// <summary>A fit that could not be made.</summary>
    public static FopdtIdentificationResult Failed(FanCalibrationFailure failure, double temperatureSwingCelsius)
        => new(false, 0d, 0d, 0d, temperatureSwingCelsius, failure);
}
