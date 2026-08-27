namespace SubZeroFramework.Models;

/// <summary>
/// What a calibration run produced — a model, or a reason it could not make one.
/// </summary>
/// <remarks>
/// The measured values are carried on BOTH outcomes. A failure screen that says "the machine never got busy
/// enough" without saying how busy it did get, and how busy it needed to be, gives the user nothing to act
/// on — and this run costs them several minutes of a deliberately loaded machine.
/// </remarks>
public sealed record FanCalibrationRunResult
{
    /// <summary>The fan that was calibrated.</summary>
    public required int FanIndex { get; init; }

    /// <summary>Whether a usable model came out.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>The model, when successful.</summary>
    public FanCalibrationSnapshot? Calibration { get; init; }

    /// <summary>Why it failed, when it did.</summary>
    public FanCalibrationFailure Failure { get; init; } = FanCalibrationFailure.None;

    /// <summary>The step it stopped at, for "stopped at step 4 of 7".</summary>
    public FanCalibrationStep StoppedAt { get; init; }

    /// <summary>Average load the run managed to produce, in watts.</summary>
    public double? AveragePackagePowerWatts { get; init; }

    /// <summary>How far the temperature actually moved, in °C.</summary>
    public double? TemperatureSwingCelsius { get; init; }

    /// <summary>The hottest reading seen, for the ceiling failure.</summary>
    public double? PeakTemperatureCelsius { get; init; }

    /// <summary>How long the run lasted before stopping.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether the fan was put back under the control it had before the run.
    /// </summary>
    /// <remarks>
    /// Reported explicitly, and shown to the user on every failure screen. A calibration deliberately drives
    /// the fan to extremes; if it ends without restoring, the machine is left in a state nothing on screen
    /// describes — so this is the one fact a failure must never leave ambiguous.
    /// </remarks>
    public bool FansRestored { get; init; }
}
