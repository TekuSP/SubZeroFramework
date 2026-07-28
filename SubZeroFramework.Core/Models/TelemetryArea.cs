namespace SubZeroFramework.Models;

public enum TelemetryArea
{
    Thermal,
    Power,

    /// <summary>Processing devices whose load we report: GPUs and NPUs.</summary>
    Compute,
}
