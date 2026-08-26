using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Service.Models;

/// <summary>
/// How long each step is expected to take, so a run can report overall progress rather than only a step name.
/// </summary>
/// <remarks>
/// <para>
/// A calibration is minutes long and mostly looks like nothing happening. "Step 3 of 9" tells a user almost
/// nothing about whether to wait or walk away — the steps are wildly unequal, with the response measurement
/// alone longer than four of the others together. Weighting by expected duration is what makes a progress bar
/// move at a believable rate instead of sitting still and then leaping.
/// </para>
/// <para>
/// These are ESTIMATES and some steps genuinely vary: settling ends when the machine settles, and the gain
/// sweep scales with the time constant that was just measured. Progress within a step is therefore clamped,
/// so a step that overruns its estimate stalls at its own boundary rather than running past into the next
/// step's share — a bar that never goes backwards is worth more than one that is precisely wrong.
/// </para>
/// </remarks>
public sealed class FanCalibrationSchedule
{
    private readonly IReadOnlyDictionary<FanCalibrationStep, TimeSpan> _expected;
    private readonly TimeSpan _total;

    public FanCalibrationSchedule(FanCalibrationTimings timings)
    {
        ArgumentNullException.ThrowIfNull(timings);

        // The minimum-spin walk visits 40% down to 5% in steps of five: eight dwells.
        const int minimumSpinLevels = 8;

        // Three intermediate duties, each held for a couple of time constants. Estimated with a typical
        // constant, because the real one is not known until the fit — which happens after this is needed.
        const int gainCurveLevels = 3;
        var typicalGainDwell = TimeSpan.FromSeconds(50);

        _expected = new Dictionary<FanCalibrationStep, TimeSpan>
        {
            [FanCalibrationStep.SettlingAtIdle] = timings.IdleSettle,
            [FanCalibrationStep.FindingMinimumSpin] = timings.MinimumSpinDwell * minimumSpinLevels,

            // The typical case, not the timeout: most machines settle well before the ceiling, and budgeting
            // the worst case would leave the bar crawling through a step that usually ends early.
            // TWO settle passes, because the load phase settles twice: once at the cool loaded point under
            // full fan, then again at the low hold it descends to. The ramp happens only during the first.
            [FanCalibrationStep.LoadingAndSettling] =
                timings.MinimumLoad + timings.SettleWindow + LoadRamp.DefaultDuration
                + timings.MinimumLoad + timings.SettleWindow,
            [FanCalibrationStep.SteppingFan] = TimeSpan.FromSeconds(1),
            [FanCalibrationStep.MeasuringResponse] = timings.Response,
            [FanCalibrationStep.FittingModel] = TimeSpan.FromSeconds(1),
            [FanCalibrationStep.VerifyingSpeedTracking] = timings.TrackingSettle,
            [FanCalibrationStep.MeasuringGainCurve] = (timings.GainCurveDwell ?? typicalGainDwell) * gainCurveLevels,
        };

        _total = _expected.Values.Aggregate(TimeSpan.Zero, static (sum, next) => sum + next);
    }

    /// <summary>How long the whole run is expected to take.</summary>
    public TimeSpan TotalEstimate => _total;

    /// <summary>
    /// Overall completion, 0–1, from the step being run and how far into it the run is.
    /// </summary>
    /// <param name="step">The step currently running.</param>
    /// <param name="elapsedInStep">How long that step has been running.</param>
    public double ProgressAt(FanCalibrationStep step, TimeSpan elapsedInStep)
    {
        if (_total <= TimeSpan.Zero)
        {
            return 0d;
        }

        if (step == FanCalibrationStep.Completed)
        {
            return 1d;
        }

        var done = TimeSpan.Zero;
        foreach (var (candidate, duration) in _expected)
        {
            if (candidate < step)
            {
                done += duration;
            }
        }

        var current = _expected.TryGetValue(step, out var expected) && expected > TimeSpan.Zero
            ? Math.Clamp(elapsedInStep / expected, 0d, 1d)
            : 0d;

        return Math.Clamp((done + (expected * current)) / _total, 0d, 1d);
    }

    /// <summary>Roughly how much longer, or null before there is enough to say.</summary>
    public TimeSpan? RemainingAt(FanCalibrationStep step, TimeSpan elapsedInStep)
    {
        var progress = ProgressAt(step, elapsedInStep);
        if (progress <= 0d)
        {
            return _total;
        }

        var remaining = _total * (1d - progress);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
