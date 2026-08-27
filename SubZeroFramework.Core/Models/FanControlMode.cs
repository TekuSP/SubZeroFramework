namespace SubZeroFramework.Models;

public enum FanControlMode
{
    Auto = 0,
    Manual = 1,
    CustomCurve = 2,
    Max = 3,

    /// <summary>
    /// Closed-loop control to a target temperature, using the fan's calibrated model. Requires a usable
    /// <see cref="FanCalibrationSnapshot"/> — an uncalibrated fan cannot be armed into this mode.
    /// </summary>
    Adaptive = 4,
}