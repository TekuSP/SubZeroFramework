namespace SubZeroFramework.Models;

/// <summary>
/// How hard the processor is being held back, as the embedded controller itself reports it.
/// </summary>
/// <remarks>
/// Distinct from an inferred performance ratio. A ratio falls for power limits, parked cores and a workload
/// that simply asked for less, none of which is a thermal emergency; this is the controller stating what it
/// is doing.
/// </remarks>
public enum EcThrottleSeverity
{
    /// <summary>The processor is running unrestricted.</summary>
    None = 0,

    /// <summary>Clocks are being trimmed — a limit being managed, not a protection acting.</summary>
    Soft = 1,

    /// <summary>The controller is protecting the silicon. The most severe state it reports.</summary>
    Hard = 2,
}
