namespace SubZeroFramework.Models;

/// <summary>How the adaptive controller's speed demand reaches the fan.</summary>
public enum FanSpeedTrackingMode
{
    /// <summary>
    /// Command duty directly. The fallback, and the safe default: every Framework EC accepts a duty write,
    /// so a fan whose speed tracking was never verified still runs.
    /// </summary>
    Duty = 0,

    /// <summary>
    /// Cascade: command an RPM setpoint and let the EC's own speed loop hold it. Preferred where calibration
    /// verified it — the firmware closes a faster inner loop than user space can, which rejects fan-curve
    /// non-linearity and gives a steadier, quieter result for the same average airflow.
    /// </summary>
    Cascade = 1,
}
