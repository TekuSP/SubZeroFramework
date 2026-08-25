using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;

using Material.Icons;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

using Microsoft.UI.Xaml;

using SkiaSharp;

using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;
using SubZeroFramework.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>Which face of the calibration wizard is showing.</summary>
public enum FanCalibrationStage
{
    /// <summary>Explaining what the run will do, before anything has happened.</summary>
    Consent,

    /// <summary>The run itself, with live readings.</summary>
    Running,

    /// <summary>Finished — successfully or not.</summary>
    Outcome,
}

/// <summary>
/// State for the calibration wizard: consent, the live run, and the outcome.
/// </summary>
/// <remarks>
/// A view model rather than properties on the dialog, because almost everything here CHANGES while the dialog
/// is open — progress, temperature, the step name — and computed properties on a view raise nothing, so the
/// bindings would sit at their first value for the whole five minutes.
/// </remarks>
public sealed partial class FanCalibrationDialogModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FanCalibrationDialogModel(
        string fanDisplayName,
        FanCoolingRole coolingRole,
        IUnitFormattingService unitFormattingService)
    {
        ArgumentNullException.ThrowIfNull(unitFormattingService);

        _unitFormattingService = unitFormattingService;
        FanDisplayName = fanDisplayName;
        CoolingRole = coolingRole;
    }

    public string FanDisplayName { get; }

    public FanCoolingRole CoolingRole { get; }

    /// <summary>Names the fan, so a four-fan machine's dialog says which one it will heat.</summary>
    public string DialogTitle => $"Learn {FanDisplayName}";

    /// <summary>Why this exists, phrased around what the fan will DO rather than around control theory.</summary>
    public string Introduction =>
        $"Adaptive drives {FanDisplayName} from a measured model of how fast it moves heat out of this "
        + "machine — how much the temperature falls per unit of airflow, and how long that takes. SubZero "
        + "learns those numbers once by running a short auto-tune, then holds your target temperature with "
        + "the least noise it can.";

    /// <summary>Why using the machine mid-run ruins the measurement, named to the component being loaded.</summary>
    public string DoNotDisturbText => CoolingRole == FanCoolingRole.Gpu
        ? "SubZero controls the GPU load itself. Anything you start — a game, a render, a video call — adds "
        + "heat it isn't expecting, and the measurement will be wrong. Leave it alone until it finishes."
        : "SubZero controls the CPU load itself. Anything you start — a game, a build, a video call — adds "
        + "heat it isn't expecting, and the measurement will be wrong. Leave it alone until it finishes.";

    /// <summary>Names the component that will be loaded, which differs per fan and is otherwise invisible.</summary>
    public string LoadDescription => CoolingRole == FanCoolingRole.Gpu
        ? "The GPU is loaded on purpose to raise heat"
        : "The CPU is loaded on purpose to raise heat";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsentVisibility))]
    [NotifyPropertyChangedFor(nameof(RunningVisibility))]
    [NotifyPropertyChangedFor(nameof(OutcomeVisibility))]
    [NotifyPropertyChangedFor(nameof(PrimaryText))]
    [NotifyPropertyChangedFor(nameof(CloseText))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryEnabled))]
    public partial FanCalibrationStage Stage { get; private set; } = FanCalibrationStage.Consent;

    public Visibility ConsentVisibility => Stage == FanCalibrationStage.Consent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RunningVisibility => Stage == FanCalibrationStage.Running ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OutcomeVisibility => Stage == FanCalibrationStage.Outcome ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The primary button's label per stage.
    /// </summary>
    /// <remarks>
    /// Empty while running: there is nothing to confirm mid-test, and a live primary button invites a second
    /// click on an operation already in progress.
    /// </remarks>
    public string PrimaryText => Stage switch
    {
        FanCalibrationStage.Consent => "Start the test",
        FanCalibrationStage.Outcome => "Done",
        _ => string.Empty,
    };

    /// <summary>
    /// The close button's label per stage.
    /// </summary>
    /// <remarks>
    /// "Stop the test" while running, because closing the dialog IS cancelling — the stream is the run's
    /// lease. Labelling it "Cancel" would suggest it cancels the dialog and leaves the run going.
    /// </remarks>
    public string CloseText => Stage switch
    {
        FanCalibrationStage.Consent => "Not now",
        FanCalibrationStage.Running => "Stop the test",
        _ => string.Empty,
    };

    public bool IsPrimaryEnabled => Stage != FanCalibrationStage.Running;

    // ----- Live run -----

    [ObservableProperty]
    public partial string StepTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StepCounter { get; private set; } = string.Empty;

    /// <summary>Overall completion, 0–1, weighted by expected step duration rather than by step count.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string RemainingText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TemperatureText { get; private set; } = "—";

    [ObservableProperty]
    public partial string SpeedText { get; private set; } = "—";

    [ObservableProperty]
    public partial string PowerText { get; private set; } = "—";

    // ----- Live plot -----
    //
    // The centre of the running screen. Watching the temperature actually bend after the fan steps is what
    // makes a five-minute wait tolerable, and it is the only way to tell a run that is working from one that
    // is about to fail for lack of load — a flat line says that long before the failure screen does.

    /// <summary>Driving temperature against seconds since the run began.</summary>
    [ObservableProperty]
    public partial ObservablePoint[] TemperatureSeries { get; private set; } = [];

    /// <summary>Commanded duty against the same clock, so the step and its effect line up.</summary>
    [ObservableProperty]
    public partial ObservablePoint[] DutySeries { get; private set; } = [];

    /// <summary>
    /// Marks the moment the fan was stepped.
    /// </summary>
    /// <remarks>
    /// Without it the temperature curve is just a shape. The whole claim of the plot is "this bend is a
    /// response to that step", which needs both drawn against one clock and the step called out.
    /// </remarks>
    [ObservableProperty]
    public partial IEnumerable<IChartElement> StepMarker { get; private set; } = [];

    public SolidColorPaint TemperaturePaint { get; } = new(SKColors.Salmon) { StrokeThickness = 2f };

    public SolidColorPaint DutyPaint { get; } = new(new SKColor(0x00, 0x78, 0xD7)) { StrokeThickness = 2f };

    // ----- Outcome -----

    [ObservableProperty]
    public partial string OutcomeHeadline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OutcomeBody { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial MaterialIconKind OutcomeIconKind { get; private set; } = MaterialIconKind.CheckCircle;

    /// <summary>
    /// Resource key for the outcome icon's colour, resolved by the theme-brush converter.
    /// </summary>
    /// <remarks>
    /// A key rather than a Brush: brushes created off the UI thread bind to nothing at all, silently, and a
    /// view model has no business owning one. See the theme-brush converter.
    /// </remarks>
    [ObservableProperty]
    public partial string OutcomeBrushKey { get; private set; } = "StatusSuccessBrush";

    [ObservableProperty]
    public partial string RestoredBrushKey { get; private set; } = "StatusSuccessBrush";

    [ObservableProperty]
    public partial string RestoredText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial MaterialIconKind RestoredIconKind { get; private set; } = MaterialIconKind.CheckCircle;

    [ObservableProperty]
    public partial bool DidSucceed { get; private set; }

    [ObservableProperty]
    public partial bool WasRestored { get; private set; } = true;

    [ObservableProperty]
    public partial IReadOnlyList<AdaptiveKnownFact> OutcomeFacts { get; private set; } = [];

    /// <summary>Moves to the live run. Called once, when consent is given.</summary>
    public void BeginRun()
    {
        Stage = FanCalibrationStage.Running;
        StepTitle = "Starting";
        StepCounter = string.Empty;
        RemainingText = "Estimating how long this will take…";
    }

    /// <summary>Applies one streamed progress update.</summary>
    public void Apply(FanCalibrationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        StepTitle = Describe(progress.Step);
        // Clamped: the final update carries Completed, which is one past the last countable step, and would
        // otherwise render as "Step 9 of 8".
        StepCounter = $"Step {Math.Min((int)progress.Step, progress.StepCount)} of {progress.StepCount}";
        Progress = progress.OverallProgress;

        RemainingText = progress.EstimatedRemaining is TimeSpan remaining && remaining > TimeSpan.Zero
            ? $"About {FormatDuration(remaining)} left"
            : "Finishing up…";

        TemperatureText = progress.TemperatureCelsius is double celsius
            ? _unitFormattingService.FormatTemperature(celsius, decimals: 0)
            : "—";

        SpeedText = progress.SpeedRpm is double rpm
            ? _unitFormattingService.FormatFanSpeed(rpm, decimals: 0)
            : "—";

        PowerText = progress.PackagePowerWatts is double watts
            ? _unitFormattingService.FormatPowerWatts(watts, decimals: 0)
            : "—";

        AppendPlotPoint(progress);
    }

    /// <summary>
    /// Adds this sample to the live plot.
    /// </summary>
    /// <remarks>
    /// Arrays are rebuilt rather than an observable collection mutated, matching how the rest of the app
    /// feeds LiveCharts: the series is replaced wholesale so one assignment raises one change, instead of a
    /// redraw per point at a sample a second.
    /// </remarks>
    private void AppendPlotPoint(FanCalibrationProgress progress)
    {
        if (progress.TemperatureCelsius is double celsius)
        {
            _temperaturePoints.Add(new ObservablePoint(
                progress.ElapsedSeconds,
                _unitFormattingService.ConvertTemperature(celsius)));

            TemperatureSeries = [.. _temperaturePoints];
        }

        if (progress.DutyPercent is double duty)
        {
            _dutyPoints.Add(new ObservablePoint(progress.ElapsedSeconds, duty));
            DutySeries = [.. _dutyPoints];
        }

        if (progress.IsStepMarker)
        {
            StepMarker =
            [
                new RectangularSection
                {
                    Xi = progress.ElapsedSeconds,
                    Xj = progress.ElapsedSeconds,
                    Stroke = new SolidColorPaint(new SKColor(0xC5, 0x99, 0x4E))
                    {
                        StrokeThickness = 1.5f,
                        PathEffect = new DashEffect([5f, 4f]),
                    },
                },
            ];
        }
    }

    private readonly List<ObservablePoint> _temperaturePoints = [];
    private readonly List<ObservablePoint> _dutyPoints = [];

    /// <summary>
    /// Shows the outcome — success or any of the ways it can fail.
    /// </summary>
    /// <remarks>
    /// Every failure carries the measured values, not just a reason. "The machine never got busy enough"
    /// without saying how busy it did get, against how busy it needed to be, leaves the user with nothing to
    /// change before spending another five minutes on it.
    /// </remarks>
    public void Complete(FanCalibrationRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Stage = FanCalibrationStage.Outcome;
        DidSucceed = result.Succeeded;
        WasRestored = result.FansRestored;
        Progress = 1d;

        List<AdaptiveKnownFact> facts = [];

        if (result.Succeeded && result.Calibration is { } calibration)
        {
            OutcomeIconKind = MaterialIconKind.CheckDecagram;
            OutcomeBrushKey = "StatusSuccessBrush";
            OutcomeHeadline = $"{FanDisplayName} is calibrated";
            OutcomeBody =
                "Adaptive now drives this fan from a model of this machine rather than from safe defaults, "
                + "and keeps refining it as you use the computer.";

            facts.Add(new AdaptiveKnownFact(
                "Cooling per 1% fan",
                FormatTemperatureDelta(calibration.ProcessGainCelsiusPerPercent, decimals: 2, suffix: "/%"),
                MaterialIconKind.SnowflakeThermometer));

            facts.Add(new AdaptiveKnownFact(
                "Responds in",
                $"{calibration.TimeConstantSeconds.ToString("0", CultureInfo.CurrentCulture)} s",
                MaterialIconKind.ChartTimelineVariant));

            if (calibration.MinimumSpinRpm > 0d)
            {
                facts.Add(new AdaptiveKnownFact(
                    "Stalls below",
                    _unitFormattingService.FormatFanSpeed(calibration.MinimumSpinRpm, decimals: 0),
                    MaterialIconKind.FanMinus));
            }

            facts.Add(new AdaptiveKnownFact(
                "Speed control",
                calibration.TrackingMode == FanSpeedTrackingMode.Cascade ? "Holds a commanded RPM" : "Duty only",
                calibration.TrackingMode == FanSpeedTrackingMode.Cascade
                    ? MaterialIconKind.CheckNetworkOutline
                    : MaterialIconKind.AlertDecagramOutline));

            // The unflattering answer is the valuable one, and nowhere else in the app can give it.
            if (calibration.PerformanceResponse.SustainedSpeedGainFraction is double gained)
            {
                facts.Add(new AdaptiveKnownFact(
                    "Full fan buys",
                    gained >= FanPerformanceResponse.MeaningfulSpeedGainFraction
                        ? $"+{gained:P0} sustained speed"
                        : "no extra speed",
                    MaterialIconKind.Speedometer));
            }
        }
        else
        {
            (OutcomeIconKind, OutcomeHeadline, OutcomeBody) = DescribeFailure(result.Failure);

            // A stopped test and a test that hit the safety ceiling are not errors in the same sense as a
            // machine that could not be measured, but none of them produced a model — warning throughout,
            // rather than red for some and amber for others, which would invite ranking them.
            OutcomeBrushKey = "StatusWarningBrush";

            if (result.AveragePackagePowerWatts is double averageWatts)
            {
                // The requirement comes from the runner rather than being retyped here — a copy would drift
                // the moment the threshold moved, and this text is the user's only guide to what to change.
                facts.Add(new AdaptiveKnownFact(
                    "Load reached",
                    $"{_unitFormattingService.FormatPowerWatts(averageWatts, decimals: 0)} of "
                    + $"{_unitFormattingService.FormatPowerWatts(FanCalibrationLimits.MinimumAveragePowerWatts, decimals: 0)} needed",
                    MaterialIconKind.SpeedometerSlow));
            }

            if (result.TemperatureSwingCelsius is double swing)
            {
                facts.Add(new AdaptiveKnownFact(
                    "Temperature moved",
                    FormatTemperatureDelta(swing, decimals: 1),
                    MaterialIconKind.ThermometerMinus));
            }

            if (result.PeakTemperatureCelsius is double peak)
            {
                facts.Add(new AdaptiveKnownFact(
                    "Hottest reading",
                    _unitFormattingService.FormatTemperature(peak, decimals: 0),
                    MaterialIconKind.ThermometerAlert));
            }
        }

        OutcomeFacts = facts;

        RestoredIconKind = result.FansRestored ? MaterialIconKind.CheckCircle : MaterialIconKind.AlertDecagramOutline;
        RestoredBrushKey = result.FansRestored ? "StatusSuccessBrush" : "StatusErrorTextBrush";
        RestoredText = result.FansRestored
            ? "This fan has been returned to the control it had before the test."
            : "This fan may still be under the test's control — switching its mode will take it back.";
    }

    /// <summary>
    /// Formats a temperature DIFFERENCE, which is not a temperature.
    /// </summary>
    /// <remarks>
    /// A span of degrees carries no scale offset: 12 °C of cooling is 21.6 °F of cooling, not 53.6 °F. Passing
    /// one through the absolute-scale formatter adds the freezing point and produces a number that looks
    /// plausible and is nonsense — a process gain of 0.35 °C per duty point renders as "32.63 °F".
    /// </remarks>
    private string FormatTemperatureDelta(double celsiusDelta, int decimals, string suffix = "")
    {
        var converted = _unitFormattingService.ConvertTemperatureDelta(celsiusDelta);
        var format = "0." + new string('#', Math.Max(0, decimals));

        return $"{converted.ToString(format, CultureInfo.CurrentCulture)} {_unitFormattingService.TemperatureUnitSuffix}{suffix}";
    }

    private static string FormatDuration(TimeSpan value) => value.TotalMinutes >= 1d
        ? $"{Math.Ceiling(value.TotalMinutes).ToString("0", CultureInfo.CurrentCulture)} min"
        : $"{Math.Ceiling(value.TotalSeconds).ToString("0", CultureInfo.CurrentCulture)} s";

    /// <summary>What each step is doing, in the user's terms rather than the fit's.</summary>
    private static string Describe(FanCalibrationStep step) => step switch
    {
        FanCalibrationStep.SettlingAtIdle => "Letting the machine settle",
        FanCalibrationStep.FindingMinimumSpin => "Finding the slowest this fan will turn",
        FanCalibrationStep.LoadingAndSettling => "Warming the machine up",
        FanCalibrationStep.SteppingFan => "Turning the fan up",
        FanCalibrationStep.MeasuringResponse => "Measuring how fast it cools",
        FanCalibrationStep.FittingModel => "Working out the model",
        FanCalibrationStep.VerifyingSpeedTracking => "Checking it holds a commanded speed",
        FanCalibrationStep.MeasuringGainCurve => "Measuring cooling across the speed range",
        FanCalibrationStep.Completed => "Finished",
        _ => "Starting",
    };

    /// <summary>
    /// Each failure said in terms of what happened and what to change.
    /// </summary>
    /// <remarks>
    /// Never the enum name, and never "an error occurred". Every one of these is a physical situation the user
    /// can usually do something about, and the whole point of the run reporting them separately is that the
    /// remedies differ.
    /// </remarks>
    private static (MaterialIconKind Icon, string Headline, string Body) DescribeFailure(FanCalibrationFailure failure) => failure switch
    {
        FanCalibrationFailure.InsufficientLoad => (
            MaterialIconKind.SpeedometerSlow,
            "The machine never got busy enough",
            "The test needs sustained load to create heat worth measuring. Something may be limiting the "
            + "processor — a power profile, a thermal limit, or another application holding it back."),

        FanCalibrationFailure.InsufficientTemperatureSwing => (
            MaterialIconKind.ThermometerMinus,
            "The temperature barely moved",
            "Turning the fan up did not cool this sensor enough to measure. If this fan does not cool the "
            + "sensors it is driven by, choose sensors it actually affects and try again."),

        FanCalibrationFailure.TemperatureCeiling => (
            MaterialIconKind.ThermometerAlert,
            "It got too hot, so the test stopped",
            "The machine reached the safety ceiling before the measurement finished. This is the test "
            + "protecting the hardware — nothing is damaged, and the fan has been handed back."),

        FanCalibrationFailure.OnBattery => (
            MaterialIconKind.BatteryAlertVariantOutline,
            "Running on battery",
            "On battery the processor runs to different limits, so the model would describe a machine that "
            + "only exists while unplugged. Plug in and try again."),

        FanCalibrationFailure.GpuLoadUnavailable => (
            MaterialIconKind.AlertDecagramOutline,
            "This machine cannot load its GPU",
            "This fan cools the graphics module, so the test has to heat the GPU — and no usable graphics "
            + "accelerator was found. Loading the processor instead would measure the wrong component."),

        FanCalibrationFailure.Cancelled => (
            MaterialIconKind.CloseCircleOutline,
            "Test stopped",
            "The run was stopped before it finished, so nothing was learned from it."),

        FanCalibrationFailure.ClientDisconnected => (
            MaterialIconKind.LanDisconnect,
            "Lost contact with the service",
            "The connection dropped while the test was running. The service stops the run and hands the fan "
            + "back on its own when that happens."),

        _ => (
            MaterialIconKind.AlertDecagramOutline,
            "The test could not finish",
            "Not enough usable readings were gathered to build a model. This usually means telemetry stalled "
            + "part-way through."),
    };
}
