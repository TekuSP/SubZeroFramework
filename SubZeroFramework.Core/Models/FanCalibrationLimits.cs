namespace SubZeroFramework.Models;

/// <summary>
/// The thresholds a calibration run is judged against.
/// </summary>
/// <remarks>
/// In Core rather than beside the runner because the UI needs them too: a failure screen that says the
/// machine was not busy enough has to say how busy it needed to be, and that number appearing in two places
/// is a guarantee they eventually disagree — with the user reading the stale one.
/// </remarks>
public static class FanCalibrationLimits
{
    /// <summary>
    /// Average package power the run must reach for the result to mean anything.
    /// </summary>
    /// <remarks>
    /// Below this the temperature rise is too small for the fall after the step to be separable from noise,
    /// so the fit would return a confident-looking model built from very little.
    /// </remarks>
    public const double MinimumAveragePowerWatts = 25d;

    /// <summary>
    /// Hard temperature ceiling. Passing it aborts the run, whatever else is happening.
    /// </summary>
    /// <remarks>
    /// A calibration is the one operation that deliberately pushes temperature up, so it is also the one that
    /// must be most willing to give up.
    /// </remarks>
    public const double SafetyCeilingCelsius = 95d;
}
