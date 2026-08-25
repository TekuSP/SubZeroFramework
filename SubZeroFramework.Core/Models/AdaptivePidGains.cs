namespace SubZeroFramework.Models;

/// <summary>
/// PI gains derived from an identified plant, as SIMC produces them.
/// </summary>
/// <remarks>
/// Derived at runtime from the calibration plus the user's λ, rather than stored, because λ is adjustable
/// without recalibrating: the plant did not change, only how hard the loop is asked to chase it. Integral
/// time is carried alongside the gains because it is what the settings page shows the user — "how long trim
/// takes to build" is legible in a way an integral gain is not.
/// </remarks>
/// <param name="ProportionalGain">Kc, in duty points per °C of error.</param>
/// <param name="IntegralTimeSeconds">τᵢ, in seconds.</param>
/// <param name="IntegralGain">Kᵢ = Kc/τᵢ, in duty points per °C-second.</param>
public readonly record struct AdaptivePidGains(double ProportionalGain, double IntegralTimeSeconds, double IntegralGain)
{
    /// <summary>No usable gains — the feedback path is disabled.</summary>
    public static AdaptivePidGains None { get; } = new(0d, 0d, 0d);

    /// <summary>True when these gains can actually close a loop.</summary>
    public bool IsUsable
        => ProportionalGain > 0d
            && double.IsFinite(ProportionalGain)
            && IntegralTimeSeconds > 0d
            && double.IsFinite(IntegralTimeSeconds)
            && double.IsFinite(IntegralGain);
}
