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
    /// Smallest temperature swing that can be fitted, in °C.
    /// </summary>
    /// <remarks>
    /// Below this the sensor's own quantisation and noise are a large fraction of the signal, and the timing
    /// points land essentially at random. A cool room or a well-ventilated dock is the usual cause, and the
    /// honest answer is to tell the user rather than return a model built from noise.
    /// </remarks>
    public const double MinimumUsableSwingCelsius = 8d;

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

        var start = samples[0].Celsius;

        // The settled end, taken as the mean of the last fifth rather than the final sample, so one noisy
        // reading cannot set the asymptote the whole fit is measured against.
        var tailCount = Math.Max(1, samples.Count / 5);
        var end = samples.Skip(samples.Count - tailCount).Average(static sample => sample.Celsius);

        // More fan means cooler, so a correct run falls. A rise means something else was happening — a
        // workload that grew during the measurement, most likely — and the fit would be meaningless.
        var swing = start - end;
        if (!double.IsFinite(swing) || swing <= 0d)
        {
            return FopdtIdentificationResult.Failed(FanCalibrationFailure.InsufficientTemperatureSwing, Math.Abs(swing));
        }

        if (swing < MinimumUsableSwingCelsius)
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
