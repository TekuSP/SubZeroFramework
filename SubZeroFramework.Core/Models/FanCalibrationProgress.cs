namespace SubZeroFramework.Models;

/// <summary>
/// One progress update from a running calibration, streamed to whoever started it.
/// </summary>
/// <remarks>
/// Carries the live readings as well as the step, so the wizard can plot the response as it happens. Watching
/// the temperature actually move is what makes a four-minute wait tolerable — and it is also the only way a
/// user can tell a run that is working from one that is about to fail for lack of load.
/// </remarks>
public sealed record FanCalibrationProgress
{
    /// <summary>The fan being calibrated.</summary>
    public required int FanIndex { get; init; }

    /// <summary>What the run is doing now.</summary>
    public required FanCalibrationStep Step { get; init; }

    /// <summary>How many steps there are, so the UI never hard-codes a count that can drift.</summary>
    /// <remarks>
    /// Derived from the last step BEFORE <see cref="FanCalibrationStep.Completed"/>, so adding a stage to the
    /// run updates every "step n of m" readout without anyone remembering to.
    /// </remarks>
    public int StepCount => (int)FanCalibrationStep.Completed - 1;

    /// <summary>Rough time left, for the "N left" readout. Null when it cannot be estimated yet.</summary>
    public TimeSpan? EstimatedRemaining { get; init; }

    /// <summary>
    /// How much of the whole run is done, 0–1 — what a progress bar binds to.
    /// </summary>
    /// <remarks>
    /// Weighted by each step's expected duration rather than by step count. The steps are wildly unequal —
    /// the response measurement alone outlasts several others together — so counting them would produce a bar
    /// that sits still and then leaps.
    /// </remarks>
    public double OverallProgress { get; init; }

    /// <summary>Seconds since the run began — the X axis of the live plot.</summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>The driving temperature right now.</summary>
    public double? TemperatureCelsius { get; init; }

    /// <summary>The duty currently commanded.</summary>
    public double? DutyPercent { get; init; }

    /// <summary>Measured speed, which is what the tracking verdict is decided from.</summary>
    public double? SpeedRpm { get; init; }

    /// <summary>
    /// The clock of the component the run is heating — CPU on a CPU-load run, GPU core on a GPU-load one.
    /// </summary>
    /// <remarks>
    /// Which component needs no field: the client chose the load target when it started the run.
    /// </remarks>
    public double? ClockMegahertz { get; init; }

    /// <summary>Busy share of that same component, in canonical percent.</summary>
    public double? UtilizationPercent { get; init; }

    /// <summary>The load the run is producing, for the "is this machine actually busy?" readout.</summary>
    public double? PackagePowerWatts { get; init; }

    /// <summary>
    /// True when <see cref="PackagePowerWatts"/> is a whole-system reading rather than the package.
    /// </summary>
    /// <remarks>
    /// The package figure is unavailable on some platforms and the run falls back to system power so it can
    /// still measure. Carrying WHICH it got means the UI can name it correctly instead of presenting an
    /// adapter draw as processor power.
    /// </remarks>
    public bool PowerIsSystemWide { get; init; }

    /// <summary>Set on the update where the fan was stepped, so the plot can mark it.</summary>
    public bool IsStepMarker { get; init; }
}
