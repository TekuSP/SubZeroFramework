using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Keeps a fan's thermal model true over time, by identifying it from ordinary use and merging that with
/// whatever a calibration run measured.
/// </summary>
/// <remarks>
/// <para>
/// <b>Calibration and self-learning are not alternatives — they do different jobs.</b> A hot test is a
/// controlled experiment: it excites the plant deliberately, so it can measure things ordinary use never
/// reveals — dead time, the time constant, the stall point, whether the EC tracks commanded speed — and it
/// does so in minutes. What it cannot do is stay true. A machine gathers dust, its paste degrades, it moves
/// to a warmer room, it gets a heavier workload than the test used. Identification from live operation
/// tracks all of that, and needs no cooperation from the user.
/// </para>
/// <para>
/// So the model this exposes is a MERGE: calibrated values are the baseline, and identified values override
/// the two parameters live operation can actually resolve — the process gain K and, derived from it, the
/// feed-forward gain. Dead time, time constant, stall point and tracking mode stay whatever calibration
/// measured, because a machine sitting at steady state carries no information about them.
/// </para>
/// <para>
/// <b>Why identification is safe here.</b> This is INDIRECT adaptive control: identify the plant, then
/// re-derive the controller through a fixed tuning rule (<see cref="AdaptivePidTuning"/>). The rule never
/// changes, so whatever model comes out, the gains are the ones that rule would have produced for a machine
/// with those parameters. Perturbing the gains directly from closed-loop error — the obvious alternative —
/// is the classic route to a self-tuning regulator that drifts into oscillation.
/// </para>
/// </remarks>
public sealed class AdaptiveModelLearner
{
    /// <summary>Temperature slope, in °C/s, below which the plant counts as settled.</summary>
    public const double SteadyStateSlopeCelsiusPerSecond = 0.05d;

    /// <summary>How long the settled conditions must hold continuously before a sample is taken.</summary>
    /// <remarks>
    /// The fit assumes each sample sits on the steady-state surface. A machine passing through an operating
    /// point on its way somewhere else does not, and fitting to it would identify the dynamics as if they
    /// were the steady state.
    /// </remarks>
    public static readonly TimeSpan SteadyStateDwell = TimeSpan.FromSeconds(30);

    /// <summary>Minimum spacing between accepted samples.</summary>
    /// <remarks>
    /// Without it a machine sitting at one load contributes a sample every tick, and recursive least squares
    /// with forgetting would converge hard onto that single operating point — discarding the spread across
    /// loads that makes the fit separable in the first place.
    /// </remarks>
    public static readonly TimeSpan ObservationInterval = TimeSpan.FromSeconds(30);

    /// <summary>Below this package power, no sample is taken.</summary>
    /// <remarks>
    /// Near idle the fan is often off and the temperature is ambient-dominated, so the sample carries almost
    /// no information about K while still consuming a slot in the forgetting window.
    /// </remarks>
    public const double MinimumObservationWatts = 8d;

    /// <summary>How far an identified gain may stray from a calibrated one, as a ratio.</summary>
    /// <remarks>
    /// Only applies when there IS a calibration to anchor against. It measured this chassis under controlled
    /// excitation; identification refines it, but does not get to overrule it by an order of magnitude. A
    /// value pinned at the bound is the signal that something changed physically — a blocked vent, a failing
    /// fan — and the honest response is to prompt for a recalibration, not to keep drifting.
    /// </remarks>
    public const double MaximumDeviationRatio = 3d;

    /// <summary>How far a calibrated gain must move before it counts as a NEW calibration rather than noise.</summary>
    public const double RecalibrationEpsilon = 1e-9d;

    private readonly AdaptivePlantEstimator _estimator = new();

    private double _calibratedFeedForwardDutyPerWatt;
    private TimeSpan _steadyDuration;
    private TimeSpan _sinceLastObservation;

    /// <summary>Creates a learner, optionally resuming a previously identified model.</summary>
    /// <param name="state">Previously learned state, or null to start from the calibration alone.</param>
    public AdaptiveModelLearner(AdaptiveLearningState? state = null)
    {
        State = state ?? AdaptiveLearningState.None;
        _sinceLastObservation = ObservationInterval;

        if (State.HasLearned)
        {
            _estimator.Restore(State);
        }
    }

    /// <summary>What has been identified so far.</summary>
    public AdaptiveLearningState State { get; private set; }

    /// <summary>
    /// Points the learner at the calibration it is refining.
    /// </summary>
    /// <param name="calibratedFeedForwardDutyPerWatt">The gain the current calibration measured.</param>
    /// <remarks>
    /// Called every tick with the live calibration, which makes recalibration handle itself: a fresh hot test
    /// is a controlled re-measurement of the machine as it is NOW, and supersedes anything identified around
    /// the model it replaced.
    /// </remarks>
    public void Anchor(double calibratedFeedForwardDutyPerWatt)
    {
        var anchor = double.IsFinite(calibratedFeedForwardDutyPerWatt) && calibratedFeedForwardDutyPerWatt > 0d
            ? calibratedFeedForwardDutyPerWatt
            : 0d;

        if (Math.Abs(anchor - _calibratedFeedForwardDutyPerWatt) <= RecalibrationEpsilon)
        {
            return;
        }

        _calibratedFeedForwardDutyPerWatt = anchor;

        // Only a genuine RECALIBRATION discards identification, and the anchor recorded in the state is how
        // that is told apart from this learner simply being told where it starts. Comparing against the field
        // would wipe resumed state on the very first call, because the field starts at zero.
        if (State.HasLearned
            && State.CalibratedAnchorDutyPerWatt is double previousAnchor
            && Math.Abs(anchor - previousAnchor) > RecalibrationEpsilon)
        {
            State = AdaptiveLearningState.None;
        }

        ResetDwell();
    }

    /// <summary>
    /// Returns the model the controller should actually run on: the calibration, with identified parameters
    /// overriding the ones live operation can resolve.
    /// </summary>
    /// <param name="calibration">What calibration measured, or <see cref="FanCalibrationSnapshot.None"/>.</param>
    /// <returns>The merged model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="calibration"/> is null.</exception>
    public FanCalibrationSnapshot EffectiveModel(FanCalibrationSnapshot calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        // No hot test yet: start from the conservative bootstrap rather than refusing to run. This is the
        // inversion — a fan on safe defaults is a working fan, and identification improves it from there.
        // A calibration that is absent OR physically nonsense (a failed run can produce a zero gain) is no
        // model at all. Falling back to the bootstrap keeps the fan working; refusing would strand it over a
        // bad number nobody can see.
        var baseline = calibration.IsUsable ? calibration : FanCalibrationSnapshot.Bootstrap;

        // Bounded against the CALIBRATED anchor only. A bootstrap value is a guess, not a measurement, so it
        // has no authority to constrain something actually identified from this machine.
        var anchorGain = calibration.State == FanCalibrationState.None ? 0d : calibration.ProcessGainCelsiusPerPercent;
        var identifiedGain = Bounded(State.IdentifiedProcessGainCelsiusPerPercent, anchorGain);
        var identifiedFeedForward = Bounded(State.FeedForwardDutyPerWatt, _calibratedFeedForwardDutyPerWatt);

        if (identifiedGain is null && identifiedFeedForward is null)
        {
            return baseline;
        }

        return baseline with
        {
            ProcessGainCelsiusPerPercent = identifiedGain ?? baseline.ProcessGainCelsiusPerPercent,
            FeedForwardDutyPerWatt = identifiedFeedForward ?? baseline.FeedForwardDutyPerWatt,
        };
    }

    /// <summary>The feed-forward gain to use, merged as above.</summary>
    public double EffectiveFeedForwardDutyPerWatt
        => Bounded(State.FeedForwardDutyPerWatt, _calibratedFeedForwardDutyPerWatt) ?? _calibratedFeedForwardDutyPerWatt;

    /// <summary>
    /// Offers one tick, which either advances the settled dwell or resets it, and folds a sample into the fit
    /// when everything qualifies.
    /// </summary>
    /// <param name="observation">This tick's plant conditions.</param>
    /// <param name="elapsed">Time since the previous tick.</param>
    /// <param name="timestamp">Now, stamped onto an accepted sample.</param>
    /// <returns>True when this tick produced an accepted sample.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="observation"/> is null.</exception>
    public bool Observe(AdaptiveLearningObservation observation, TimeSpan elapsed, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (elapsed > TimeSpan.Zero)
        {
            _sinceLastObservation += elapsed;
        }

        if (!IsQualified(observation))
        {
            _steadyDuration = TimeSpan.Zero;
            return false;
        }

        if (elapsed > TimeSpan.Zero)
        {
            _steadyDuration += elapsed;
        }

        if (_steadyDuration < SteadyStateDwell || _sinceLastObservation < ObservationInterval)
        {
            return false;
        }

        // Captured before the fit moves, so the material-change comparison below is against the value the
        // controller was actually running on.
        var previousGain = _estimator.ProcessGainCelsiusPerPercent;

        _estimator.Observe(
            observation.TemperatureCelsius,
            observation.PackagePowerWatts,
            observation.CommandedDutyPercent);

        _sinceLastObservation = TimeSpan.Zero;

        // Publish only once the fit is separable. Until then the machine keeps running on the calibration,
        // which is the correct answer rather than a placeholder.
        if (!_estimator.HasSufficientExcitation)
        {
            // The source is claimed from the first accepted sample, not only once the fit publishes — the
            // samples being accumulated right now are already conditional on it.
            State = State with
            {
                ObservationCount = _estimator.ObservationCount,
                ThermalLoadSource = observation.ThermalLoadSource,
            };
            return true;
        }

        var identifiedGain = _estimator.ProcessGainCelsiusPerPercent;
        var hasMovedMaterially = HasMovedMaterially(previousGain, identifiedGain);

        // A point per MATERIAL move, not per observation: the estimator nudges K on almost every sample, so
        // recording each one would fill a bounded, persisted history with jitter and push the real drift off
        // the end. The first separable fit counts as a move (previousGain is null), so the history always
        // opens with where the model started.
        var gainHistory = hasMovedMaterially && identifiedGain is double movedGain
            ? State.AppendGainSample(new AdaptiveGainSample(timestamp, movedGain))
            : State.GainHistory;

        State = State with
        {
            GainHistory = gainHistory,
            IdentifiedProcessGainCelsiusPerPercent = identifiedGain,
            IdentifiedCelsiusPerWatt = _estimator.CelsiusPerWatt,
            IdentifiedInterceptCelsius = _estimator.InterceptCelsius,
            FeedForwardDutyPerWatt = _estimator.FeedForwardDutyPerWatt,
            CalibratedAnchorDutyPerWatt = _calibratedFeedForwardDutyPerWatt > 0d ? _calibratedFeedForwardDutyPerWatt : null,
            ThermalLoadSource = observation.ThermalLoadSource,
            ObservationCount = _estimator.ObservationCount,
            LastUpdatedAt = timestamp,
            LastMaterialChangeAt = hasMovedMaterially
                ? timestamp
                : State.LastMaterialChangeAt,
        };

        return true;
    }

    /// <summary>Drops the dwell timers, without discarding what has been identified.</summary>
    /// <remarks>
    /// Called when the fan stops being adaptively driven. The MODEL survives — it describes the chassis,
    /// which did not change because the fan spent an hour in Manual — but the settled evidence in flight does
    /// not, because the loop that produced it is no longer running.
    /// </remarks>
    public void ResetDwell()
    {
        _steadyDuration = TimeSpan.Zero;
        _sinceLastObservation = ObservationInterval;
    }

    /// <summary>
    /// Whether the identified gain moved enough to count as the model changing rather than jittering.
    /// </summary>
    /// <remarks>
    /// The first separable fit always counts as a change: going from "no model" to "a model" is the most
    /// material thing that ever happens to this estimate.
    /// </remarks>
    private static bool HasMovedMaterially(double? previous, double? current)
    {
        if (current is not double now)
        {
            return false;
        }

        if (previous is not double before || before <= 0d)
        {
            return true;
        }

        return Math.Abs(now - before) / before > AdaptiveLearningState.MaterialChangeFraction;
    }

    /// <summary>
    /// Whether this sample's load source matches the one the fit was built on, adopting it if there is none.
    /// </summary>
    /// <remarks>
    /// The whole fit is conditional on the source. Unplugging a charger swaps system power for component
    /// power (or for nothing at all), and folding those samples into the same fit would move <c>b</c> toward
    /// a coupling that describes neither — with no symptom until the fan behaves oddly days later.
    /// </remarks>
    private bool IsSourceConsistent(ThermalLoadSource source)
    {
        if (source == ThermalLoadSource.None)
        {
            return false;
        }

        if (State.ThermalLoadSource == ThermalLoadSource.None)
        {
            return true;
        }

        return State.ThermalLoadSource == source;
    }

    private bool IsQualified(AdaptiveLearningObservation observation)
    {
        if (!IsSourceConsistent(observation.ThermalLoadSource))
        {
            return false;
        }

        // Saturated: the loop is not holding anything, so this point is not on the steady-state surface.
        if (observation.IsSaturated || observation.IsThrottleLatched)
        {
            return false;
        }

        if (!double.IsFinite(observation.PackagePowerWatts) || observation.PackagePowerWatts < MinimumObservationWatts)
        {
            return false;
        }

        if (!double.IsFinite(observation.CommandedDutyPercent) || !double.IsFinite(observation.TemperatureCelsius))
        {
            return false;
        }

        return double.IsFinite(observation.TemperatureSlopeCelsiusPerSecond)
            && Math.Abs(observation.TemperatureSlopeCelsiusPerSecond) <= SteadyStateSlopeCelsiusPerSecond;
    }

    /// <summary>
    /// Bounds an identified value against its calibrated anchor, when there is one.
    /// </summary>
    /// <remarks>
    /// With no calibration the identified value stands on its own — there is nothing better to believe, and
    /// the estimator's own physical-plausibility limits already reject nonsense.
    /// </remarks>
    private static double? Bounded(double? identified, double anchor)
    {
        if (identified is not double value || !double.IsFinite(value) || value <= 0d)
        {
            return null;
        }

        return anchor > 0d
            ? Math.Clamp(value, anchor / MaximumDeviationRatio, anchor * MaximumDeviationRatio)
            : value;
    }
}
