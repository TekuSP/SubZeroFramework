namespace SubZeroFramework.Models;

/// <summary>
/// Why a calibration run did not produce a model.
/// </summary>
/// <remarks>
/// Each value maps to its own screen with its own measured numbers and its own advice — never a generic
/// error. A run costs the user several minutes of a deliberately loaded machine, so "it didn't work" without
/// saying what to change is the one outcome that wastes that entirely.
/// </remarks>
public enum FanCalibrationFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>
    /// The machine never got busy enough to create a thermal gradient worth measuring.
    /// </summary>
    /// <remarks>
    /// Usually a power plan capping the CPU, or thermal headroom the load generator could not fill. The user
    /// can act on it: switch to Balanced or Best performance and close whatever is limiting the processor.
    /// </remarks>
    InsufficientLoad = 1,

    /// <summary>
    /// The temperature barely moved, so the timing points would be noise.
    /// </summary>
    /// <remarks>
    /// A cool room or a well-ventilated dock does this — the fan simply has little work to do. Retrying at
    /// normal room temperature is the fix.
    /// </remarks>
    InsufficientTemperatureSwing = 2,

    /// <summary>
    /// The run stopped early to protect the machine after passing the safety ceiling.
    /// </summary>
    /// <remarks>
    /// Almost always blocked vents or dust. Reported as danger rather than a warning, because unlike the
    /// others it says something is wrong with the machine, not with the conditions of the test.
    /// </remarks>
    TemperatureCeiling = 3,

    /// <summary>The user cancelled. Nothing was saved and the fans were restored.</summary>
    Cancelled = 4,

    /// <summary>The client went away mid-run, and the service restored the fans on its behalf.</summary>
    ClientDisconnected = 5,

    /// <summary>Too few or too irregular samples to fit — a stalled telemetry poll, typically.</summary>
    InsufficientData = 6,

    /// <summary>The machine is on battery, where the run would neither be safe nor representative.</summary>
    OnBattery = 7,

    /// <summary>
    /// The fan is controlled by GPU sensors, but this machine cannot generate GPU load.
    /// </summary>
    /// <remarks>
    /// No discrete GPU, no OpenCL runtime, or a driver that will not initialise. Reported rather than
    /// substituting CPU load, which would heat something this fan does not cool and produce a confident
    /// model of the wrong component.
    /// </remarks>
    GpuLoadUnavailable = 8,
}
