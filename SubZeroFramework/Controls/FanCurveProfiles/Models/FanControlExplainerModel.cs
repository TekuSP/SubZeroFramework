using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

using Material.Icons;

using SkiaSharp;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Control;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// A row in the "where each number comes from" table: one value, and whether it was measured or decided.
/// </summary>
/// <param name="IsMeasured">
/// The distinction the whole page exists to draw. A measured number differs per fan and per machine and is
/// re-learned on every calibration; a decided one is the same everywhere and can be argued with.
/// </param>
public sealed record FanExplainerProvenance(bool IsMeasured, string Name, string Value, string Why)
{
    public string Tag => IsMeasured ? "measured" : "decided";

    public string TagBrushKey => IsMeasured ? "StatusSuccessBrush" : "BrandPrimaryBrush";
}

/// <summary>One measured plant parameter, shown beside the step response it came from.</summary>
public sealed record FanExplainerPlantValue(string Symbol, string BrushKey, string Value, string Description);

/// <summary>One box in the cascade diagram.</summary>
public sealed record FanExplainerChainLink(
    string Kind,
    MaterialIconKind IconKind,
    string BrushKey,
    string Title,
    string Body,
    string Signal)
{
    public bool HasSignal => !string.IsNullOrEmpty(Signal);
}

/// <summary>One term or guard in the outer control law.</summary>
public sealed record FanExplainerLawRow(
    string Term,
    string Role,
    MaterialIconKind IconKind,
    string BrushKey,
    string Purpose,
    string Notes,
    string Warning)
{
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
}

/// <summary>
/// The reference behind the Adaptive editor: what is measured, what was decided, and why.
/// </summary>
/// <remarks>
/// <para>
/// Every figure here is this fan's, pulled from its own calibration rather than from an example. A page of
/// worked numbers that belong to somebody else's machine teaches the theory and answers none of the questions
/// a user actually has about their own fan.
/// </para>
/// <para>
/// Read-only by construction. It is opened from the editor mid-edit, so it takes a snapshot and never writes
/// back — nothing here can disturb whatever the user has staged.
/// </para>
/// </remarks>
public sealed partial class FanControlExplainerModel
{
    private readonly IUnitFormattingService _units;

    public FanControlExplainerModel(
        FanCardModel fan,
        FanCalibrationSnapshot? calibration,
        AdaptiveFanSettings? settings,
        AdaptiveControlDecision? control,
        FanCoolingRole role,
        IUnitFormattingService units)
    {
        ArgumentNullException.ThrowIfNull(fan);
        ArgumentNullException.ThrowIfNull(units);

        _units = units;

        var model = calibration is { IsUsable: true } measured ? measured : FanCalibrationSnapshot.Bootstrap;
        IsMeasured = calibration is { IsUsable: true };

        FanName = fan.Snapshot.DisplayName;
        RoleChip = role switch
        {
            FanCoolingRole.Cpu => "CPU fan",
            FanCoolingRole.Gpu => "GPU fan",
            FanCoolingRole.System => "System fan",
            _ => "Fan",
        } + $" · Slot {fan.Snapshot.FanIndex + 1}";

        StateChip = IsMeasured ? "Calibrated" : "Running on safe defaults";
        StateBrushKey = IsMeasured ? "StatusSuccessBrush" : "StatusWarningBrush";

        Lambda = settings?.LambdaSeconds ?? AdaptivePidTuning.DefaultLambdaSeconds;
        TargetCelsius = settings?.TargetTemperatureCelsius ?? AdaptiveFanSettings.DefaultTargetCelsius;

        BuildPlant(model);
        BuildTuning(model);
        BuildCascade(model, control);
        BuildControlLaw(control);
        BuildProvenance(model);
    }

    public string FanName { get; }

    public string RoleChip { get; }

    public string StateChip { get; }

    public string StateBrushKey { get; }

    /// <summary>
    /// Whether the numbers on this page were measured on this fan or are the shipped defaults.
    /// </summary>
    /// <remarks>
    /// Stated rather than hidden. An uncalibrated fan still gets the page — the theory is the same and the
    /// defaults are real — but presenting bootstrap constants as if they had been measured here would be a
    /// lie about the one thing the page is for.
    /// </remarks>
    public bool IsMeasured { get; }

    public string ProvenanceFooter => IsMeasured
        ? $"Values shown are this machine's, for {FanName}. Anything tagged “measured” is re-learned on every "
          + "calibration and differs per fan and per machine."
        : $"{FanName} has not been calibrated, so the measured values below are the shipped starting points "
          + "rather than this machine's. Calibrating replaces every one of them.";

    // ----- 1. The plant -----

    public double Lambda { get; }

    public double TargetCelsius { get; }

    public ObservablePoint[] StepResponse { get; private set; } = [];

    public FanExplainerPlantValue[] PlantValues { get; private set; } = [];

    public string StepResponseCaption { get; private set; } = string.Empty;

    public string DeadTimeCallout { get; private set; } = string.Empty;

    public SolidColorPaint StepResponsePaint { get; } = new(new SKColor(0xE0, 0x73, 0x6C)) { StrokeThickness = 2.4f };

    public SolidColorPaint DeadTimePaint { get; } = new(new SKColor(0xC5, 0x99, 0x4E))
    {
        StrokeThickness = 1f,
        PathEffect = new DashEffect([4f, 4f]),
    };

    // ----- 2. Tuning -----

    public string LambdaText { get; private set; } = string.Empty;

    public string LambdaMultipleText { get; private set; } = string.Empty;

    public string ProportionalGainText { get; private set; } = string.Empty;

    public string IntegralTimeText { get; private set; } = string.Empty;

    public string IntegralGainText { get; private set; } = string.Empty;

    public string SettlingText { get; private set; } = string.Empty;

    // ----- 3. Cascade -----

    public FanExplainerChainLink[] Chain { get; private set; } = [];

    public bool IsCascade { get; private set; }

    public string TrackingVerdictTitle { get; private set; } = string.Empty;

    public string TrackingVerdictBody { get; private set; } = string.Empty;

    public string TrackingVerdictPlain { get; private set; } = string.Empty;

    public MaterialIconKind TrackingVerdictIconKind { get; private set; }

    public string TrackingVerdictBrushKey { get; private set; } = string.Empty;

    // ----- 4. The control law -----

    public FanExplainerLawRow[] LawRows { get; private set; } = [];

    public bool HasLiveDemand { get; private set; }

    public string LiveDemandText { get; private set; } = string.Empty;

    // ----- 5. Provenance -----

    public FanExplainerProvenance[] Provenance { get; private set; } = [];

    private void BuildPlant(FanCalibrationSnapshot model)
    {
        const int points = 90;

        var gain = model.ProcessGainCelsiusPerPercent;
        var tau = Math.Max(model.TimeConstantSeconds, 1d);
        var dead = Math.Max(model.DeadTimeSeconds, 0d);

        // Plotted as a temperature CHANGE, not an absolute temperature. The snapshot keeps the identified
        // model and not the raw run, so an absolute curve would need a starting temperature this page does not
        // have and would have to invent — and an invented number on the page about provenance is the one thing
        // that must not appear here. K, τ and L are all it takes to draw the shape honestly.
        var step = Math.Max(100d - model.MinimumSpinDutyPercent, 1d);
        var drop = gain * step;
        var window = dead + (tau * 3.5d);

        var curve = new ObservablePoint[points];
        for (var i = 0; i < points; i++)
        {
            var t = window * i / (points - 1);
            var change = t < dead ? 0d : -drop * (1d - Math.Exp(-(t - dead) / tau));

            // A DELTA: °C per point carries no scale offset, so the absolute converter would add the freezing
            // point and turn a 33 °C drop into a 91 °F rise.
            curve[i] = new ObservablePoint(t, _units.ConvertTemperatureDelta(change));
        }

        StepResponse = curve;
        StepResponseCaption =
            $"fan duty {_units.FormatRatio(model.MinimumSpinDutyPercent, decimals: 0)} → {_units.FormatRatio(100d, decimals: 0)} at t = 0 · settles "
            + $"{_units.ConvertTemperatureDelta(drop):0.#} {_units.TemperatureUnitSuffix} lower"
            + (IsMeasured ? string.Empty : " · shipped defaults, not measured on this fan");

        PlantValues =
        [
            new FanExplainerPlantValue(
                "K",
                "BrandSecondaryBrush",
                // A DELTA: °C per duty point carries no scale offset, so the absolute formatter would add the
                // freezing point and render 0.42 °C/% as "32.76 °F/%".
                $"{_units.ConvertTemperatureDelta(gain):0.##} {_units.TemperatureUnitSuffix} / 1% duty",
                "How much cooling one percent of fan buys"),
            new FanExplainerPlantValue("τ", "StatusSuccessBrush", $"{tau:0} s", "How long most of that change takes"),
            new FanExplainerPlantValue("L", "StatusWarningBrush", $"{dead:0.#} s", "Delay before anything shows on the sensor"),
        ];

        DeadTimeCallout =
            $"For {dead:0.#} seconds after the fan changes, the sensor says nothing. A controller that reacts "
            + $"only to temperature is always {dead:0.#} seconds late — which is why the control law below "
            + "leads with power, not temperature.";
    }

    private void BuildTuning(FanCalibrationSnapshot model)
    {
        var gains = AdaptivePidTuning.Compute(model, Lambda);
        var dead = Math.Max(model.DeadTimeSeconds, 0.1d);

        LambdaText = $"{Lambda:0.#} s";
        LambdaMultipleText = $"= {Lambda / dead:0.#}× dead time";
        ProportionalGainText = $"{gains.ProportionalGain:0.##} %/{_units.TemperatureUnitSuffix}";
        IntegralTimeText = $"{gains.IntegralTimeSeconds:0} s";
        IntegralGainText = $"{gains.IntegralGain:0.###} %/{_units.TemperatureUnitSuffix}·s";
        SettlingText = $"~{AdaptivePidTuning.EstimateSettlingSeconds(Lambda, model.DeadTimeSeconds):0} s";
    }

    private void BuildCascade(FanCalibrationSnapshot model, AdaptiveControlDecision? control)
    {
        IsCascade = model.TrackingMode == FanSpeedTrackingMode.Cascade;

        var temperature = control?.DrivingTemperatureCelsius is double driving
            ? _units.FormatTemperature(driving, decimals: 0)
            : string.Empty;

        var setpoint = control?.SetpointRpm is double rpm
            ? _units.FormatFanSpeed(rpm, decimals: 0)
            : string.Empty;

        Chain =
        [
            new FanExplainerChainLink(
                "Measurement",
                MaterialIconKind.Thermometer,
                "StatusErrorTextBrush",
                "Driving temperature",
                "Aggregate of the sensors you selected, sampled each tick.",
                temperature),
            new FanExplainerChainLink(
                "Outer loop",
                MaterialIconKind.ChartTimelineVariant,
                "BrandPrimaryBrush",
                "SIMC PI + FF + lead",
                "The control law below. Runs in SubZero, once per tick.",
                string.Empty),
            new FanExplainerChainLink(
                "Setpoint",
                MaterialIconKind.Target,
                "BrandSecondaryBrush",
                IsCascade ? "Commanded RPM" : "Commanded duty",
                IsCascade ? "A speed request, not a duty percentage." : "A duty percentage, converted from the RPM demand.",
                setpoint),
            new FanExplainerChainLink(
                "Inner loop",
                MaterialIconKind.Chip,
                "StatusSuccessBrush",
                IsCascade ? "The embedded controller" : "Direct duty",
                IsCascade
                    ? "Already exists. The EC holds the commanded speed against its own tachometer, far faster than we could."
                    : "No inner loop. The duty goes straight to the motor, so nothing corrects for a fan that is slowing down.",
                string.Empty),
            new FanExplainerChainLink(
                "Plant",
                MaterialIconKind.Fan,
                "TextSecondaryBrush",
                "Fan → heatsink → sensor",
                "K, τ and L live here — the thing measured in section 1.",
                string.Empty),
        ];

        if (IsCascade)
        {
            TrackingVerdictTitle = "Cascade — command RPM";
            TrackingVerdictIconKind = MaterialIconKind.CheckNetworkOutline;
            TrackingVerdictBrushKey = "StatusSuccessBrush";
            TrackingVerdictBody =
                "The hot test asked for specific speeds and the EC reached them, so the outer loop outputs RPM "
                + "and lets the firmware handle the motor. Steadiest and quietest.";
            TrackingVerdictPlain = "This fan takes speed commands directly";
            return;
        }

        TrackingVerdictTitle = "Fallback — command duty";
        TrackingVerdictIconKind = MaterialIconKind.AlertDecagramOutline;
        TrackingVerdictBrushKey = "StatusWarningBrush";
        TrackingVerdictBody =
            "Tracking was poor, so the outer loop converts its RPM demand to duty through the measured "
            + "duty→RPM curve and drives duty instead. Coarser, but it never fights the firmware.";
        TrackingVerdictPlain = "This fan is driven by power level instead of exact speed";
    }

    private void BuildControlLaw(AdaptiveControlDecision? control)
    {
        HasLiveDemand = control is { IsDriven: true };
        LiveDemandText = control?.SetpointRpm is double rpm
            ? $"Right now this fan is commanded {_units.FormatFanSpeed(rpm, decimals: 0)}"
            : "This fan is not being driven by Adaptive right now";

        LawRows =
        [
            new FanExplainerLawRow(
                "Feed-forward",
                "additive term",
                MaterialIconKind.Flash,
                "BrandPrimaryBrush",
                "The anti-lag workhorse. Cools for the heat being generated right now, before the sensor has "
                + "moved at all — the only way to beat the dead time.",
                "Adapter volts × amps, GPU power, and CPU package power where the platform reports it, mapped "
                + "through the identified steady-state gain. Largest single contributor in normal use.",
                string.Empty),
            new FanExplainerLawRow(
                "PI trim",
                "additive term",
                MaterialIconKind.TuneVariant,
                "BrandSecondaryBrush",
                "Corrects everything feed-forward cannot know: ambient temperature, dust, degraded paste, "
                + "altitude, a blocked vent.",
                "Gains come from SIMC above, never hand-tuned. Anti-windup is mandatory — output clamp plus "
                + "back-calculation.",
                "Without anti-windup the integrator keeps accumulating while the fan is saturated, and the fan "
                + "sticks at maximum long after the machine has cooled."),
            new FanExplainerLawRow(
                "Lead",
                "additive term",
                MaterialIconKind.TrendingUp,
                "StatusSuccessBrush",
                "Early warning. A sharp rise means a transient is underway even while the absolute temperature "
                + "still looks harmless.",
                "Derivative on the measurement, not on the error, and low-pass filtered — so a setpoint change "
                + "does not kick the fan, and sensor noise does not become fan noise.",
                string.Empty),
            new FanExplainerLawRow(
                "Throttle escalation",
                "additive · latched",
                MaterialIconKind.LockAlertOutline,
                "StatusWarningBrush",
                "The direct answer to a thermal freeze: if the hardware says it is throttling, the model was "
                + "wrong and airflow goes up regardless.",
                "Driven by the platform's own throttle reporting. Additive on top of the model, and latched "
                + "rather than momentary.",
                "Latched means it holds until the driving temperature stays below target — surfaced in the "
                + "editor as a HELD badge with a release countdown."),
            new FanExplainerLawRow(
                "Floor",
                "guard · clamps output",
                MaterialIconKind.ShieldHalfFull,
                "TextSecondaryBrush",
                "A minimum speed that no setting, curve or control term can undercut.",
                "Defaults to the minimum spin measured in calibration. User-overridable, and off by default so "
                + "a fan that can legitimately stop is allowed to.",
                string.Empty),
            new FanExplainerLawRow(
                "Slew limit",
                "guard · rate limit",
                MaterialIconKind.SpeedometerSlow,
                "TextSecondaryBrush",
                "Caps how fast the commanded speed may change — fast up, slow down.",
                "Deliberately asymmetric. This is what makes a PI-controlled fan sound acceptable rather than "
                + "surging: it may react instantly to heat, but it must come back down gently.",
                string.Empty),
        ];
    }

    private void BuildProvenance(FanCalibrationSnapshot model)
    {
        var floor = model.MinimumSpinRpm > 0d
            ? $"{_units.FormatFanSpeed(model.MinimumSpinRpm, decimals: 0)} · {_units.FormatRatio(model.MinimumSpinDutyPercent, decimals: 0)}"
            : _units.FormatRatio(model.MinimumSpinDutyPercent, decimals: 0);

        Provenance =
        [
            new FanExplainerProvenance(
                true,
                "K — process gain",
                $"{_units.ConvertTemperatureDelta(model.ProcessGainCelsiusPerPercent):0.##} {_units.TemperatureUnitSuffix} per 1% duty",
                "Total temperature drop divided by the duty step applied in calibration. Sets how much airflow "
                + "is worth on this specific machine."),
            new FanExplainerProvenance(
                true,
                "τ — time constant",
                $"{model.TimeConstantSeconds:0} s",
                "Time to reach 63% of the change. Directly sets the integral time, so a sluggish machine gets a "
                + "patient controller automatically."),
            new FanExplainerProvenance(
                true,
                "L — dead time",
                $"{model.DeadTimeSeconds:0.#} s",
                "Delay before the sensor reacts at all. The whole reason feed-forward and lead exist, and the "
                + "lower bound on any sane λ."),
            new FanExplainerProvenance(
                true,
                "Minimum spin",
                floor,
                "Lowest duty at which the fan kept turning during calibration. Becomes the default safety floor "
                + "— below it the fan stalls."),
            new FanExplainerProvenance(
                true,
                "Tracking verdict",
                IsCascade ? "cascade" : "duty fallback",
                "Calibration checked whether the controller actually holds a commanded speed. Decides RPM output "
                + "versus duty fallback, and is stated to you in plain language above."),
            new FanExplainerProvenance(
                false,
                "Tuning rule",
                "SIMC / lambda",
                "Chosen for dead-time-dominant plants and for exposing one intuitive knob. Ziegler–Nichols is "
                + "not used — on this plant it either rings or has to be detuned into uselessness."),
            new FanExplainerProvenance(
                false,
                "λ default",
                $"{AdaptivePidTuning.DefaultLambdaSeconds:0} s",
                "Two dead times is the standard robust starting point: quick enough to catch a transient, slow "
                + "enough that the fan is not audibly hunting."),
            new FanExplainerProvenance(
                false,
                "Target temperature",
                $"{_units.FormatTemperature(TargetCelsius, decimals: 0)} · range "
                + $"{_units.FormatTemperature(AdaptiveFanSettings.MinimumTargetCelsius, decimals: 0)}–"
                + $"{_units.FormatTemperature(AdaptiveFanSettings.MaximumTargetCelsius, decimals: 0)}",
                "Your comfort knob, not a tuning parameter. The bounds keep it above ambient-limited nonsense "
                + "and below the throttle ceiling."),
            new FanExplainerProvenance(
                false,
                "Latch release",
                "held until below target",
                "Long enough that a throttling workload cannot flap the fan, short enough that the machine goes "
                + "quiet soon after the work stops."),
            new FanExplainerProvenance(
                false,
                "Slew asymmetry",
                "fast up, slow down",
                "Purely a perceived-noise decision. Symmetric rate limits measure the same and sound worse."),
        ];
    }
}
