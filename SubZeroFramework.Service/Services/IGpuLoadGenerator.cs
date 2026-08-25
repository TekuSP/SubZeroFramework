namespace SubZeroFramework.Service.Services;

/// <summary>
/// Generates GPU load on demand, so calibrating a GPU-cooling fan has something to measure.
/// </summary>
/// <remarks>
/// Separate from <see cref="ICpuLoadGenerator"/> because they heat different things. On a Framework 16 the
/// left fan cools the CPU and the right fan cools the discrete GPU; loading the CPU while calibrating the
/// right fan would leave the sensors it controls sitting at idle, and the run would fail for lack of a
/// temperature swing that was never going to happen.
/// </remarks>
public interface IGpuLoadGenerator
{
    /// <summary>
    /// Whether this machine can generate GPU load at all.
    /// </summary>
    /// <remarks>
    /// False when no usable accelerator is present — no discrete GPU, no OpenCL runtime installed, or a
    /// driver that will not initialise. Callers must treat this as a refusal to calibrate rather than a
    /// reason to substitute CPU load: heating the wrong component would produce a model of the wrong thing.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>A short description of the accelerator, for logs and the failure message.</summary>
    string? AcceleratorName { get; }

    /// <summary>True while load is being generated.</summary>
    bool IsRunning { get; }

    /// <summary>The share of wall-clock time the ramp currently CALLS for, 0–1.</summary>
    double CurrentLoadFraction { get; }

    /// <summary>
    /// The share of wall-clock time actually spent dispatching, 0–1, measured rather than intended.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="CurrentLoadFraction"/> on purpose. That one is the schedule; this one is what
    /// the machine really did, and the two diverge whenever the sleep the generator asked for is not the
    /// sleep the OS granted. Only the measured figure can tell anyone the load is the size it claims.
    /// </remarks>
    double ObservedLoadFraction { get; }

    /// <summary>True once the ramp has reached its target and the load is steady. See <see cref="ICpuLoadGenerator.IsAtTargetLoad"/>.</summary>
    bool IsAtTargetLoad { get; }

    /// <summary>Starts loading the GPU, ramping up to the target. Idempotent. Returns false if no accelerator could be used.</summary>
    bool Start();

    /// <summary>Stops the load and waits for the work to drain. Idempotent.</summary>
    void Stop();
}
