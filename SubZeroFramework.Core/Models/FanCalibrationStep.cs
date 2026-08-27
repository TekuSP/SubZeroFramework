namespace SubZeroFramework.Models;

/// <summary>
/// The stages of a calibration run, in the order they execute.
/// </summary>
/// <remarks>
/// The ordering is physics, not presentation. Identifying K — °C of cooling per duty point — requires varying
/// DUTY while everything else holds still, so the load is applied and allowed to settle FIRST, and only then
/// is the fan stepped. Stepping the fan and the load together would confound the two and produce a number
/// that describes neither.
/// </remarks>
public enum FanCalibrationStep
{
    /// <summary>Not running.</summary>
    None = 0,

    /// <summary>Letting the machine settle so the baseline is a real idle rather than the tail of something else.</summary>
    SettlingAtIdle = 1,

    /// <summary>Walking the duty down to find the lowest speed the fan reliably keeps turning at.</summary>
    FindingMinimumSpin = 2,

    /// <summary>Loading the CPU deliberately, and holding a low duty until the temperature stops climbing.</summary>
    LoadingAndSettling = 3,

    /// <summary>The step itself: fan to maximum, load unchanged.</summary>
    SteppingFan = 4,

    /// <summary>Recording the temperature fall the step produced.</summary>
    MeasuringResponse = 5,

    /// <summary>Fitting K, τ and L to what was recorded.</summary>
    FittingModel = 6,

    /// <summary>Checking whether the EC actually holds a commanded speed — cascade, or duty fallback.</summary>
    VerifyingSpeedTracking = 7,

    /// <summary>
    /// Walking back down through intermediate duties to measure how cooling varies across the range.
    /// </summary>
    /// <remarks>
    /// After the fit, because it depends on it: knowing τ lets each level's settled temperature be
    /// extrapolated from a partial transient instead of waited out in full, which is the difference between
    /// a few extra minutes and quarter of an hour.
    /// </remarks>
    MeasuringGainCurve = 8,

    /// <summary>Finished, with a model stored.</summary>
    Completed = 9,

    /// <summary>
    /// Between attempts: heat off, every fan under firmware, waiting to cool before a retry.
    /// </summary>
    /// <remarks>
    /// Deliberately numbered PAST <see cref="Completed"/>, which keeps it out of the step count — the
    /// ordinal-based "Step N of M" derivation counts the steps before Completed, and a pause is not a step.
    /// Anything ordering steps numerically must treat it as unordered, not as "after finished".
    /// </remarks>
    CoolingDown = 10,
}
