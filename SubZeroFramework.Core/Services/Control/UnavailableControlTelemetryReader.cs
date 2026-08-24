using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// The reader used where no control-telemetry source exists — a platform without one, or a machine whose
/// counters cannot be opened.
/// </summary>
/// <remarks>
/// Exists so "we cannot read this" is an ordinary, silent state rather than a special case the polling loop
/// has to test for, exactly as <see cref="Compute.UnavailableComputeUtilizationReader"/> does. It reports
/// nothing, and the adaptive controller degrades to running without feed-forward.
/// </remarks>
public sealed class UnavailableControlTelemetryReader : IControlTelemetryReader
{
    public static readonly UnavailableControlTelemetryReader Instance = new();

    public bool IsAvailable => false;

    public ControlTelemetrySample Sample() => ControlTelemetrySample.Unavailable;

    public void Dispose()
    {
        // Nothing held.
    }
}
