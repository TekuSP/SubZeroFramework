using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

using Material.Icons;

using Microsoft.UI.Xaml;

using SkiaSharp;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Control;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

/// <summary>
/// Body ViewModel for the Adaptive mode route: the controller readout, what the model knows about this fan,
/// and the three things the user actually sets — target temperature, response, and the safety floor.
/// </summary>
/// <remarks>
/// <para>
/// The controller readout is the reason this screen exists. A fan that changes speed for reasons the user
/// cannot see is indistinguishable from a broken one, and "why did it just speed up?" is the single most
/// common complaint about automatic fan control. The contribution bar answers it literally.
/// </para>
/// <para>
/// Target, response and floor are STAGED — they flow through the page's existing dirty/Preview/Apply model
/// like every other fan setting. Releasing a throttle latch is not: it is an immediate command about a
/// control loop that is running right now, and staging it would be nonsense.
/// </para>
/// </remarks>
public sealed partial class FanAdaptiveModeModel : FanModeModelBase
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FanAdaptiveModeModel(FanCoordinatorAccessor coordinatorAccessor, IUnitFormattingService unitFormattingService)
        : base(coordinatorAccessor)
    {
        ArgumentNullException.ThrowIfNull(unitFormattingService);
        _unitFormattingService = unitFormattingService;

        ReleaseThrottleLatchCommand = new AsyncRelayCommand(ReleaseThrottleLatchAsync);
        ForgetLearningCommand = new AsyncRelayCommand(ForgetLearningAsync);
        RunCalibrationCommand = new RelayCommand(RequestCalibration);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
        ExplainControlCommand = new RelayCommand(RequestExplainer);
    }

    /// <summary>
    /// Raised when the user asks for the calibration wizard, so the view can host the dialog.
    /// </summary>
    /// <remarks>
    /// An event rather than the view model opening a <c>ContentDialog</c> itself: a dialog needs a XamlRoot,
    /// which is a view concern, and a view model that reaches for one cannot be tested without one.
    /// </remarks>
    public event EventHandler? CalibrationRequested;

    /// <summary>Raised when the user asks to see how adaptive control works.</summary>
    public event EventHandler? ExplainerRequested;

    // ----- Controller readout -----

    /// <summary>True once the controller has produced a tick for this fan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControllerVisibility))]
    public partial bool IsControllerRunning { get; private set; }

    public Visibility ControllerVisibility => IsControllerRunning ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"Tracking setpoint", or how far off it is. The at-a-glance health of the loop.</summary>
    [ObservableProperty]
    public partial string TrackingText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTracking { get; private set; } = true;

    /// <summary>Commanded speed, in canonical RPM. Rendered through the unit converter.</summary>
    [ObservableProperty]
    public partial double? SetpointRpm { get; private set; }

    [ObservableProperty]
    public partial double? ActualRpm { get; private set; }

    /// <summary>Canonical Celsius; the view formats both through the unit converter.</summary>
    [ObservableProperty]
    public partial double? DrivingTemperatureCelsius { get; private set; }

    [ObservableProperty]
    public partial double TargetTemperatureCelsius { get; private set; } = AdaptiveFanSettings.DefaultTargetCelsius;

    // ----- Contribution bar -----
    //
    // Widths are shares of the RAW demand, not of the limited output: the terms are what the controller
    // ASKED for, and a bar that renormalised to the clamped result would hide the fact that it asked for more
    // than the fan can deliver — which is exactly when the user most wants to see why.

    [ObservableProperty]
    public partial GridLength FeedForwardShare { get; private set; } = new(0, GridUnitType.Star);

    [ObservableProperty]
    public partial GridLength ProportionalIntegralShare { get; private set; } = new(0, GridUnitType.Star);

    [ObservableProperty]
    public partial GridLength LeadShare { get; private set; } = new(0, GridUnitType.Star);

    [ObservableProperty]
    public partial GridLength ThrottleShare { get; private set; } = new(0, GridUnitType.Star);

    /// <summary>Legend rows, with zero-contribution terms omitted entirely.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AdaptiveTermLegendEntry> LegendEntries { get; private set; } = [];

    /// <summary>One sentence explaining what is currently driving the fan.</summary>
    [ObservableProperty]
    public partial string ExplanationText { get; private set; } = string.Empty;

    /// <summary>Set when the loop is running without a power reading, so it can only react.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedForwardWarningVisibility))]
    public partial bool IsFeedForwardUnavailable { get; private set; }

    public Visibility FeedForwardWarningVisibility => IsFeedForwardUnavailable ? Visibility.Visible : Visibility.Collapsed;

    // ----- Throttle escalation -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThrottleLatchVisibility))]
    public partial bool IsThrottleLatched { get; private set; }

    public Visibility ThrottleLatchVisibility => IsThrottleLatched ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial string ThrottleLatchText { get; private set; } = string.Empty;

    public IAsyncRelayCommand ReleaseThrottleLatchCommand { get; }

    // ----- What SubZero knows about this fan -----
    //
    // Status headline plus concrete facts, which is the treatment the design settled on. Deliberately NOT a
    // progress bar: a fan running on defaults is a WORKING fan, and a half-filled bar says otherwise.

    [ObservableProperty]
    public partial string ConfidenceHeadline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfidenceBody { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfidenceChip { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial MaterialIconKind ConfidenceIconKind { get; private set; } = MaterialIconKind.SchoolOutline;

    [ObservableProperty]
    public partial IReadOnlyList<AdaptiveKnownFact> KnownFacts { get; private set; } = [];

    // ----- Identified-gain drift -----
    //
    // The one thing continuous operation produces that no single number can express: whether this chassis is
    // getting BETTER or WORSE at moving heat, and when it turned. Plotted against sample index rather than
    // time — the points are spaced by when the model moved, not by the clock, so a time axis would compress
    // months of stability into a sliver and stretch one busy afternoon across the width.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainHistoryVisibility))]
    public partial ObservablePoint[] GainHistory { get; private set; } = [];

    /// <summary>Hidden until there are two points, because one point is not a trend.</summary>
    public Visibility GainHistoryVisibility => GainHistory.Length >= 2 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What the drift line says, in words — the chart is the evidence, this is the reading.</summary>
    [ObservableProperty]
    public partial string GainHistoryCaption { get; private set; } = string.Empty;

    private void RefreshGainHistory(AdaptiveLearningState learning)
    {
        var history = learning.GainHistory;
        if (history.IsDefaultOrEmpty || history.Length < 2)
        {
            GainHistory = [];
            GainHistoryCaption = string.Empty;
            return;
        }

        GainHistory = [.. history.Select((sample, index) => new ObservablePoint(index, sample.ProcessGainCelsiusPerPercent))];

        var first = history[0].ProcessGainCelsiusPerPercent;
        var latest = history[^1].ProcessGainCelsiusPerPercent;
        var changeFraction = first > 0d ? (latest - first) / first : 0d;
        var span = history[^1].At - history[0].At;
        var over = DescribeSpan(span);

        // Losing cooling is the finding worth surfacing; gaining it is reassurance. The threshold matches the
        // learner's own idea of a material move, so the words cannot disagree with the line.
        GainHistoryCaption = Math.Abs(changeFraction) < AdaptiveLearningState.MaterialChangeFraction
            ? $"Steady over {over}."
            : changeFraction < 0d
                ? $"Cooling {Math.Abs(changeFraction) * 100d:0} % less effective over {over} — dust or ageing paste look like this."
                : $"Cooling {changeFraction * 100d:0} % more effective over {over}.";
    }

    private static string DescribeSpan(TimeSpan span) => span.TotalDays >= 2d
        ? $"{span.TotalDays:0} days"
        : span.TotalHours >= 2d
            ? $"{span.TotalHours:0} hours"
            : "this session";

    /// <summary>Enabled only once there is something learned to discard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForgetLearningVisibility))]
    public partial bool HasLearnedAnything { get; private set; }

    public Visibility ForgetLearningVisibility => HasLearnedAnything ? Visibility.Visible : Visibility.Collapsed;

    public IAsyncRelayCommand ForgetLearningCommand { get; }

    /// <summary>
    /// True when nothing at all is known about this fan, so Adaptive cannot run yet.
    /// </summary>
    /// <remarks>
    /// The first-run state, and the state a fan returns to if its learning is discarded without a measurement
    /// behind it. Everything else in this editor is hidden while it holds: the controls describe a loop that
    /// is not running, and showing them would invite the user to tune something inert.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockoutVisibility))]
    [NotifyPropertyChangedFor(nameof(EditorVisibility))]
    public partial bool IsAwaitingFirstLearning { get; private set; }

    public Visibility LockoutVisibility => IsAwaitingFirstLearning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EditorVisibility => IsAwaitingFirstLearning ? Visibility.Collapsed : Visibility.Visible;


    /// <summary>Stroke for the live response preview.</summary>
    public SolidColorPaint ResponsePreviewPaint { get; } = new(new SKColor(0x00, 0x78, 0xD7)) { StrokeThickness = 2f };

    /// <summary>Stroke for the identified-gain drift line.</summary>
    public SolidColorPaint GainHistoryPaint { get; } = new(new SKColor(0x6C, 0xCB, 0x5F)) { StrokeThickness = 2f };

    /// <summary>Dashed stroke for the default-setting ghost it is compared against.</summary>
    public SolidColorPaint ResponsePreviewDefaultPaint { get; } = new(new SKColor(0x6E, 0x75, 0x7C))
    {
        StrokeThickness = 1.2f,
        PathEffect = new DashEffect([4f, 4f]),
    };

    /// <summary>Formatting for the wizard's live readings, so it obeys the user's chosen units too.</summary>
    public IUnitFormattingService UnitFormattingService => _unitFormattingService;

    /// <summary>Runs the calibration through the page, which owns the service client.</summary>
    public Task<FanCalibrationRunResult> StartCalibrationAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        IProgress<FanCalibrationProgress> progress,
        CancellationToken cancellationToken,
        ThermalLoadTarget loadTarget = ThermalLoadTarget.None)
        => Page.RunCalibrationAsync(fanIndex, drivingSensorIndices, progress, cancellationToken, loadTarget);

    /// <summary>Every sensor the machine reports, for the wizard's picker.</summary>
    public ReadOnlyObservableCollection<SensorChipModel> AvailableSensors => Page.AvailableSensors;

    // Mirrored from the page as STORED properties, assigned in RefreshPowerReadiness. As pass-through
    // getters they never raised PropertyChanged on this object, so the wizard's power state was pushed once
    // and then frozen — the blocked-on-battery screen never cleared when the charger went in.

    /// <summary>Whether a test could run right now, and what to say about it. Relayed from the page.</summary>
    [ObservableProperty]
    public partial bool IsOnBattery { get; private set; }

    /// <summary>The lowest pack's charge, relayed for the wizard's blocked-on-battery readout.</summary>
    [ObservableProperty]
    public partial double? BatteryChargePercent { get; private set; }

    /// <summary>Records the sensors a successful calibration measured, so arming Adaptive can reuse them.</summary>
    public void RememberCalibratedSensors(int fanIndex, IReadOnlyCollection<int> sensorIndices)
        => Page.RememberCalibratedSensors(fanIndex, sensorIndices);

    [ObservableProperty]
    public partial string PowerReadyText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PowerReadyBrushKey { get; private set; } = "TextSecondaryBrush";

    [ObservableProperty]
    public partial MaterialIconKind PowerReadyIconKind { get; private set; } = MaterialIconKind.PowerPlug;

    private void RefreshPowerReadiness()
    {
        IsOnBattery = Page.IsOnBattery;
        BatteryChargePercent = Page.BatteryChargePercent;
        PowerReadyText = Page.PowerReadyText;
        PowerReadyBrushKey = Page.PowerReadyBrushKey;
        PowerReadyIconKind = Page.PowerReadyIconKind;
    }

    /// <summary>What this fan cools, which decides the component a learning test loads.</summary>
    [ObservableProperty]
    public partial FanCoolingRole CoolingRole { get; private set; } = FanCoolingRole.Unknown;

    /// <summary>
    /// The sensors Adaptive holds, which are also the ones a learning test measures against.
    /// </summary>
    /// <remarks>
    /// Deliberately the SAME set, not a separate choice made inside the wizard. A model fitted against
    /// sensors the controller does not read would describe a relationship nothing acts on — so the sensors
    /// are chosen once, in the editor, and the test simply uses them.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingSensorChips))]
    [NotifyPropertyChangedFor(nameof(DrivingSensorChipsVisibility))]
    [NotifyPropertyChangedFor(nameof(DrivingSensorFallbackVisibility))]
    public partial IReadOnlyList<int> DrivingSensorIndices { get; private set; } = [];

    /// <summary>
    /// The held sensors as one single-line chip label each ("CPU · Temp 2"), for READING — deliberately
    /// not a picker.
    /// </summary>
    /// <remarks>
    /// The Adaptive editor states what the loop watches without offering to change it here: the sensors are
    /// a property of what this fan physically cools, chosen in the calibration wizard (or inherited from the
    /// fan's curve), and a fitted model describes exactly that set. Sensor names arrive as two lines — index
    /// above location — which joined into a sentence rendered as a wall of wrapped text; each chip takes one
    /// line, location first because it is the half that says what the sensor is.
    /// </remarks>
    public IReadOnlyList<string> DrivingSensorChips => [.. DrivingSensorIndices.Select(index =>
    {
        var name = AvailableSensors.FirstOrDefault(sensor => sensor.SensorIndex == index)?.DisplayName ?? $"Temp {index}";
        var breakAt = name.IndexOf('\n');
        return breakAt < 0 ? name.Trim() : $"{name[(breakAt + 1)..].Trim()} · {name[..breakAt].Trim()}";
    })];

    public Visibility DrivingSensorChipsVisibility => DrivingSensorIndices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shown in place of the chips until the wizard (or an inherited curve) picks the sensors.</summary>
    public Visibility DrivingSensorFallbackVisibility => DrivingSensorIndices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Opens the calibration wizard.</summary>
    public IRelayCommand RunCalibrationCommand { get; }

    /// <summary>Returns every staged knob to its default, without touching what the fan has learned.</summary>
    public IRelayCommand ResetToDefaultsCommand { get; }

    /// <summary>Opens the control-design explainer.</summary>
    public IRelayCommand ExplainControlCommand { get; }

    // ----- Staged settings -----

    /// <summary>Target temperature, in canonical °C. The view binds its slider through the unit converter.</summary>
    [ObservableProperty]
    public partial double TargetDraftCelsius { get; set; } = AdaptiveFanSettings.DefaultTargetCelsius;

    /// <summary>λ in seconds, presented to the user as Quick ↔ Calm rather than as a number.</summary>
    /// <remarks>
    /// Every readout in the Response card derives from this one value, so each is declared here. A computed
    /// property whose source does not notify simply never refreshes — the slider would move and the
    /// consequences beside it would sit at whatever they said when the card first appeared.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResponseName))]
    [NotifyPropertyChangedFor(nameof(SettlingText))]
    [NotifyPropertyChangedFor(nameof(SettlingBarFraction))]
    [NotifyPropertyChangedFor(nameof(IsSettlingSlow))]
    [NotifyPropertyChangedFor(nameof(SettlingBrushKey))]
    [NotifyPropertyChangedFor(nameof(SpeedChangeName))]
    [NotifyPropertyChangedFor(nameof(SpeedChangeBarFraction))]
    [NotifyPropertyChangedFor(nameof(ResponsePreview))]
    [NotifyPropertyChangedFor(nameof(LambdaText))]
    public partial double ResponseDraftSeconds { get; set; } = AdaptivePidTuning.DefaultLambdaSeconds;

    /// <summary>
    /// The fan's measured dead time, or the bootstrap default until one is measured.
    /// </summary>
    /// <remarks>
    /// The other half of every settling estimate, and it changes when a calibration lands rather than when
    /// the user touches anything — so it has to notify the same readouts λ does. Without that, the card would
    /// keep quoting the pre-calibration recovery time until the slider happened to be moved.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettlingText))]
    [NotifyPropertyChangedFor(nameof(SettlingBarFraction))]
    [NotifyPropertyChangedFor(nameof(IsSettlingSlow))]
    [NotifyPropertyChangedFor(nameof(SettlingBrushKey))]
    [NotifyPropertyChangedFor(nameof(ResponsePreview))]
    [NotifyPropertyChangedFor(nameof(ResponsePreviewDefault))]
    public partial double DeadTimeSeconds { get; private set; } = FanCalibrationSnapshot.Bootstrap.DeadTimeSeconds;

    [ObservableProperty]
    public partial bool SafetyFloorDraftEnabled { get; set; }

    [ObservableProperty]
    public partial double SafetyFloorDraftPercent { get; set; }

    // ----- Response, expressed as consequences -----
    //
    // λ is the closed-loop time constant and must never be shown as one. What the user is choosing between is
    // "recovers sooner" and "changes speed less often", so those are what the card states; the seconds live
    // in a collapsed Advanced section for anyone who wants them.

    /// <summary>The chosen response as a name — the value shown in the card header.</summary>
    public string ResponseName => ResponseDraftSeconds switch
    {
        <= 3d => "Quick",
        <= 5d => "Eager",
        <= 9d => "Steady",
        <= 13d => "Calm",
        _ => "Very calm",
    };

    /// <summary>How long a disturbance takes to be corrected, at the chosen response.</summary>
    private double SettlingSeconds =>
        AdaptivePidTuning.EstimateSettlingSeconds(ResponseDraftSeconds, DeadTimeSeconds);

    public string SettlingText => $"~{SettlingSeconds:0} s";

    /// <summary>Bar fill for recovery time, 0–1. Longer settling means a fuller bar.</summary>
    public double SettlingBarFraction => Math.Clamp(SettlingSeconds / SlowSettlingSeconds, 0d, 1d);

    /// <summary>
    /// True once recovery is slow enough to be worth flagging.
    /// </summary>
    /// <remarks>
    /// A warning rather than a block: a very calm fan IS what some people want on a machine that sits on a
    /// desk. The user is told the cost and left to decide.
    /// </remarks>
    public bool IsSettlingSlow => SettlingSeconds > SlowSettlingSeconds;

    public string SettlingBrushKey => IsSettlingSlow ? "StatusWarningBrush" : "StatusSuccessBrush";

    /// <summary>How restless the fan will be, named rather than measured.</summary>
    public string SpeedChangeName => ResponseDraftSeconds switch
    {
        <= 4d => "restless",
        <= 6d => "busy",
        <= 10d => "steady",
        _ => "very calm",
    };

    /// <summary>
    /// Bar fill for steadiness, 0–1 — fuller means calmer.
    /// </summary>
    /// <remarks>
    /// Deliberately the inverse of the recovery bar. The two consequences trade against each other, and
    /// showing both filling the same way would hide that choosing one costs the other.
    /// </remarks>
    public double SpeedChangeBarFraction => Math.Clamp(
        (ResponseDraftSeconds - MinimumResponseSeconds) / (MaximumResponseSeconds - MinimumResponseSeconds),
        0d,
        1d);

    /// <summary>
    /// The shape of a sudden load at the chosen response, against a ghost of the default.
    /// </summary>
    /// <remarks>
    /// A picture because the trade being made is a SHAPE — how far the temperature overshoots and how long it
    /// takes to come back — and neither number alone conveys it. The ghost is what makes it readable: a curve
    /// on its own has nothing to be higher or slower than.
    /// </remarks>
    public ObservablePoint[] ResponsePreview => BuildResponseCurve(ResponseDraftSeconds);

    /// <summary>The same curve at the default setting, drawn dashed behind the live one.</summary>
    public ObservablePoint[] ResponsePreviewDefault => BuildResponseCurve(AdaptivePidTuning.DefaultLambdaSeconds);

    /// <summary>
    /// A normalised disturbance response for one λ.
    /// </summary>
    /// <remarks>
    /// Illustrative rather than simulated. Drawing the real controller against the real plant would cost far
    /// more than a thumbnail is worth and would not read any differently at this size, so what is plotted is
    /// the one thing that has to be true: a temperature excursion that gets HIGHER and stays out LONGER the
    /// calmer the setting, on a rise the user's choice cannot change.
    /// </remarks>
    private ObservablePoint[] BuildResponseCurve(double lambdaSeconds)
    {
        const int points = 80;

        var lambda = Math.Clamp(lambdaSeconds, MinimumResponseSeconds, MaximumResponseSeconds);
        var settling = Math.Max(AdaptivePidTuning.EstimateSettlingSeconds(lambda, DeadTimeSeconds), 1d);

        // The rise is the PLANT's, not the controller's: heat takes as long to arrive as it takes, and no
        // choice of λ makes the temperature move sooner. Holding it fixed across both curves is what lets the
        // eye read the difference as height and width rather than as the whole shape sliding sideways.
        var rise = Math.Max(1d, DeadTimeSeconds + 1d);
        var decay = Math.Max(rise * 1.15d, settling / 3d);

        // Difference of two exponentials — up over `rise`, back down over `decay` — normalised to unit peak so
        // the excursion below is the only thing setting the height.
        var peakTime = Math.Log(decay / rise) * (rise * decay) / (decay - rise);
        var unitPeak = Math.Max(Shape(peakTime), 1e-6d);

        // How far the temperature gets before the loop catches it, which grows with λ. Not normalising THIS
        // away is the whole point — two curves rescaled to the same height would claim the settings overshoot
        // equally, which is exactly the cost the user is being asked to weigh.
        var excursion = lambda / AdaptivePidTuning.MaximumLambdaSeconds;

        // One shared time base for both curves, wide enough for the calmest setting. Letting each fit its own
        // window would draw them the same width and erase "takes longer to come back" entirely.
        var window = ResponsePreviewSpans
            * AdaptivePidTuning.EstimateSettlingSeconds(MaximumResponseSeconds, DeadTimeSeconds);

        var curve = new ObservablePoint[points];
        for (var i = 0; i < points; i++)
        {
            var t = window * i / (points - 1);
            curve[i] = new ObservablePoint(t, excursion * Shape(t) / unitPeak);
        }

        return curve;

        double Shape(double t) => Math.Exp(-t / decay) - Math.Exp(-t / rise);
    }

    /// <summary>λ itself, for the Advanced disclosure.</summary>
    public string LambdaText => $"{ResponseDraftSeconds:0.#} s";

    // STORED, not computed: both read the selected fan's CALIBRATION as well as λ, and a calibration landing
    // is not a λ change — so as computed getters declared only under ResponseDraftSeconds they never
    // re-raised, and the Advanced disclosure kept showing "—" beside freshly measured numbers.

    /// <summary>The proportional gain the tuning rule derives at this λ.</summary>
    [ObservableProperty]
    public partial string ProportionalGainText { get; private set; } = "—";

    /// <summary>The integral time the tuning rule derives at this λ.</summary>
    [ObservableProperty]
    public partial string IntegralTimeText { get; private set; } = "—";

    private void RefreshAdvancedGains()
    {
        ProportionalGainText = FormatGain(gains => gains.ProportionalGain);
        IntegralTimeText = FormatGain(gains => gains.IntegralTimeSeconds, "0.# s");
    }

    /// <summary>Above this, recovery is slow enough that the card says so.</summary>
    private const double SlowSettlingSeconds = 58d;

    /// <summary>
    /// How many settling times the preview plots, so the tail of the curve is visibly flat.
    /// </summary>
    /// <remarks>
    /// Stopping at exactly one settling time would cut every curve off mid-recovery and make the calm setting
    /// look like it never comes back at all.
    /// </remarks>
    private const double ResponsePreviewSpans = 1.35d;

    private string FormatGain(Func<AdaptivePidGains, double> select, string format = "0.##")
    {
        var calibration = SelectedFan?.ControlState?.Calibration;
        if (calibration is null || !calibration.IsUsable)
        {
            return "—";
        }

        var gains = AdaptivePidTuning.Compute(calibration, ResponseDraftSeconds);
        return gains.ProportionalGain > 0d
            ? select(gains).ToString(format, System.Globalization.CultureInfo.CurrentCulture)
            : "—";
    }

    /// <summary>Caption under the floor slider, bound to what was measured rather than a constant.</summary>
    [ObservableProperty]
    public partial string SafetyFloorCaption { get; private set; } = string.Empty;

    public double MinimumTargetCelsius => AdaptiveFanSettings.MinimumTargetCelsius;

    public double MaximumTargetCelsius => AdaptiveFanSettings.MaximumTargetCelsius;

    // ----- Unit-aware slider surface -----
    //
    // A slider editing a quantity has to present its Minimum, Maximum AND Value in the display unit, so a
    // Fahrenheit user gets a Fahrenheit scale rather than a Celsius one wearing a °F label. It cannot be done
    // with a converter: UnitValueConverter is one-way by design and throws on ConvertBack, because a TwoWay
    // binding through it would write display units into canonical state. So the conversion lives here, and
    // the slider binds these directly with no converter at all.

    /// <summary>The staged target in the user's chosen unit (TwoWay slider value).</summary>
    [ObservableProperty]
    public partial double TargetDisplayValue { get; set; }

    [ObservableProperty]
    public partial double TargetDisplayMinimum { get; private set; }

    [ObservableProperty]
    public partial double TargetDisplayMaximum { get; private set; }

    /// <summary>The staged floor in the user's chosen unit (TwoWay slider value).</summary>
    /// <remarks>
    /// A ratio, which every supported unit renders identically — but it goes through the service anyway, so
    /// that adding a unit option later does not leave this one slider quietly untranslated.
    /// </remarks>
    [ObservableProperty]
    public partial double SafetyFloorDisplayValue { get; set; }

    [ObservableProperty]
    public partial double SafetyFloorDisplayMaximum { get; private set; }

    /// <summary>
    /// One duty point, expressed in the display ratio unit — the slider's step.
    /// </summary>
    /// <remarks>
    /// The scale is converted but the step was left at WinUI's default of 1, which is only correct while the
    /// ratio unit happens to be percent. Under a fraction preference the whole scale is 0–1, so a step of 1
    /// gave the control exactly two reachable positions.
    /// </remarks>
    [ObservableProperty]
    public partial double SafetyFloorDisplayStep { get; private set; } = 1d;

    /// <summary>Guards the display → canonical → display round trip from chasing its own tail.</summary>
    private bool _suppressUnitSync;

    partial void OnTargetDisplayValueChanged(double value)
    {
        if (_suppressUnitSync)
        {
            return;
        }

        TargetDraftCelsius = Math.Clamp(
            _unitFormattingService.ConvertTemperatureToCelsius(value),
            AdaptiveFanSettings.MinimumTargetCelsius,
            AdaptiveFanSettings.MaximumTargetCelsius);
    }

    partial void OnSafetyFloorDisplayValueChanged(double value)
    {
        if (_suppressUnitSync)
        {
            return;
        }

        SafetyFloorDraftPercent = Math.Clamp(
            _unitFormattingService.ConvertRatioToPercent(value),
            0d,
            AdaptiveFanSettings.MaximumSafetyFloorPercent);
    }

    /// <summary>Re-projects the canonical staged values into the display unit.</summary>
    /// <remarks>
    /// Called both when the canonical value moves and when the user changes their unit preference — the
    /// second is why the bounds are stored rather than computed: the whole scale has to move, not the label.
    /// </remarks>
    private void RefreshUnitAwareSliders()
    {
        _suppressUnitSync = true;

        try
        {
            TargetDisplayMinimum = _unitFormattingService.ConvertTemperature(AdaptiveFanSettings.MinimumTargetCelsius);
            TargetDisplayMaximum = _unitFormattingService.ConvertTemperature(AdaptiveFanSettings.MaximumTargetCelsius);
            TargetDisplayValue = _unitFormattingService.ConvertTemperature(TargetDraftCelsius);

            SafetyFloorDisplayMaximum = _unitFormattingService.ConvertRatio(AdaptiveFanSettings.MaximumSafetyFloorPercent);
            SafetyFloorDisplayValue = _unitFormattingService.ConvertRatio(SafetyFloorDraftPercent);

            // One canonical duty point in display units. Zero would freeze the slider, so it falls back to
            // the WinUI default if a unit ever converted a single point to nothing.
            var step = _unitFormattingService.ConvertRatio(1d);
            SafetyFloorDisplayStep = double.IsFinite(step) && step > 0d ? step : 1d;
        }
        finally
        {
            _suppressUnitSync = false;
        }
    }

    public double MinimumResponseSeconds => AdaptivePidTuning.MinimumLambdaSeconds;

    public double MaximumResponseSeconds => AdaptivePidTuning.MaximumLambdaSeconds;

    public double MaximumSafetyFloorPercent => AdaptiveFanSettings.MaximumSafetyFloorPercent;

    /// <summary>The staged settings, for the coordinator to flush on Apply.</summary>
    public AdaptiveFanSettings BuildDraft()
        => new AdaptiveFanSettings
        {
            TargetTemperatureCelsius = TargetDraftCelsius,
            LambdaSeconds = ResponseDraftSeconds,
            SafetyFloorEnabled = SafetyFloorDraftEnabled,
            SafetyFloorPercent = SafetyFloorDraftPercent,
        }.Sanitized();

    // Every draft change stages, so the page's Preview/Apply bar lights up exactly as it does for a curve
    // edit. Without these the sliders would move and then silently do nothing on Apply.
    partial void OnTargetDraftCelsiusChanged(double value)
    {
        RefreshUnitAwareSliders();
        StageDraft();
    }

    partial void OnResponseDraftSecondsChanged(double value)
    {
        // λ is the other input to both figures; the calibration side is picked up by RefreshDerivedState.
        RefreshAdvancedGains();
        StageDraft();
    }

    partial void OnSafetyFloorDraftEnabledChanged(bool value) => StageDraft();

    partial void OnSafetyFloorDraftPercentChanged(double value)
    {
        RefreshUnitAwareSliders();
        StageDraft();
    }

    private void StageDraft()
    {
        // Guarded against the refresh path: adopting the service's values must not look like a user edit, or
        // every telemetry tick would mark the page dirty and the Apply bar would never go away.
        if (_isAdoptingFromService || SelectedFan is not { } fan)
        {
            return;
        }

        Page.StageAdaptiveSettings(fan.Snapshot.FanIndex, BuildDraft());
    }

    private bool _isAdoptingFromService;


    /// <summary>
    /// The coordinator properties this body mirrors, on top of the base list.
    /// </summary>
    /// <remarks>
    /// The base gates every refresh on this, so omitting the override leaves the whole readout refreshing
    /// only by accident — whichever unrelated coordinator property happens to change on the same pass.
    /// </remarks>
    protected override bool AffectsDerivedState(string propertyName) => propertyName switch
    {
        nameof(FanCurveProfilesModel.CanCommandFanMode) => true,
        // The wizard's power readiness is mirrored from these, and the charger going in or out is exactly
        // the change the blocked-on-battery screen has to notice.
        nameof(FanCurveProfilesModel.IsOnBattery) => true,
        nameof(FanCurveProfilesModel.BatteryChargePercent) => true,
        nameof(FanCurveProfilesModel.PowerReadyText) => true,
        nameof(FanCurveProfilesModel.PowerReadyBrushKey) => true,
        nameof(FanCurveProfilesModel.PowerReadyIconKind) => true,
        _ => base.AffectsDerivedState(propertyName),
    };

    protected override void RefreshDerivedState()
    {
        base.RefreshDerivedState();
        FollowSelectedFan();
        RefreshPowerReadiness();
        RefreshAdaptiveState();
        RefreshAdvancedGains();

        // Also re-projects the sliders, which is how a change of display unit moves the whole scale rather
        // than just relabelling a Celsius one.
        RefreshUnitAwareSliders();
    }

    /// <summary>
    /// Subscribes to the selected fan's own notifications, which is where live control state actually lands.
    /// </summary>
    /// <remarks>
    /// The coordinator raises <c>SelectedFan</c> only when the SELECTION changes; a telemetry tick replaces
    /// <see cref="FanCardModel.ControlState"/> on the same card object, so nothing the base watches moves.
    /// Without this the entire controller readout — setpoint, actual speed, driving temperature, the
    /// contribution bar, the confidence card — paints once and then sits frozen for as long as the editor is
    /// open, which is indistinguishable from a controller that has stopped.
    /// </remarks>
    private void FollowSelectedFan()
    {
        if (ReferenceEquals(_followedFan, SelectedFan))
        {
            return;
        }

        if (_followedFan is not null)
        {
            _followedFan.PropertyChanged -= OnSelectedFanPropertyChanged;
        }

        _followedFan = SelectedFan;

        if (_followedFan is not null)
        {
            _followedFan.PropertyChanged += OnSelectedFanPropertyChanged;
        }
    }

    private void OnSelectedFanPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(FanCardModel.ControlState))
        {
            RefreshAdaptiveState();
        }
    }

    /// <summary>
    /// Releases the selected fan's subscription.
    /// </summary>
    /// <remarks>
    /// The card outlives this body, and a card holding a handler into it would keep the whole editor alive
    /// for as long as the fan exists. Detach is not virtual, so the release hangs off Dispose.
    /// </remarks>
    /// <summary>
    /// Releases the followed fan card as well as the coordinator.
    /// </summary>
    /// <remarks>
    /// On Detach rather than only on Dispose: the view's Unloaded handler calls Detach, and nothing in the
    /// app disposes a navigation-resolved mode model — so releasing here was the difference between one
    /// handler per fan switch and one handler per fan switch for the life of the process.
    /// </remarks>
    public override void Detach()
    {
        if (_followedFan is not null)
        {
            _followedFan.PropertyChanged -= OnSelectedFanPropertyChanged;
            _followedFan = null;
        }

        base.Detach();
    }

    /// <summary>The card currently subscribed to, so the handler can be swapped when the selection moves.</summary>
    private FanCardModel? _followedFan;

    private void RefreshAdaptiveState()
    {
        var state = SelectedFan?.ControlState;

        RefreshSettingsDraft(state?.AdaptiveSettings);
        RefreshConfidence(state);
        RefreshController(state?.AdaptiveControl);
        RefreshFloorCaption(state);
    }

    private void RefreshSettingsDraft(AdaptiveFanSettings? settings)
    {
        // Only adopt from the service while THIS fan has nothing staged; otherwise a telemetry tick would
        // overwrite a slider mid-drag. Deliberately per-fan: the page-wide check this used to make meant a
        // staged edit on any fan froze the editor for every fan, so selecting another Adaptive fan kept the
        // first fan's values on screen and the next nudge staged them against the wrong fan.
        if (settings is null || SelectedFan is not { } fan || Page.HasStagedAdaptiveSettingsFor(fan.Snapshot.FanIndex))
        {
            return;
        }

        _isAdoptingFromService = true;
        try
        {
            TargetDraftCelsius = settings.TargetTemperatureCelsius;
            ResponseDraftSeconds = settings.LambdaSeconds;
            SafetyFloorDraftEnabled = settings.SafetyFloorEnabled;
            SafetyFloorDraftPercent = settings.SafetyFloorPercent;
        }
        finally
        {
            _isAdoptingFromService = false;
        }
    }

    private void RefreshController(AdaptiveControlDecision? control)
    {
        if (control is not { IsDriven: true })
        {
            IsControllerRunning = false;
            IsThrottleLatched = false;
            if (LegendEntries.Count > 0)
            {
                LegendEntries = [];
            }

            return;
        }

        IsControllerRunning = true;
        SetpointRpm = control.SetpointRpm;
        ActualRpm = SelectedFan?.Snapshot.SpeedRpm;
        DrivingTemperatureCelsius = control.DrivingTemperatureCelsius;
        TargetTemperatureCelsius = control.TargetTemperatureCelsius;
        IsFeedForwardUnavailable = control.IsFeedForwardUnavailable;

        RefreshTracking(control);
        RefreshContributionBar(control);
        RefreshThrottleLatch(control);

        ExplanationText = control.IsThrottleLatched
            ? "The fan sped up because the processor reported throttling — the escalation holds until temperature settles."
            : control.IsFeedForwardUnavailable
                ? "No power reading is available, so the fan can only respond after the temperature moves."
                : "Feed-forward reacts to power before the temperature moves; the trim corrects whatever it misses.";
    }

    private void RefreshTracking(AdaptiveControlDecision control)
    {
        // Only meaningful under cascade, where a speed is actually commanded. Under duty tracking there is no
        // setpoint to miss, so claiming "off setpoint" would be inventing a fault.
        if (control.SetpointRpm is not double setpoint || SelectedFan?.Snapshot.SpeedRpm is not double actual)
        {
            IsTracking = true;
            TrackingText = "Holding temperature";
            return;
        }

        var error = Math.Abs(setpoint - actual);
        IsTracking = error <= TrackingToleranceRpm;
        TrackingText = IsTracking
            ? "Tracking setpoint"
            : $"Off setpoint by {_unitFormattingService.FormatFanSpeed(error, decimals: 0)}";
    }

    private void RefreshContributionBar(AdaptiveControlDecision control)
    {
        // Shares of the raw demand. Terms can be negative (the trim pulling duty back), and a negative width
        // is meaningless in a bar, so only positive contributions are drawn — the legend still reports the
        // signed value, so nothing is hidden.
        var feedForward = Math.Max(0d, control.FeedForwardDutyPercent);
        var trim = Math.Max(0d, control.ProportionalIntegralDutyPercent);
        var lead = Math.Max(0d, control.LeadDutyPercent);
        var throttle = Math.Max(0d, control.ThrottleEscalationDutyPercent);
        var total = feedForward + trim + lead + throttle;

        if (total <= 0d)
        {
            FeedForwardShare = new GridLength(1, GridUnitType.Star);
            ProportionalIntegralShare = new GridLength(0, GridUnitType.Star);
            LeadShare = new GridLength(0, GridUnitType.Star);
            ThrottleShare = new GridLength(0, GridUnitType.Star);
            if (LegendEntries.Count > 0)
            {
                LegendEntries = [];
            }

            return;
        }

        FeedForwardShare = new GridLength(feedForward, GridUnitType.Star);
        ProportionalIntegralShare = new GridLength(trim, GridUnitType.Star);
        LeadShare = new GridLength(lead, GridUnitType.Star);
        ThrottleShare = new GridLength(throttle, GridUnitType.Star);

        // Terms contributing nothing are omitted entirely rather than rendered as empty rows.
        List<AdaptiveTermLegendEntry> entries = [];
        AddLegendEntry(entries, "Feed-forward", control.FeedForwardDutyPercent, "BrandPrimaryBrush");
        AddLegendEntry(entries, "Trim", control.ProportionalIntegralDutyPercent, "BrandSecondaryBrush");
        AddLegendEntry(entries, "Rising", control.LeadDutyPercent, "StatusSuccessBrush");
        AddLegendEntry(entries, "Throttle escalation", control.ThrottleEscalationDutyPercent, "StatusWarningBrush");
        ReplaceIfChanged(entries, value => LegendEntries = value, LegendEntries);
    }

    /// <summary>
    /// Assigns a rebuilt list only when its CONTENT differs from what is already there.
    /// </summary>
    /// <remarks>
    /// These lists are rebuilt on every telemetry tick, and handing an ItemsRepeater a brand-new list makes
    /// it tear down and re-create every row — several times a second, against the repo's live-update
    /// stability rule, and visible as flicker and lost pointer state. The entries are records, so value
    /// equality settles whether anything actually moved.
    /// </remarks>
    private static void ReplaceIfChanged<T>(IReadOnlyList<T> next, Action<IReadOnlyList<T>> assign, IReadOnlyList<T> current)
    {
        if (!current.SequenceEqual(next))
        {
            assign(next);
        }
    }

    private void AddLegendEntry(List<AdaptiveTermLegendEntry> entries, string name, double dutyPercent, string brushKey)
    {
        if (Math.Abs(dutyPercent) < 0.05d)
        {
            return;
        }

        var formatted = _unitFormattingService.FormatRatio(Math.Abs(dutyPercent), decimals: 0);
        var sign = dutyPercent > 0d && entries.Count > 0 ? "+" : dutyPercent < 0d ? "−" : string.Empty;

        entries.Add(new AdaptiveTermLegendEntry(name, $"{sign}{formatted}", brushKey));
    }

    private void RefreshThrottleLatch(AdaptiveControlDecision control)
    {
        IsThrottleLatched = control.IsThrottleLatched;
        if (!control.IsThrottleLatched)
        {
            return;
        }

        var releaseText = control.ThrottleLatchReleaseSeconds is double seconds
            ? $" Releasing in {seconds:0} s once it stays below target."
            : string.Empty;

        var atText = control.ThrottleLatchedAt is DateTimeOffset latchedAt
            ? latchedAt.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture)
            : "recently";

        ThrottleLatchText =
            $"The processor reported thermal throttling at {atText}. Adaptive is holding an elevated speed.{releaseText}";
    }

    private void RefreshConfidence(FanControlStateSnapshot? state)
    {
        var learning = state?.AdaptiveLearning ?? AdaptiveLearningState.None;
        HasLearnedAnything = learning.HasLearned;

        // Whether Adaptive can run here at all — asked of the page so this editor and the Preview/Apply
        // buttons can never disagree about it.
        //
        // Deliberately NOT just "nothing measured and nothing learned". A fan can carry a model and no
        // DRIVING SENSORS (a config written before sensors were persisted restores exactly that), and the
        // loop cannot run without them. The wizard is the only place sensors can be chosen, so this editor —
        // which states the sensors but never offers to change them — is a dead end in that state: it showed
        // a tuning page for a loop that could not start, and Apply died on the service's refusal. The
        // consent panel and its Calibrate button are the only honest thing to show.
        var calibration = state?.Calibration ?? FanCalibrationSnapshot.None;
        IsAwaitingFirstLearning = !Page.SelectedFanCanRunAdaptive;

        DeadTimeSeconds = calibration.DeadTimeSeconds > 0d
            ? calibration.DeadTimeSeconds
            : FanCalibrationSnapshot.Bootstrap.DeadTimeSeconds;

        CoolingRole = state?.CoolingRole ?? FanCoolingRole.Unknown;
        DrivingSensorIndices = state?.DrivingSensorIndices ?? [];

        var confidence = learning.ConfidenceAt(DateTimeOffset.UtcNow);
        List<AdaptiveKnownFact> facts = [];

        RefreshGainHistory(learning);

        switch (confidence)
        {
            case AdaptiveConfidence.Confident:
                ConfidenceIconKind = MaterialIconKind.CheckDecagram;
                ConfidenceChip = "Settled";
                ConfidenceHeadline = "Knows this fan well";
                ConfidenceBody =
                    "The model has been steady across hundreds of quiet periods. Nothing to do here — and it "
                    + "will notice on its own if the machine changes.";
                break;

            case AdaptiveConfidence.Converging:
                ConfidenceIconKind = MaterialIconKind.ChartBellCurveCumulative;
                ConfidenceChip = "Refining";
                ConfidenceHeadline = $"Learned from {learning.ObservationCount} quiet periods";
                ConfidenceBody =
                    "Adaptive has its own model of this fan now and keeps refining it. These numbers are "
                    + "already better than the defaults.";
                break;

            default:
                ConfidenceIconKind = MaterialIconKind.SchoolOutline;
                ConfidenceChip = "Learning";
                ConfidenceHeadline = "Still getting to know this fan";
                ConfidenceBody =
                    "Adaptive is running on what the test measured and watching how this machine behaves "
                    + "from here. It only learns from settled, quiet moments, so this takes a while — the "
                    + "fan is doing its job the whole time.";
                break;
        }

        facts.Add(new AdaptiveKnownFact("Quiet periods seen", learning.ObservationCount.ToString(System.Globalization.CultureInfo.CurrentCulture), MaterialIconKind.EyeOutline));

        if (learning.IdentifiedProcessGainCelsiusPerPercent is double gain)
        {
            // A DELTA, not a temperature: °C per duty point carries no scale offset, and the absolute
            // formatter would add the freezing point and render 0.35 °C/% as "32.63 °F".
            facts.Add(new AdaptiveKnownFact(
                "Cooling per 1% fan",
                $"{_unitFormattingService.ConvertTemperatureDelta(gain):0.##} {_unitFormattingService.TemperatureUnitSuffix}/%",
                MaterialIconKind.SnowflakeThermometer));
        }
        else
        {
            // Nothing identified from live use YET — but this card is only reachable on a fan that has been
            // calibrated, so what it is running on is that measurement, not a built-in guess. Saying "safe
            // defaults" here told a user who had just sat through a four-minute hot test that it had been
            // ignored.
            facts.Add(state?.Calibration.IsMeasured == true
                ? new AdaptiveKnownFact("Running on", "Its own measurement", MaterialIconKind.ShieldCheck)
                : new AdaptiveKnownFact("Running on", "Safe defaults", MaterialIconKind.ShieldHalfFull));
        }

        if (state?.Calibration is { MinimumSpinRpm: > 0d } calibrated)
        {
            facts.Add(new AdaptiveKnownFact(
                "Stalls below",
                _unitFormattingService.FormatFanSpeed(calibrated.MinimumSpinRpm, decimals: 0),
                MaterialIconKind.FanMinus));
        }

        AddMeasuredFacts(state, facts);

        ReplaceIfChanged(facts, value => KnownFacts = value, KnownFacts);
    }

    /// <summary>
    /// Facts the hot test measures that the confidence card was designed before we could produce.
    /// </summary>
    /// <remarks>
    /// Added as tiles rather than as charts on purpose. The design settled on Facts as the treatment that
    /// makes the card accountable — concrete numbers the user can check — and these are exactly that shape.
    /// A gain curve rendered as a plot would ask the user to interpret a chart to learn something a sentence
    /// states outright.
    /// </remarks>
    private void AddMeasuredFacts(FanControlStateSnapshot? state, List<AdaptiveKnownFact> facts)
    {
        if (state is null)
        {
            return;
        }

        // What this fan cools, not where it sits. Worth stating here because it is also what decides which
        // component a learning test would heat, which is otherwise invisible to the user.
        if (FrameworkFanNameDisplay.ToFunction(state.CoolingRole) is { } function)
        {
            facts.Add(new AdaptiveKnownFact("This fan cools", function, MaterialIconKind.FanChevronUp));
        }

        // Diminishing returns, stated rather than plotted: the ratio between what a duty point buys down low
        // and what it buys near maximum is the single number that explains why the last 20% of fan is mostly
        // noise.
        var curve = state.Calibration.GainCurve;
        if (curve.IsUsable)
        {
            // Sampled inside the measured range rather than at its ends: the sweep visits 22% and 100%, and
            // reading exactly at those points would use the outermost segment's slope on one side only. 30
            // and 90 sit within the curve on both sides, so the ratio compares two interpolated slopes.
            const double lowSampleDuty = 30d;
            const double highSampleDuty = 90d;

            var low = curve.GainAt(lowSampleDuty, state.Calibration.ProcessGainCelsiusPerPercent);
            var high = curve.GainAt(highSampleDuty, state.Calibration.ProcessGainCelsiusPerPercent);

            if (high > 0d && low > high)
            {
                facts.Add(new AdaptiveKnownFact(
                    "Airflow worth more at",
                    $"low speed — {low / high:0.#}× vs high",
                    MaterialIconKind.ChartBellCurveCumulative));
            }
        }

        // What the noise actually buys. The unflattering answer is the valuable one: a machine limited by its
        // power budget rather than its heatsink gains nothing from a louder fan, and nowhere else in the app
        // can say so.
        if (state.Calibration.PerformanceResponse.SustainedSpeedGainFraction is double gained)
        {
            facts.Add(new AdaptiveKnownFact(
                "Full fan buys",
                gained >= FanPerformanceResponse.MeaningfulSpeedGainFraction
                    ? $"+{gained:P0} sustained speed"
                    : "no extra speed",
                MaterialIconKind.Speedometer));
        }
    }

    private void RefreshFloorCaption(FanControlStateSnapshot? state)
    {
        // Source-agnostic wording: true whether the number came from a calibration run or from learning.
        SafetyFloorCaption = state?.Calibration is { MinimumSpinRpm: > 0d } calibration
            ? $"Below about {_unitFormattingService.FormatFanSpeed(calibration.MinimumSpinRpm, decimals: 0)} this fan stalls."
            : "Keeps the fan turning even when the machine is cold.";
    }

    private async Task ReleaseThrottleLatchAsync()
    {
        if (SelectedFan is { } fan)
        {
            await Page.ReleaseThrottleLatchAsync(fan.Snapshot.FanIndex).ConfigureAwait(true);
        }
    }

    private async Task ForgetLearningAsync()
    {
        if (SelectedFan is { } fan)
        {
            await Page.ForgetAdaptiveLearningAsync(fan.Snapshot.FanIndex).ConfigureAwait(true);
        }
    }

    // Synchronous: both only raise an intent for the view to act on. Wrapping them in Task.CompletedTask
    // would dress a plain event raise as asynchronous work and lose the disabled-while-running behaviour that
    // is the only reason to reach for an async command in the first place.
    private void RequestCalibration() => CalibrationRequested?.Invoke(this, EventArgs.Empty);

    private void RequestExplainer() => ExplainerRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Returns every knob to its default — and nothing else.
    /// </summary>
    /// <remarks>
    /// Pointedly does NOT discard what the fan has learned. These are two different intentions: "I have
    /// fiddled with the sliders and want to start over" is routine, while "this machine changed physically
    /// and the model is now describing hardware that no longer exists" is rare and destructive. Folding them
    /// together would make an everyday button quietly throw away days of observation.
    /// </remarks>
    private void ResetToDefaults()
    {
        var defaults = AdaptiveFanSettings.Default;

        TargetDraftCelsius = defaults.TargetTemperatureCelsius;
        SafetyFloorDraftEnabled = defaults.SafetyFloorEnabled;
        SafetyFloorDraftPercent = defaults.SafetyFloorPercent;
        ResponseDraftSeconds = AdaptivePidTuning.DefaultLambdaSeconds;
    }

    /// <summary>How far actual speed may sit from the setpoint before the loop reads as "off".</summary>
    /// <remarks>
    /// Generous on purpose. A fan is a mechanical device answering a firmware loop; a couple of hundred RPM of
    /// wander is normal and flagging it would train the user to ignore the indicator.
    /// </remarks>
    private const double TrackingToleranceRpm = 350d;
}

/// <summary>One row under the contribution bar.</summary>
/// <param name="Name">The term, in the user's language rather than the controller's.</param>
/// <param name="Value">Its signed contribution, already unit-formatted.</param>
/// <param name="BrushKey">Theme brush key for the swatch, so the legend and the bar cannot drift apart.</param>
public sealed record AdaptiveTermLegendEntry(string Name, string Value, string BrushKey);

/// <summary>One concrete thing the controller has worked out about this fan.</summary>
/// <param name="Label">What it is, in plain language.</param>
/// <param name="Value">Already unit-formatted.</param>
/// <param name="IconKind">Material icon name.</param>
public sealed record AdaptiveKnownFact(string Label, string Value, MaterialIconKind IconKind);
