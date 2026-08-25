namespace SubZeroFramework.Models;

/// <summary>How much of a fan's learned thermal model is usable right now.</summary>
public enum FanCalibrationState
{
    /// <summary>
    /// Nothing measured and nothing identified. Adaptive still runs — on
    /// <see cref="FanCalibrationSnapshot.Bootstrap"/> — because a fan on safe defaults is a working fan.
    /// </summary>
    None = 0,

    /// <summary>Calibrated and current.</summary>
    Ok = 1,

    /// <summary>
    /// Calibrated, but long enough ago that the machine it describes may not be the machine in front of the
    /// user any more — dust, a new heatsink, a different ambient. Adaptive still runs; the UI nags.
    /// </summary>
    Stale = 2,

    /// <summary>
    /// Running on conservative defaults while identification gathers evidence. Not a failure state, and not
    /// something to warn about — this is what every fan looks like on its first day.
    /// </summary>
    Bootstrap = 3,
}
