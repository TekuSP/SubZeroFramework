namespace SubZeroFramework.Models;

/// <summary>
/// The exponential feed-forward math behind the per-fan CPU usage modifier. Lives in Core so a future
/// client-side preview renders exactly what the service actuates (the same principle the curve
/// interpolation follows).
/// </summary>
public static class FanUsageModifierMath
{
    /// <summary>
    /// Shape constant for the exponential ramp. At k = 4 the boost is ~2% of strength at 25% usage,
    /// ~12% at 50%, ~46% at 75% and 100% at full usage — so fans spin up hard only when the CPU is
    /// genuinely pinned, before the heat shows up in the sensors.
    /// </summary>
    public const double Exponent = 4d;

    /// <summary>
    /// Computes the duty points to add on top of a curve-interpolated duty:
    /// strength × (e^(k·usage) − 1) / (e^k − 1), which is 0 at idle and exactly
    /// <paramref name="strength"/> at 100% usage. Returns 0 when the modifier is disabled
    /// (null/NaN strength) or no usage reading is available. The caller clamps the summed duty.
    /// </summary>
    public static double ComputeBoost(double? strength, double? usageFraction)
    {
        if (strength is not double strengthValue
            || double.IsNaN(strengthValue)
            || strengthValue <= 0d
            || usageFraction is not double usageValue
            || double.IsNaN(usageValue))
        {
            return 0d;
        }

        var usage = Math.Clamp(usageValue, 0d, 1d);
        return strengthValue * (Math.Exp(Exponent * usage) - 1d) / (Math.Exp(Exponent) - 1d);
    }
}
