using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Turns an identified FOPDT plant model into PI gains, using SIMC (Skogestad) lambda tuning.
/// </summary>
/// <remarks>
/// <para>
/// For a first-order-plus-dead-time plant <c>K·e^(-Ls)/(τs+1)</c>, SIMC gives
/// <c>Kc = τ / (K·(λ + L))</c> and <c>τᵢ = min(τ, 4·(λ + L))</c>, hence <c>Kᵢ = Kc / τᵢ</c>.
/// </para>
/// <para>
/// SIMC rather than Ziegler-Nichols, and rather than plain IMC. ZN targets a quarter-amplitude decay — a
/// deliberately oscillatory response that on this plant either rings or has to be detuned into uselessness.
/// Plain IMC sets <c>τᵢ = τ</c> unconditionally; SIMC's <c>min(τ, 4(λ+L))</c> cap is what keeps integral
/// action from becoming uselessly slow on a lag-dominant fan, which is the difference between a controller
/// that rejects a load step and one that takes minutes to remove a standing offset.
/// </para>
/// <para>
/// One knob: λ, the closed-loop time constant, in SECONDS. Everything else is measured. λ is a user setting
/// rather than a constant here because it is the single legitimate taste decision in the loop — how quickly
/// the fan should chase a disturbance, traded against how often it audibly changes speed.
/// </para>
/// </remarks>
public static class AdaptivePidTuning
{
    /// <summary>
    /// The shipped default λ, in seconds: two dead times on a representative Framework fan.
    /// </summary>
    /// <remarks>
    /// Two dead times is the standard robust starting point — quick enough to catch a transient, slow enough
    /// that the fan is not audibly hunting. Expressed as an absolute value rather than a multiple of L so the
    /// number the user sees on the slider means the same thing on every machine.
    /// </remarks>
    public const double DefaultLambdaSeconds = 8d;

    /// <summary>The tightest λ offered: a fast, restless fan.</summary>
    public const double MinimumLambdaSeconds = 2d;

    /// <summary>The calmest λ offered: slow to react, rarely changes speed.</summary>
    public const double MaximumLambdaSeconds = 16d;

    /// <summary>
    /// Computes SIMC PI gains for an identified plant.
    /// </summary>
    /// <param name="processGainCelsiusPerPercent">K — °C of cooling per duty point, a positive magnitude.</param>
    /// <param name="timeConstantSeconds">τ — the plant time constant, in seconds.</param>
    /// <param name="deadTimeSeconds">L — the plant dead time, in seconds.</param>
    /// <param name="lambdaSeconds">λ — the desired closed-loop time constant, in seconds.</param>
    /// <returns>
    /// Proportional gain in duty points per °C, integral time in seconds, and integral gain in duty points
    /// per °C-second. All zero when the model is not physically usable.
    /// </returns>
    public static AdaptivePidGains Compute(
        double processGainCelsiusPerPercent,
        double timeConstantSeconds,
        double deadTimeSeconds,
        double lambdaSeconds = DefaultLambdaSeconds)
    {
        // A non-positive gain or time constant is not a slow fan, it is a failed identification. Zero gains
        // disable the feedback path rather than dividing by it.
        if (!double.IsFinite(processGainCelsiusPerPercent) || processGainCelsiusPerPercent <= 0d
            || !double.IsFinite(timeConstantSeconds) || timeConstantSeconds <= 0d
            || !double.IsFinite(deadTimeSeconds) || deadTimeSeconds < 0d)
        {
            return AdaptivePidGains.None;
        }

        var lambda = ClampLambda(lambdaSeconds);

        var divisor = processGainCelsiusPerPercent * (lambda + deadTimeSeconds);
        if (divisor <= 0d || !double.IsFinite(divisor))
        {
            return AdaptivePidGains.None;
        }

        var proportionalGain = timeConstantSeconds / divisor;
        var integralTimeSeconds = Math.Min(timeConstantSeconds, 4d * (lambda + deadTimeSeconds));

        if (integralTimeSeconds <= 0d || !double.IsFinite(integralTimeSeconds))
        {
            return AdaptivePidGains.None;
        }

        return new AdaptivePidGains(proportionalGain, integralTimeSeconds, proportionalGain / integralTimeSeconds);
    }

    /// <summary>Computes gains for a calibrated fan at a chosen λ.</summary>
    /// <param name="calibration">The identified plant.</param>
    /// <param name="lambdaSeconds">λ, in seconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="calibration"/> is null.</exception>
    public static AdaptivePidGains Compute(FanCalibrationSnapshot calibration, double lambdaSeconds = DefaultLambdaSeconds)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        return Compute(
            calibration.ProcessGainCelsiusPerPercent,
            calibration.TimeConstantSeconds,
            calibration.DeadTimeSeconds,
            lambdaSeconds);
    }

    /// <summary>Pulls λ into the range the UI offers.</summary>
    public static double ClampLambda(double lambdaSeconds)
        => double.IsFinite(lambdaSeconds)
            ? Math.Clamp(lambdaSeconds, MinimumLambdaSeconds, MaximumLambdaSeconds)
            : DefaultLambdaSeconds;

    /// <summary>
    /// Roughly how long the loop takes to come back on target after a load step, in seconds.
    /// </summary>
    /// <remarks>
    /// For the settings UI's "back on target within {n} s" readout. An approximation of the closed-loop
    /// settling time, not a guarantee — the real answer depends on the disturbance.
    /// </remarks>
    public static double EstimateSettlingSeconds(double lambdaSeconds, double deadTimeSeconds)
        => (ClampLambda(lambdaSeconds) + Math.Max(0d, deadTimeSeconds)) * 3.4d;
}
