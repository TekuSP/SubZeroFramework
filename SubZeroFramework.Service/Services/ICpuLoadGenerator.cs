namespace SubZeroFramework.Service.Services;

/// <summary>
/// Generates CPU load on demand, so a calibration run can create a thermal gradient worth measuring.
/// </summary>
/// <remarks>
/// An interface mostly so the guarantee that matters can be tested: a calibration must stop the load on every
/// exit path, including the ones nobody plans for. Verifying that against the real generator would mean
/// heating the test machine to find out.
/// </remarks>
public interface ICpuLoadGenerator
{
    /// <summary>True while load is being generated.</summary>
    bool IsRunning { get; }

    /// <summary>The share of each core's time currently being consumed, 0–1.</summary>
    double CurrentLoadFraction { get; }

    /// <summary>
    /// True once the ramp has reached its target and the load is steady.
    /// </summary>
    /// <remarks>
    /// The fit assumes load holds still while the fan is stepped. A run that began measuring during the ramp
    /// would be watching temperature rise because the load was still growing, and would read that rise as the
    /// machine failing to settle — or worse, mistake a moment of it for a plateau.
    /// </remarks>
    bool IsAtTargetLoad { get; }

    /// <summary>Starts loading every logical processor, ramping up to the target. Idempotent.</summary>
    void Start();

    /// <summary>Stops the load and waits for the workers to finish. Idempotent.</summary>
    void Stop();
}
