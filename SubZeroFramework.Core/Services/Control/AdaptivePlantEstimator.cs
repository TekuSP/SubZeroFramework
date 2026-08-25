using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Identifies a fan's steady-state thermal model from ordinary use, by recursive least squares over settled
/// operating points.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model.</b> At steady state a machine sits where heat in equals heat out:
/// <c>T ≈ a + b·P − K·duty</c>, where <c>a</c> is the ambient-ish intercept, <c>b</c> is °C per watt of load,
/// and <c>K</c> is °C of cooling per duty point — the same K a hot test measures. Three unknowns, one linear
/// equation per settled sample.
/// </para>
/// <para>
/// <b>Why this is safe where adapting gains directly is not.</b> This is INDIRECT adaptive control: identify
/// the plant, then re-derive the controller through a fixed, known-good tuning rule (SIMC). The tuning rule
/// never changes, so whatever model comes out, the gains it produces are the gains that rule would have
/// produced for a machine with those parameters. Contrast with perturbing Kc and Kᵢ directly from closed-loop
/// error, which is the classic route to a self-tuning regulator that drifts into oscillation.
/// </para>
/// <para>
/// <b>Feed-forward falls out for free.</b> Holding <c>T = target</c> and solving for duty gives
/// <c>duty = (a + b·P − target)/K</c>, so <c>d(duty)/dP = b/K</c> — the feed-forward gain, in duty points per
/// watt, consistent with K by construction rather than estimated separately and allowed to disagree with it.
/// </para>
/// <para>
/// <b>Excitation is the hard requirement.</b> Least squares can only separate <c>b·P</c> from <c>K·duty</c> if
/// the samples actually differ in both. A machine that sat at one operating point all day provides one point
/// repeated, and fitting three parameters to it yields nonsense with high apparent confidence. Hence the
/// spread checks below: the estimate is not published until the observations span a real range of both.
/// </para>
/// </remarks>
public sealed class AdaptivePlantEstimator
{
    /// <summary>Parameters in the fit: intercept, °C per watt, °C per duty point.</summary>
    private const int ParameterCount = 3;

    /// <summary>
    /// Forgetting factor: how much each new observation discounts the accumulated history.
    /// </summary>
    /// <remarks>
    /// 0.98 over observations spaced tens of seconds apart gives a memory of roughly the last fifty settled
    /// points — long enough to average out sensor noise, short enough that a machine which physically changed
    /// (dust, a new heatsink, a different ambient) converges to its new behaviour within a session rather than
    /// being anchored to last month.
    /// </remarks>
    public const double ForgettingFactor = 0.98d;

    /// <summary>Duty spread, in points, the observations must cover before the fit is trusted.</summary>
    public const double RequiredDutySpreadPercent = 12d;

    /// <summary>Power spread, in watts, the observations must cover before the fit is trusted.</summary>
    public const double RequiredPowerSpreadWatts = 12d;

    /// <summary>Settled observations required before the fit is trusted.</summary>
    public const int RequiredObservationCount = 12;

    /// <summary>The lowest process gain treated as physically real, in °C per duty point.</summary>
    /// <remarks>
    /// Below this the fan would be doing essentially nothing, and dividing by it produces enormous gains.
    /// A fit this low is far more likely to be a degenerate least-squares solution than a real fan.
    /// </remarks>
    public const double MinimumProcessGain = 0.05d;

    /// <summary>The highest process gain treated as physically real.</summary>
    public const double MaximumProcessGain = 3d;

    private readonly double[] _theta = new double[ParameterCount];
    private readonly double[,] _covariance = new double[ParameterCount, ParameterCount];

    private double _minimumDuty = double.MaxValue;
    private double _maximumDuty = double.MinValue;
    private double _minimumPower = double.MaxValue;
    private double _maximumPower = double.MinValue;

    /// <summary>Creates an estimator with no observations.</summary>
    public AdaptivePlantEstimator()
    {
        // A large initial covariance says "we know nothing", so the first observations move the estimate
        // freely instead of being damped toward an arbitrary starting guess.
        for (var i = 0; i < ParameterCount; i++)
        {
            _covariance[i, i] = 1_000d;
        }
    }

    /// <summary>How many settled observations have been folded in.</summary>
    public int ObservationCount { get; private set; }

    /// <summary>
    /// True when the observations span enough of both duty and power for the fit to be separable.
    /// </summary>
    public bool HasSufficientExcitation
        => ObservationCount >= RequiredObservationCount
            && _maximumDuty - _minimumDuty >= RequiredDutySpreadPercent
            && _maximumPower - _minimumPower >= RequiredPowerSpreadWatts;

    /// <summary>
    /// The identified process gain K, in °C per duty point, or null while the fit is not yet trustworthy.
    /// </summary>
    public double? ProcessGainCelsiusPerPercent
    {
        get
        {
            // theta[2] is the coefficient of duty in T = a + b·P + c·duty, so K = -c: more duty, less heat.
            var gain = -_theta[2];
            return HasSufficientExcitation && double.IsFinite(gain) && gain is >= MinimumProcessGain and <= MaximumProcessGain
                ? gain
                : null;
        }
    }

    /// <summary>The fit's intercept, in °C. Persisted so a restored fit resumes rather than restarts.</summary>
    public double? InterceptCelsius => HasSufficientExcitation && double.IsFinite(_theta[0]) ? _theta[0] : null;

    /// <summary>Thermal resistance b, in °C per watt, or null while the fit is not yet trustworthy.</summary>
    public double? CelsiusPerWatt
    {
        get
        {
            var resistance = _theta[1];
            return HasSufficientExcitation && double.IsFinite(resistance) && resistance > 0d
                ? resistance
                : null;
        }
    }

    /// <summary>
    /// The feed-forward gain implied by the fit, in duty points per watt, or null while untrustworthy.
    /// </summary>
    /// <remarks>
    /// <c>b/K</c> — see the type remarks. Consistent with <see cref="ProcessGainCelsiusPerPercent"/> by
    /// construction, which is the point of deriving it here rather than estimating it separately.
    /// </remarks>
    public double? FeedForwardDutyPerWatt
    {
        get
        {
            if (ProcessGainCelsiusPerPercent is not double gain || CelsiusPerWatt is not double resistance)
            {
                return null;
            }

            var feedForward = resistance / gain;
            return double.IsFinite(feedForward) && feedForward > 0d ? feedForward : null;
        }
    }

    /// <summary>
    /// Folds one SETTLED operating point into the fit.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for only offering settled points; this type has no notion of transients and
    /// would happily fit a model to a machine mid-ramp, which would be meaningless.
    /// </remarks>
    /// <param name="temperatureCelsius">The driving temperature.</param>
    /// <param name="packagePowerWatts">The load, in watts.</param>
    /// <param name="dutyPercent">The duty holding it.</param>
    public void Observe(double temperatureCelsius, double packagePowerWatts, double dutyPercent)
    {
        if (!double.IsFinite(temperatureCelsius) || !double.IsFinite(packagePowerWatts) || !double.IsFinite(dutyPercent))
        {
            return;
        }

        double[] regressors = [1d, packagePowerWatts, dutyPercent];

        // Standard RLS with exponential forgetting:
        //   gain    = P·φ / (λ + φᵀ·P·φ)
        //   θ      += gain · (y − φᵀ·θ)
        //   P       = (P − gain·φᵀ·P) / λ
        var covarianceTimesRegressors = new double[ParameterCount];
        for (var i = 0; i < ParameterCount; i++)
        {
            var sum = 0d;
            for (var j = 0; j < ParameterCount; j++)
            {
                sum += _covariance[i, j] * regressors[j];
            }

            covarianceTimesRegressors[i] = sum;
        }

        var denominator = ForgettingFactor;
        for (var i = 0; i < ParameterCount; i++)
        {
            denominator += regressors[i] * covarianceTimesRegressors[i];
        }

        if (denominator <= 0d || !double.IsFinite(denominator))
        {
            return;
        }

        var prediction = 0d;
        for (var i = 0; i < ParameterCount; i++)
        {
            prediction += regressors[i] * _theta[i];
        }

        var error = temperatureCelsius - prediction;
        for (var i = 0; i < ParameterCount; i++)
        {
            _theta[i] += covarianceTimesRegressors[i] / denominator * error;
        }

        for (var i = 0; i < ParameterCount; i++)
        {
            for (var j = 0; j < ParameterCount; j++)
            {
                _covariance[i, j] = (_covariance[i, j] - (covarianceTimesRegressors[i] * covarianceTimesRegressors[j] / denominator)) / ForgettingFactor;
            }
        }

        _minimumDuty = Math.Min(_minimumDuty, dutyPercent);
        _maximumDuty = Math.Max(_maximumDuty, dutyPercent);
        _minimumPower = Math.Min(_minimumPower, packagePowerWatts);
        _maximumPower = Math.Max(_maximumPower, packagePowerWatts);
        ObservationCount++;
    }

    /// <summary>Restores a previously identified fit, so a machine resumes where it left off.</summary>
    /// <param name="state">The persisted estimate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public void Restore(AdaptiveLearningState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IdentifiedProcessGainCelsiusPerPercent is not double gain
            || state.IdentifiedCelsiusPerWatt is not double resistance)
        {
            return;
        }

        _theta[0] = state.IdentifiedInterceptCelsius ?? 0d;
        _theta[1] = resistance;
        _theta[2] = -gain;
        ObservationCount = state.ObservationCount;

        // Restore the spread as satisfied: the persisted estimate only exists because it was published, and
        // publication already required excitation. Re-earning it every restart would throw away a converged
        // model on every service update.
        _minimumDuty = 0d;
        _maximumDuty = RequiredDutySpreadPercent;
        _minimumPower = 0d;
        _maximumPower = RequiredPowerSpreadWatts;

        // A restored fit is trusted but not frozen: shrink the covariance so new observations refine it
        // rather than yanking it, which is what a large "know nothing" covariance would do.
        for (var i = 0; i < ParameterCount; i++)
        {
            for (var j = 0; j < ParameterCount; j++)
            {
                _covariance[i, j] = i == j ? 1d : 0d;
            }
        }
    }
}
