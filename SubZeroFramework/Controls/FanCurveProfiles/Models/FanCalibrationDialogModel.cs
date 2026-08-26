using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
using SubZeroFramework.Services.Control;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// One sensor the run can be measured against, as offered in the consent step.
/// </summary>
/// <remarks>
/// Its own object rather than the page's chip. The page's chips carry the selection used for CURVES, and
/// binding the wizard straight to them would make ticking a box here silently rewrite the fan's curve
/// sensors. Name and temperature are mirrored from the page's chip so the reading stays live while the
/// dialog is open; only the tick is this one's own.
/// </remarks>
public sealed partial class FanCalibrationSensorChoice : ObservableObject, IDisposable
{
    private readonly SensorChipModel _source;

    public FanCalibrationSensorChoice(FanCalibrationDialogModel owner, SensorChipModel source, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);

        Owner = owner;
        _source = source;
        IsSelected = isSelected;

        _source.PropertyChanged += OnSourceChanged;
    }

    /// <summary>
    /// The list this chip belongs to, so the chip's own button can bind to a command.
    /// </summary>
    /// <remarks>
    /// An ItemsRepeater template has no DataContext, so a command on the dialog's model is not otherwise
    /// reachable from inside the row — and routing the click through code-behind to get at it is more
    /// plumbing than one back-reference.
    /// </remarks>
    public FanCalibrationDialogModel Owner { get; }

    public int SensorIndex => _source.SensorIndex;

    public string DisplayName => _source.DisplayName;

    /// <summary>
    /// The sensor's index label — "Temp 3" — which the chip shows quietly.
    /// </summary>
    /// <remarks>
    /// The chip name arrives as two lines: a generic index and the place it measures. They carry very
    /// different amounts of information, so they are separated here and weighted accordingly — the index
    /// distinguishes two sensors with the same location and is otherwise noise, while the location is the
    /// only part that says what the sensor actually is.
    /// </remarks>
    public string IndexLabel => SplitName().Index;

    /// <summary>Where the sensor measures — "APU / SoC" — which is the part worth reading.</summary>
    public string LocationLabel => SplitName().Location;

    public bool HasIndexLabel => IndexLabel.Length > 0;

    public string TemperatureDisplay => _source.TemperatureDisplay;

    private (string Index, string Location) SplitName()
    {
        var name = _source.DisplayName ?? string.Empty;
        var breakAt = name.IndexOf('\n');

        // No second line means the name was never split — show it whole rather than inventing an index.
        return breakAt < 0
            ? (string.Empty, name.Trim())
            : (name[..breakAt].Trim(), name[(breakAt + 1)..].Trim());
    }

    public double? CurrentTemperatureCelsius => _source.CurrentTemperatureCelsius;

    /// <summary>False for a sensor that is not reporting, which cannot be measured against.</summary>
    public bool IsUsable => _source.IsUsable;

    public double ChipOpacity => _source.ChipOpacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckIconKind))]
    [NotifyPropertyChangedFor(nameof(ChipBorderBrushKey))]
    [NotifyPropertyChangedFor(nameof(CheckBrushKey))]
    public partial bool IsSelected { get; set; }

    /// <summary>A filled check when chosen, an empty ring when not — the chip is the control.</summary>
    public MaterialIconKind CheckIconKind => IsSelected
        ? MaterialIconKind.CheckCircle
        : MaterialIconKind.CircleOutline;

    public string ChipBorderBrushKey => IsSelected ? "BrandPrimaryBrush" : "SurfaceOutlineBrush";

    public string CheckBrushKey => IsSelected ? "BrandPrimaryBrush" : "TextTertiaryBrush";

    public void Dispose() => _source.PropertyChanged -= OnSourceChanged;

    private void OnSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The temperature is the one thing that moves while the dialog is open, and the readout above the
        // chips is derived from it — so an empty name (the "everything changed" signal) has to pass through.
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(SensorChipModel.CurrentTemperatureCelsius)
                or nameof(SensorChipModel.DisplayName)
                or nameof(SensorChipModel.State))
        {
            OnPropertyChanged(nameof(TemperatureDisplay));
            OnPropertyChanged(nameof(CurrentTemperatureCelsius));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IndexLabel));
            OnPropertyChanged(nameof(LocationLabel));
            OnPropertyChanged(nameof(HasIndexLabel));
            OnPropertyChanged(nameof(IsUsable));
            OnPropertyChanged(nameof(ChipOpacity));
        }
    }
}

/// <summary>One thing the run will do to the machine, as a row in the consent list.</summary>
public sealed record FanCalibrationFact(MaterialIconKind IconKind, string BrushKey, string Title, string Detail);

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

    /// <summary>
    /// How long the run is advertised as taking, in minutes.
    /// </summary>
    /// <remarks>
    /// The typical length of a clean run, quoted only where it is hedged ("about", "usually"). The heading
    /// deliberately says "a few minutes" instead: the run retries itself with cooling breaks when the
    /// machine rides its thermal ceiling, so a number in the TITLE became a promise a hot chassis visibly
    /// broke — a five-minute banner over a fifteen-minute run reads as a hang, not a measurement.
    /// </remarks>
    private const int AdvertisedMinutes = 5;

    public FanCalibrationDialogModel(
        string fanDisplayName,
        FanCoolingRole coolingRole,
        IUnitFormattingService unitFormattingService,
        IReadOnlyList<SensorChipModel>? sensors = null,
        IReadOnlyCollection<int>? selectedSensorIndices = null)
    {
        ArgumentNullException.ThrowIfNull(unitFormattingService);

        _unitFormattingService = unitFormattingService;
        FanDisplayName = fanDisplayName;
        CoolingRole = coolingRole;

        // Plain closures, not rebuilt on a preference change, because the dialog is modal — Settings cannot
        // be reached while it is open, so the unit preference is pinned for the life of the run.
        TemperatureAxisLabeler = unitFormattingService.FormatTemperatureAxisTick;
        FanSpeedAxisLabeler = unitFormattingService.FormatFanSpeedAxisTick;
        ClockAxisLabeler = unitFormattingService.FormatClockFrequencyAxisTick;
        UsageAxisLabeler = unitFormattingService.FormatRatioAxisTick;

        // A GPU fan's run has to heat the GPU, so that is where the toggle starts. It is a starting point and
        // not a lock: the role is inferred, and on a machine nobody has mapped it can simply be wrong.
        IsGpuLoad = coolingRole == FanCoolingRole.Gpu;

        var preselected = selectedSensorIndices is null
            ? []
            : new HashSet<int>(selectedSensorIndices);

        foreach (var sensor in sensors ?? [])
        {
            var choice = new FanCalibrationSensorChoice(this, sensor, preselected.Contains(sensor.SensorIndex));
            choice.PropertyChanged += OnSensorChoiceChanged;
            _sensors.Add(choice);
        }

        Sensors = new ReadOnlyObservableCollection<FanCalibrationSensorChoice>(_sensors);
        RefreshSensorSummary();
    }

    private readonly ObservableCollection<FanCalibrationSensorChoice> _sensors = [];

    public string FanDisplayName { get; }

    public FanCoolingRole CoolingRole { get; }

    /// <summary>The sensors on offer, with the run's own tick state.</summary>
    public ReadOnlyObservableCollection<FanCalibrationSensorChoice> Sensors { get; }

    /// <summary>
    /// Names the fan and sets the expectation in one line.
    /// </summary>
    /// <remarks>
    /// "Calibrate", not "Teach". You teach somebody something — teaching a fan reads as though the fan is
    /// the student, when what is being taught is SubZero. It also matches the button that opens this and the
    /// word the rest of the app already uses for the same operation.
    /// </remarks>
    public string DialogTitle => $"Calibrate {FanDisplayName} in a few minutes";

    /// <summary>What the test buys, and that it is not the end of the learning.</summary>
    public string Introduction =>
        "This test measures how this fan moves heat out of the machine — how much the temperature falls per "
        + $"unit of airflow, and how long that takes. It gives Adaptive its starting model in about {AdvertisedMinutes} "
        + "minutes, and Adaptive keeps refining it from ordinary use from then on.";

    // ----- What it learns from -----

    /// <summary>
    /// Which component the run heats.
    /// </summary>
    /// <remarks>
    /// Asked rather than inferred because the inference can be wrong, and wrong here is expensive: heating
    /// the processor while measuring a fan that cools the graphics module leaves those sensors at idle, and
    /// the run fails several minutes later for a temperature swing that was never going to happen.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCpuLoad))]
    [NotifyPropertyChangedFor(nameof(LoadTarget))]
    [NotifyPropertyChangedFor(nameof(LoadHint))]
    [NotifyPropertyChangedFor(nameof(LoadDescription))]
    [NotifyPropertyChangedFor(nameof(DoNotDisturbText))]
    [NotifyPropertyChangedFor(nameof(ClockPlotLabel))]
    [NotifyPropertyChangedFor(nameof(UsagePlotLabel))]
    public partial bool IsGpuLoad { get; set; }

    public bool IsCpuLoad => !IsGpuLoad;

    /// <summary>Picks the component to heat. A command so the segmented pair needs no code-behind.</summary>
    [RelayCommand]
    private void ChooseLoad(string? target) => IsGpuLoad = string.Equals(target, "Gpu", StringComparison.Ordinal);

    /// <summary>Ticks or un-ticks one sensor, from the chip itself.</summary>
    [RelayCommand]
    private static void ToggleSensor(FanCalibrationSensorChoice? sensor)
    {
        if (sensor is not null)
        {
            sensor.IsSelected = !sensor.IsSelected;
        }
    }

    public ThermalLoadTarget LoadTarget => IsGpuLoad ? ThermalLoadTarget.Gpu : ThermalLoadTarget.Cpu;

    /// <summary>Which fans each choice suits, so the toggle is a decision rather than a guess.</summary>
    public string LoadHint => IsGpuLoad
        ? "Best for the fan over the graphics module"
        : "Best for the CPU and APU fans";

    /// <summary>The hottest of the ticked sensors — the one the run actually follows.</summary>
    [ObservableProperty]
    public partial string HottestSensorText { get; private set; } = "—";

    /// <summary>False while nothing is ticked, which is the one state the run cannot start from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    public partial bool HasSelectedSensor { get; private set; }

    /// <summary>The sensors the run will be measured against.</summary>
    public IReadOnlyList<int> SelectedSensorIndices =>
        [.. _sensors.Where(static sensor => sensor.IsSelected).Select(static sensor => sensor.SensorIndex)];

    public bool CanStart => HasSelectedSensor;

    /// <summary>
    /// What the run does to the machine, as one column of rows.
    /// </summary>
    /// <remarks>
    /// Each row is a title and a consequence, because the title alone does not tell the user why they should
    /// care: "Fans will run to maximum" is a specification, "this is loud" is the thing they will actually
    /// experience and the reason they might choose a different moment.
    /// </remarks>
    public IReadOnlyList<FanCalibrationFact> Facts =>
    [
        new(MaterialIconKind.FanSpeed3, "StatusWarningBrush", "Fans will run to maximum",
            "This is loud. The fans spin up and hold there for part of the run."),
        new(MaterialIconKind.Chip, "StatusWarningBrush", LoadDescription,
            "SubZero generates heat so it has something to measure. The machine may feel sluggish."),
        new(MaterialIconKind.HandBackLeft, "StatusWarningBrush", "Leave the machine alone once it starts",
            "SubZero applies the load itself — anything you run on top adds heat it isn't expecting and skews the result."),
        new(MaterialIconKind.ClockOutline, "BrandSecondaryBrush", $"It usually takes about {AdvertisedMinutes} minutes",
            "Longer if the machine runs hot and needs cooling breaks. You can stop at any point without leaving anything half-applied."),
        new(MaterialIconKind.PowerPlug, "StatusSuccessBrush", "AC power is required",
            "Power limits behave differently on battery, which would skew the measurement."),
    ];

    private void OnSensorChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RefreshSensorSummary();

    /// <summary>Recomputes the readout above the chips from whatever is currently ticked.</summary>
    private void RefreshSensorSummary()
    {
        var selected = _sensors.Where(static sensor => sensor.IsSelected).ToArray();

        HasSelectedSensor = selected.Length > 0;

        var hottest = selected
            .Select(static sensor => sensor.CurrentTemperatureCelsius)
            .Where(static celsius => celsius is not null)
            .Select(static celsius => celsius!.Value)
            .DefaultIfEmpty(double.NaN)
            .Max();

        HottestSensorText = double.IsNaN(hottest)
            ? "—"
            : _unitFormattingService.FormatTemperature(hottest, decimals: 0);
    }

    /// <summary>Why using the machine mid-run ruins the measurement, named to the component being loaded.</summary>
    public string DoNotDisturbText => IsGpuLoad
        ? "SubZero controls the GPU load itself. Anything you start — a game, a render, a video call — adds "
        + "heat it isn't expecting, and the measurement will be wrong. Leave it alone until it finishes."
        : "SubZero controls the CPU load itself. Anything you start — a game, a build, a video call — adds "
        + "heat it isn't expecting, and the measurement will be wrong. Leave it alone until it finishes.";

    /// <summary>Names the component that will be loaded, which follows the toggle rather than the fan's role.</summary>
    public string LoadDescription => IsGpuLoad
        ? "The GPU will be loaded on purpose"
        : "The CPU will be loaded on purpose";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsentVisibility))]
    [NotifyPropertyChangedFor(nameof(RunningVisibility))]
    [NotifyPropertyChangedFor(nameof(OutcomeVisibility))]
    [NotifyPropertyChangedFor(nameof(PrimaryText))]
    [NotifyPropertyChangedFor(nameof(CloseText))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryEnabled))]
    [NotifyPropertyChangedFor(nameof(AcceptText))]
    [NotifyPropertyChangedFor(nameof(AcceptVisibility))]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    [NotifyPropertyChangedFor(nameof(CancelText))]
    [NotifyPropertyChangedFor(nameof(CancelVisibility))]
    [NotifyPropertyChangedFor(nameof(ConsentReadyVisibility))]
    [NotifyPropertyChangedFor(nameof(ConsentBlockedVisibility))]
    [NotifyPropertyChangedFor(nameof(RetryVisibility))]
    [NotifyPropertyChangedFor(nameof(FailureCloseVisibility))]
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
        // No primary at all while blocked on battery — the design is an explanation that unblocks live,
        // and a disabled Start would be the wordless version of it.
        FanCalibrationStage.Consent => IsOnBattery ? string.Empty : "Start the test",

        // Hidden on a failure: that footer is the plain Close plus the accent Try-again pair instead —
        // an accent "Close" beside an accent "Try again" would make the way out look like the action.
        FanCalibrationStage.Outcome => DidSucceed ? "Done" : string.Empty,
        _ => string.Empty,
    };

    /// <summary>The failure footer's plain way out, beside the accent retry.</summary>
    public Visibility FailureCloseVisibility
        => Stage == FanCalibrationStage.Outcome && !DidSucceed ? Visibility.Visible : Visibility.Collapsed;

    // ----- Footer, owned by the dialog rather than by the ContentDialog template -----

    /// <summary>The affirmative button's label, or empty where there is nothing to affirm.</summary>
    public string AcceptText => PrimaryText;

    public Visibility AcceptVisibility => AcceptText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Whether the run can be started at all.
    /// </summary>
    /// <remarks>
    /// Two blockers, both of which the service would otherwise discover minutes in: no sensors to measure
    /// against, and running on battery. Refusing here costs a moment; refusing there costs a five-minute run
    /// that heated the machine for nothing.
    /// </remarks>
    public bool CanAccept => Stage != FanCalibrationStage.Consent || (CanStart && !IsOnBattery);

    // ----- Power, which decides whether the run may start at all -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    [NotifyPropertyChangedFor(nameof(ConsentReadyVisibility))]
    [NotifyPropertyChangedFor(nameof(ConsentBlockedVisibility))]
    [NotifyPropertyChangedFor(nameof(AcceptText))]
    [NotifyPropertyChangedFor(nameof(AcceptVisibility))]
    public partial bool IsOnBattery { get; set; }

    /// <summary>The lowest pack's charge, canonical percent, shown in the blocked state's power tiles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryChargeText))]
    public partial double? BatteryChargePercent { get; set; }

    public string BatteryChargeText => BatteryChargePercent is { } charge
        ? _unitFormattingService.FormatRatio(charge, decimals: 0)
        : "—";

    /// <summary>
    /// The consent body, shown only on AC. On battery the whole consent gives way to the blocked state —
    /// an explicit explanation with the power readout, not a disabled button with no reason.
    /// </summary>
    public Visibility ConsentReadyVisibility
        => Stage == FanCalibrationStage.Consent && !IsOnBattery ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The blocked-on-battery state. Warning-toned, not danger: it blocks THIS shortcut, not the feature —
    /// Adaptive keeps learning from ordinary use either way. Unblocks live when the charger is attached,
    /// because <see cref="IsOnBattery"/> is pushed continuously while the dialog is open.
    /// </summary>
    public Visibility ConsentBlockedVisibility
        => Stage == FanCalibrationStage.Consent && IsOnBattery ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial string PowerReadyText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PowerReadyBrushKey { get; set; } = "StatusSuccessBrush";

    [ObservableProperty]
    public partial MaterialIconKind PowerReadyIconKind { get; set; } = MaterialIconKind.PowerPlug;

    /// <summary>
    /// The way out, which is "Cancel" everywhere except mid-run.
    /// </summary>
    /// <remarks>
    /// "Stop the test" while running, because leaving IS cancelling — the stream is the run's lease.
    /// "Cancel" there would suggest it dismisses the dialog and leaves the run going.
    /// </remarks>
    public string CancelText => Stage switch
    {
        FanCalibrationStage.Consent => "Cancel",
        FanCalibrationStage.Running => "Stop the test",
        _ => string.Empty,
    };

    public Visibility CancelVisibility => CancelText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>What the power figure actually is, which depends on what this platform reports.</summary>
    [ObservableProperty]
    public partial string PowerLabel { get; private set; } = "Package power";

    // ----- Live plot -----
    //
    // The centre of the running screen. Watching the temperature actually bend after the fan steps is what
    // makes a five-minute wait tolerable, and it is the only way to tell a run that is working from one that
    // is about to fail for lack of load — a flat line says that long before the failure screen does.

    /// <summary>
    /// The temperature line, in the user's display units, on its own auto-fitted axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The series live in XAML and only these VALUES arrays are replaced per sample — the recipe every chart
    /// that demonstrably live-updates uses (the fan cards, the adaptive response preview). The previous
    /// version rebuilt whole LineSeries objects here once a second, which no working chart does. Each array
    /// is replaced wholesale so one assignment raises one change rather than a redraw per point.
    /// </para>
    /// <para>
    /// Pre-converted to DISPLAY units so the axis labeler — the non-converting AxisTick family — agrees
    /// with the data on coordinate space. Converting at append time is safe here because the dialog is
    /// modal: the Settings page, and with it the unit preference, is unreachable while a run is showing.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    public partial ObservablePoint[] TemperatureHistory { get; private set; } = [];

    /// <summary>
    /// The MEASURED fan speed, in the user's display units, on the second plot.
    /// </summary>
    /// <remarks>
    /// Measured rather than commanded, and that choice is what makes the line exist from the first sample:
    /// during the settle step the firmware still owns the fan, so a commanded-duty line had nothing to show
    /// for minutes and read as broken. The tachometer always has an answer. Sharing one plot with the
    /// temperature was tried twice and failed twice — a dual-axis ScalesYAt the wrapper silently ignored,
    /// then a unitless normalized axis — which is why each quantity now has a plot of its own.
    /// </remarks>
    [ObservableProperty]
    public partial ObservablePoint[] SpeedHistory { get; private set; } = [];

    /// <summary>
    /// The loaded component's clock in display units — CPU on a CPU-load run, GPU core on a GPU-load one.
    /// </summary>
    /// <remarks>
    /// Clock and busy share together answer the question the temperature cannot: whether the load actually
    /// TOOK. A run heading for "insufficient load" shows a near-idle usage line minutes before it fails.
    /// </remarks>
    [ObservableProperty]
    public partial ObservablePoint[] ClockHistory { get; private set; } = [];

    /// <summary>That same component's busy share, in the display ratio unit.</summary>
    [ObservableProperty]
    public partial ObservablePoint[] UsageHistory { get; private set; } = [];

    /// <summary>Formats an ALREADY-CONVERTED temperature axis value; converting here would double-convert.</summary>
    public Func<double, string> TemperatureAxisLabeler { get; }

    /// <summary>Formats an ALREADY-CONVERTED fan-speed axis value.</summary>
    public Func<double, string> FanSpeedAxisLabeler { get; }

    /// <summary>Formats an ALREADY-CONVERTED clock-frequency axis value.</summary>
    public Func<double, string> ClockAxisLabeler { get; }

    /// <summary>Formats an ALREADY-CONVERTED ratio axis value.</summary>
    public Func<double, string> UsageAxisLabeler { get; }

    /// <summary>Names the clock plot after the component this run heats, which the load toggle decides.</summary>
    public string ClockPlotLabel => IsGpuLoad ? "GPU frequency" : "CPU frequency";

    /// <summary>Names the usage plot the same way.</summary>
    public string UsagePlotLabel => IsGpuLoad ? "GPU usage" : "CPU usage";

    /// <summary>Muted label paint for both Y axes. An SKPaint, not a XAML Brush, so it is thread-agnostic.</summary>
    public SolidColorPaint AxisLabelsPaint { get; } = new(new SKColor(0x8d, 0x8e, 0xa3));

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

    public SolidColorPaint SpeedPaint { get; } = new(new SKColor(0x00, 0x78, 0xD7)) { StrokeThickness = 2f };

    public SolidColorPaint ClockPaint { get; } = new(new SKColor(0xB4, 0x8C, 0xFF)) { StrokeThickness = 2f };

    public SolidColorPaint UsagePaint { get; } = new(new SKColor(0x50, 0xC8, 0x78)) { StrokeThickness = 2f };

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
    [NotifyPropertyChangedFor(nameof(PrimaryText))]
    [NotifyPropertyChangedFor(nameof(AcceptText))]
    [NotifyPropertyChangedFor(nameof(AcceptVisibility))]
    [NotifyPropertyChangedFor(nameof(RetryVisibility))]
    [NotifyPropertyChangedFor(nameof(FailureCloseVisibility))]
    public partial bool DidSucceed { get; private set; }

    /// <summary>The failure screens' second act: a fresh run, one click away.</summary>
    public Visibility RetryVisibility
        => Stage == FanCalibrationStage.Outcome && !DidSucceed && Failure != FanCalibrationFailure.ClientDisconnected
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>"Start again" after a deliberate cancel; "Try again" after anything that went wrong.</summary>
    public string RetryText => Failure == FanCalibrationFailure.Cancelled ? "Start again" : "Try again";

    /// <summary>The cause of the failure being shown, for the presentation switches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetryVisibility))]
    [NotifyPropertyChangedFor(nameof(RetryText))]
    public partial FanCalibrationFailure Failure { get; private set; }

    /// <summary>"{fan} · nothing was saved" on failures; the measured-just-now line on success.</summary>
    [ObservableProperty]
    public partial string OutcomeSubtitle { get; private set; } = string.Empty;

    /// <summary>The per-cause banner inside a failure outcome: icon, title, body on a severity wash.</summary>
    [ObservableProperty]
    public partial MaterialIconKind BannerIconKind { get; private set; } = MaterialIconKind.AlertDecagramOutline;

    [ObservableProperty]
    public partial string BannerTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BannerBody { get; private set; } = string.Empty;

    /// <summary>Foreground for the banner's icon and title — the severity accent.</summary>
    [ObservableProperty]
    public partial string BannerBrushKey { get; private set; } = "StatusWarningBrush";

    /// <summary>
    /// The banner's border, SEPARATE from the title brush.
    /// </summary>
    /// <remarks>
    /// One key drove both once, and the neutral cancelled state paid for it: a title meant to be plain
    /// text-primary became a bright white OUTLINE around the whole banner. Severity banners still match
    /// border to title; the neutral one takes the hairline every card uses.
    /// </remarks>
    [ObservableProperty]
    public partial string BannerBorderKey { get; private set; } = "StatusWarningBrush";

    /// <summary>The banner's translucent background wash, matched to the severity.</summary>
    [ObservableProperty]
    public partial string BannerWashKey { get; private set; } = "StatusWarningWashBrush";

    [ObservableProperty]
    public partial Visibility BannerVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>"WHAT WAS MEASURED" — the failure's numbers as divider-separated rows, headline value first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CalibrationMeasuredRow> MeasuredRows { get; private set; } = [];

    [ObservableProperty]
    public partial Visibility MeasuredRowsVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>The one-line "what to change before spending another five minutes" advice.</summary>
    [ObservableProperty]
    public partial string AdviceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility AdviceVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>"WHAT WAS MEASURED" where the numbers are readings; "WHERE IT GOT TO" where they are a stop.</summary>
    [ObservableProperty]
    public partial string MeasuredEyebrow { get; private set; } = "WHAT WAS MEASURED";

    /// <summary>
    /// Collapses the body paragraph when a failure moves its story into the banner.
    /// </summary>
    /// <remarks>
    /// An empty TextBlock is zero-height but its StackPanel spacing is not, and the failure header grew a
    /// visible dead band exactly there.
    /// </remarks>
    [ObservableProperty]
    public partial Visibility OutcomeBodyVisibility { get; private set; } = Visibility.Visible;

    /// <summary>
    /// Hides the standalone fans-were-restored line when the measured rows already carry it.
    /// </summary>
    /// <remarks>
    /// Cancelled, disconnected and on-battery answer "Fans restored" as a table row; repeating it as a
    /// footnote said the same thing twice on the states where reassurance matters most.
    /// </remarks>
    [ObservableProperty]
    public partial Visibility RestoredLineVisibility { get; private set; } = Visibility.Visible;

    [ObservableProperty]
    public partial bool WasRestored { get; private set; } = true;

    [ObservableProperty]
    public partial IReadOnlyList<AdaptiveKnownFact> OutcomeFacts { get; private set; } = [];

    /// <summary>
    /// True from the moment the run starts, and never false again.
    /// </summary>
    /// <remarks>
    /// Drives <c>x:Load</c> on the chart. The dialog opens on the consent stage with the run section
    /// collapsed, and a chart realized inside a collapsed panel takes a zero-size measure it never recovers
    /// from — so the chart must not EXIST until there is a visible place for it to measure into. Latched
    /// rather than following the stage, because unloading it on the outcome screen would throw away the
    /// element for no gain.
    /// </remarks>
    [ObservableProperty]
    public partial bool HasRunStarted { get; private set; }

    /// <summary>Moves to the live run — from consent, or from a failure screen's retry.</summary>
    public void BeginRun()
    {
        // A retry is a NEW run, and the plots and progress must say so: the previous attempt's traces would
        // otherwise sit under the new ones as a curve nothing is drawing any more.
        _temperaturePoints.Clear();
        _speedPoints.Clear();
        _clockPoints.Clear();
        _usagePoints.Clear();
        _recentPlotCelsius.Clear();
        TemperatureHistory = [];
        SpeedHistory = [];
        ClockHistory = [];
        UsageHistory = [];
        StepMarker = [];
        Progress = 0d;

        Stage = FanCalibrationStage.Running;
        HasRunStarted = true;
        StepTitle = "Starting";
        StepCounter = string.Empty;
        RemainingText = "Estimating how long this will take…";
    }

    /// <summary>Applies one streamed progress update.</summary>
    public void Apply(FanCalibrationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        StepTitle = Describe(progress.Step);

        if (progress.Step == FanCalibrationStep.CoolingDown)
        {
            // A pause between attempts, not a step — numbering it would misuse its out-of-order enum value,
            // and estimating it would put a countdown on a wait whose length is the machine's to decide.
            StepCounter = "Between attempts";
            RemainingText = "Waiting for the machine to cool…";
        }
        else
        {
            // Clamped: the final update carries Completed, which is one past the last countable step, and
            // would otherwise render as "Step 9 of 8".
            StepCounter = $"Step {Math.Min((int)progress.Step, progress.StepCount)} of {progress.StepCount}";

            RemainingText = progress.EstimatedRemaining is TimeSpan remaining && remaining > TimeSpan.Zero
                ? $"About {FormatDuration(remaining)} left"
                : "Finishing up…";
        }

        Progress = progress.OverallProgress;

        TemperatureText = progress.TemperatureCelsius is double celsius
            ? _unitFormattingService.FormatTemperature(celsius, decimals: 0)
            : "—";

        // The commanded duty rides along once SubZero has taken the fan — "4,079 RPM · 22%". Before that
        // (settling, minimum spin not yet started) the fan is the firmware's, there is no commanded duty,
        // and pretending otherwise would plot a number nobody commanded.
        SpeedText = progress.SpeedRpm is double rpm
            ? progress.DutyPercent is double dutyPercent
                ? $"{_unitFormattingService.FormatFanSpeed(rpm, decimals: 0)} · {dutyPercent:0}%"
                : _unitFormattingService.FormatFanSpeed(rpm, decimals: 0)
            : "—";

        PowerText = progress.PackagePowerWatts is double watts
            ? _unitFormattingService.FormatPowerWatts(watts, decimals: 0)
            : "—";

        // Named for what it actually IS. Where the package figure is unavailable the run falls back to the
        // whole-system reading so it can still measure — but calling a 240 W adapter draw "package power"
        // makes the user distrust every other number on the screen once they notice.
        PowerLabel = progress.PowerIsSystemWide ? "System power" : "Package power";

        AppendPlotPoint(progress);
    }

    /// <summary>
    /// Adds this sample to the live plot.
    /// </summary>
    /// <remarks>
    /// Arrays are rebuilt rather than an observable collection mutated, matching how the rest of the app
    /// feeds LiveCharts: the Values array is replaced wholesale so one assignment raises one change, instead
    /// of a redraw per point at a sample a second. The series themselves stay put in the XAML.
    /// </remarks>
    private void AppendPlotPoint(FanCalibrationProgress progress)
    {
        if (progress.TemperatureCelsius is double celsius)
        {
            // The PLOT gets a short trailing mean; the readout below it stays the raw current value. The
            // driving reading is a maximum over several whole-degree-quantised sensors, so raw it flickers
            // ±2 °C and the curve reads as noise instead of as a response. Display only — the fit smooths
            // its own copy, zero-phase, on the service's raw samples.
            _recentPlotCelsius.Enqueue(celsius);
            while (_recentPlotCelsius.Count > 5)
            {
                _recentPlotCelsius.Dequeue();
            }

            _temperaturePoints.Add(new ObservablePoint(
                progress.ElapsedSeconds,
                _unitFormattingService.ConvertTemperature(_recentPlotCelsius.Average())));

            TemperatureHistory = [.. _temperaturePoints];
        }

        if (progress.SpeedRpm is double rpm)
        {
            _speedPoints.Add(new ObservablePoint(
                progress.ElapsedSeconds,
                _unitFormattingService.ConvertFanSpeed(rpm)));

            SpeedHistory = [.. _speedPoints];
        }

        if (progress.ClockMegahertz is double clockMegahertz)
        {
            _clockPoints.Add(new ObservablePoint(
                progress.ElapsedSeconds,
                _unitFormattingService.ConvertClockFrequencyMegahertz(clockMegahertz)));

            ClockHistory = [.. _clockPoints];
        }

        if (progress.UtilizationPercent is double utilizationPercent)
        {
            _usagePoints.Add(new ObservablePoint(
                progress.ElapsedSeconds,
                _unitFormattingService.ConvertRatio(utilizationPercent)));

            UsageHistory = [.. _usagePoints];
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
    private readonly List<ObservablePoint> _speedPoints = [];
    private readonly List<ObservablePoint> _clockPoints = [];
    private readonly List<ObservablePoint> _usagePoints = [];
    private readonly Queue<double> _recentPlotCelsius = new();

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

        Failure = result.Failure;

        if (result.Succeeded && result.Calibration is { } calibration)
        {
            OutcomeIconKind = MaterialIconKind.CheckDecagram;
            OutcomeBrushKey = "StatusSuccessBrush";
            OutcomeHeadline = $"{FanDisplayName} is calibrated";
            OutcomeSubtitle = $"{FanDisplayName} · measured just now";
            BannerVisibility = Visibility.Collapsed;
            MeasuredRowsVisibility = Visibility.Collapsed;
            AdviceVisibility = Visibility.Collapsed;
            OutcomeBodyVisibility = Visibility.Visible;
            RestoredLineVisibility = Visibility.Visible;
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
                // Short enough for its tile — the longer phrasing clipped mid-word at this column width.
                calibration.TrackingMode == FanSpeedTrackingMode.Cascade ? "Holds a set RPM" : "Duty only",
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
                        : "No extra speed",
                    MaterialIconKind.Speedometer));
            }
        }
        else
        {
            // The design's failure anatomy: a static header saying only that it did not finish, the CAUSE
            // in a severity-washed banner, the numbers as labelled rows, one line of advice, and a retry.
            // Never a generic error, and never a wall of tiles the user has to interpret.
            (BannerIconKind, BannerTitle, BannerBody) = DescribeFailure(result.Failure);
            OutcomeIconKind = BannerIconKind;
            OutcomeHeadline = "Calibration did not finish";
            OutcomeBody = string.Empty;
            OutcomeBodyVisibility = Visibility.Collapsed;

            // A cancel is not a malfunction, and its screen says so end to end: nothing lost, a stop rather
            // than a measurement, and no repeated reassurance — the rows already answer "fans restored".
            var wasStopped = result.Failure == FanCalibrationFailure.Cancelled;
            OutcomeSubtitle = wasStopped
                ? $"{FanDisplayName} · nothing saved, nothing lost"
                : $"{FanDisplayName} · nothing was saved";
            MeasuredEyebrow = wasStopped ? "WHERE IT GOT TO" : "WHAT WAS MEASURED";
            RestoredLineVisibility = result.Failure
                is FanCalibrationFailure.Cancelled
                or FanCalibrationFailure.ClientDisconnected
                or FanCalibrationFailure.OnBattery
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Severity ranks the failure honestly: danger where the machine or the service gave out,
            // neutral where the user simply stopped it, warning for a machine that could not be measured.
            // The neutral banner is an ELEVATED card — plain title, hairline border — not a toned one.
            (OutcomeBrushKey, BannerBrushKey, BannerBorderKey, BannerWashKey) = result.Failure switch
            {
                FanCalibrationFailure.TemperatureCeiling or FanCalibrationFailure.ClientDisconnected
                    => ("StatusErrorTextBrush", "StatusErrorTextBrush", "StatusErrorTextBrush", "StatusDangerWashBrush"),
                FanCalibrationFailure.Cancelled
                    => ("TextSecondaryBrush", "TextPrimaryBrush", "SurfaceOutlineBrush", "CardBackgroundBrush"),
                _ => ("StatusWarningBrush", "StatusWarningBrush", "StatusWarningBrush", "StatusWarningWashBrush"),
            };

            BannerVisibility = Visibility.Visible;
            MeasuredRows = BuildMeasuredRows(result);
            MeasuredRowsVisibility = MeasuredRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AdviceText = DescribeAdvice(result.Failure);
            AdviceVisibility = AdviceText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        OutcomeFacts = facts;

        RestoredIconKind = result.FansRestored ? MaterialIconKind.CheckCircle : MaterialIconKind.AlertDecagramOutline;
        RestoredBrushKey = result.FansRestored ? "StatusSuccessBrush" : "StatusErrorTextBrush";
        RestoredText = result.FansRestored
            ? "This fan has been returned to the control it had before the test."
            : "This fan may still be under the test's control — switching its mode will take it back.";
    }

    /// <summary>
    /// The failure's numbers, headline value first with the severity colour, thresholds beside them.
    /// </summary>
    /// <remarks>
    /// The requirements come from the same constants the service enforces — <see cref="FanCalibrationLimits"/>
    /// and <see cref="FopdtIdentification"/> — rather than being retyped, so they cannot drift the moment a
    /// threshold moves. These rows are the user's only guide to what to change before spending another run.
    /// </remarks>
    private IReadOnlyList<CalibrationMeasuredRow> BuildMeasuredRows(FanCalibrationRunResult result)
    {
        List<CalibrationMeasuredRow> rows = [];

        switch (result.Failure)
        {
            case FanCalibrationFailure.InsufficientLoad:
                if (result.AveragePackagePowerWatts is double averageWatts)
                {
                    rows.Add(new(MaterialIconKind.SpeedometerSlow, "Average package power",
                        _unitFormattingService.FormatPowerWatts(averageWatts, decimals: 1), "StatusWarningBrush"));
                }

                rows.Add(new(MaterialIconKind.Target, "Needed at least",
                    _unitFormattingService.FormatPowerWatts(FanCalibrationLimits.MinimumAveragePowerWatts, decimals: 0), "TextPrimaryBrush"));
                rows.Add(new(MaterialIconKind.ClockOutline, "Ran for", FormatDuration(result.Duration), "TextPrimaryBrush"));
                break;

            case FanCalibrationFailure.InsufficientTemperatureSwing:
                if (result.TemperatureSwingCelsius is double swing)
                {
                    rows.Add(new(MaterialIconKind.ThermometerMinus, "Temperature swing",
                        FormatTemperatureDelta(swing, decimals: 1), "StatusWarningBrush"));
                }

                // The floor, not the whole gate: the fit also scales its requirement with the measured
                // noise, which the label's "at least" carries.
                rows.Add(new(MaterialIconKind.Target, "Needed at least",
                    FormatTemperatureDelta(FopdtIdentification.MinimumUsableSwingCelsius, decimals: 0), "TextPrimaryBrush"));

                if (result.PeakTemperatureCelsius is double swingPeak)
                {
                    rows.Add(new(MaterialIconKind.ThermometerAlert, "Hottest reading",
                        _unitFormattingService.FormatTemperature(swingPeak, decimals: 0), "TextPrimaryBrush"));
                }

                break;

            case FanCalibrationFailure.TemperatureCeiling:
                if (result.PeakTemperatureCelsius is double peak)
                {
                    rows.Add(new(MaterialIconKind.ThermometerAlert, "Peak temperature",
                        _unitFormattingService.FormatTemperature(peak, decimals: 0), "StatusErrorTextBrush"));
                }

                rows.Add(new(MaterialIconKind.ShieldHalfFull, "Safety ceiling",
                    _unitFormattingService.FormatTemperature(FanCalibrationLimits.SafetyCeilingCelsius, decimals: 0), "TextPrimaryBrush"));
                rows.Add(new(MaterialIconKind.ClockOutline, "Stopped at step", DescribeStoppedAt(result.StoppedAt), "TextPrimaryBrush"));
                break;

            case FanCalibrationFailure.Cancelled:
                rows.Add(new(MaterialIconKind.ClockOutline, "Stopped at step", DescribeStoppedAt(result.StoppedAt), "TextPrimaryBrush"));
                rows.Add(new(MaterialIconKind.ContentSaveOff, "Saved", "Nothing", "TextPrimaryBrush"));
                rows.Add(new(MaterialIconKind.Fan, "Fans restored", result.FansRestored ? "Yes" : "No", result.FansRestored ? "StatusSuccessBrush" : "StatusErrorTextBrush"));
                break;

            case FanCalibrationFailure.ClientDisconnected:
                rows.Add(new(MaterialIconKind.ClockOutline, "Stopped at step", DescribeStoppedAt(result.StoppedAt), "TextPrimaryBrush"));
                rows.Add(new(MaterialIconKind.Fan, "Fans restored by the service", result.FansRestored ? "Yes" : "Unknown", result.FansRestored ? "StatusSuccessBrush" : "StatusWarningBrush"));
                break;

            case FanCalibrationFailure.OnBattery:
                rows.Add(new(MaterialIconKind.BatteryAlertVariantOutline, "Power source", "Battery", "StatusWarningBrush"));
                rows.Add(new(MaterialIconKind.Fan, "Fans restored", result.FansRestored ? "Yes" : "No", result.FansRestored ? "StatusSuccessBrush" : "StatusErrorTextBrush"));
                break;

            default:
                if (result.Duration > TimeSpan.Zero)
                {
                    rows.Add(new(MaterialIconKind.ClockOutline, "Ran for", FormatDuration(result.Duration), "TextPrimaryBrush"));
                }

                rows.Add(new(MaterialIconKind.ClockOutline, "Stopped at step", DescribeStoppedAt(result.StoppedAt), "TextPrimaryBrush"));
                break;
        }

        return rows;
    }

    /// <summary>The step a run died in, matching the counter the running screen was showing.</summary>
    private static string DescribeStoppedAt(FanCalibrationStep step)
    {
        var count = (int)FanCalibrationStep.Completed - 1;
        return step is FanCalibrationStep.None or FanCalibrationStep.CoolingDown
            ? "before one began"
            : $"{Math.Min((int)step, count)} of {count}";
    }

    /// <summary>One sentence on what to change before spending another run — never generic.</summary>
    private static string DescribeAdvice(FanCalibrationFailure failure) => failure switch
    {
        FanCalibrationFailure.InsufficientLoad =>
            "Set Windows power mode to Balanced or Best performance and close anything that limits the CPU, then try again.",
        FanCalibrationFailure.InsufficientTemperatureSwing =>
            "A very cool room or a well-ventilated dock can cause this. Try again with the laptop on a flat surface at normal room temperature.",
        FanCalibrationFailure.TemperatureCeiling =>
            "The run already raises the other fans and cools down between attempts, so stopping here usually means blocked vents or dust — check the airflow before trying again.",
        FanCalibrationFailure.Cancelled =>
            "No harm done. Adaptive is still learning this fan in the background — start the test again whenever the machine is free.",
        FanCalibrationFailure.ClientDisconnected =>
            "Check that the SubZero service is running in Settings, then reopen this dialog.",
        FanCalibrationFailure.OnBattery =>
            "Connect the charger and try again — power limits behave differently when unplugged.",
        _ => string.Empty,
    };

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
        FanCalibrationStep.CoolingDown => "Cooling down, then trying again with more airflow",
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
            "Calibration cancelled",
            "You stopped the run, so nothing was saved. The fans went back to the mode they were in before "
            + "it started, and the CPU load was released immediately."),

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

/// <summary>
/// One row of a failure screen's "what was measured" table: the number, named, with its severity colour.
/// </summary>
/// <param name="IconKind">The leading glyph.</param>
/// <param name="Label">What the number is.</param>
/// <param name="Value">The formatted reading, display units already applied.</param>
/// <param name="ValueBrushKey">Resource key for the value's colour — the headline row wears the severity.</param>
public sealed record CalibrationMeasuredRow(MaterialIconKind IconKind, string Label, string Value, string ValueBrushKey);
