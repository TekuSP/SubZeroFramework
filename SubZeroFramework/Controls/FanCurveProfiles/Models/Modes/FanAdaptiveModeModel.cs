using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using SubZeroFramework.Models;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;
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
    }

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
    public partial string ConfidenceIconKind { get; private set; } = "SchoolOutline";

    [ObservableProperty]
    public partial IReadOnlyList<AdaptiveKnownFact> KnownFacts { get; private set; } = [];

    /// <summary>Enabled only once there is something learned to discard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForgetLearningVisibility))]
    public partial bool HasLearnedAnything { get; private set; }

    public Visibility ForgetLearningVisibility => HasLearnedAnything ? Visibility.Visible : Visibility.Collapsed;

    public IAsyncRelayCommand ForgetLearningCommand { get; }

    // ----- Staged settings -----

    /// <summary>Target temperature, in canonical °C. The view binds its slider through the unit converter.</summary>
    [ObservableProperty]
    public partial double TargetDraftCelsius { get; set; } = AdaptiveFanSettings.DefaultTargetCelsius;

    /// <summary>λ in seconds, presented to the user as Quick ↔ Calm rather than as a number.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResponseSummaryText))]
    public partial double ResponseDraftSeconds { get; set; } = AdaptivePidTuning.DefaultLambdaSeconds;

    [ObservableProperty]
    public partial bool SafetyFloorDraftEnabled { get; set; }

    [ObservableProperty]
    public partial double SafetyFloorDraftPercent { get; set; }

    /// <summary>What the chosen response actually feels like — consequences, not control theory.</summary>
    public string ResponseSummaryText
    {
        get
        {
            var settling = AdaptivePidTuning.EstimateSettlingSeconds(ResponseDraftSeconds, DeadTimeSecondsOrDefault);
            var character = ResponseDraftSeconds switch
            {
                <= 4d => "changes speed often",
                <= 6d => "busy",
                <= 10d => "steady",
                _ => "very calm",
            };

            return $"Back on target in about {settling:0} s · {character}";
        }
    }

    /// <summary>Caption under the floor slider, bound to what was measured rather than a constant.</summary>
    [ObservableProperty]
    public partial string SafetyFloorCaption { get; private set; } = string.Empty;

    public double MinimumTargetCelsius => AdaptiveFanSettings.MinimumTargetCelsius;

    public double MaximumTargetCelsius => AdaptiveFanSettings.MaximumTargetCelsius;

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
    partial void OnTargetDraftCelsiusChanged(double value) => StageDraft();

    partial void OnResponseDraftSecondsChanged(double value) => StageDraft();

    partial void OnSafetyFloorDraftEnabledChanged(bool value) => StageDraft();

    partial void OnSafetyFloorDraftPercentChanged(double value) => StageDraft();

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

    private double DeadTimeSecondsOrDefault
        => SelectedFan?.ControlState?.Calibration is { DeadTimeSeconds: > 0d } calibration
            ? calibration.DeadTimeSeconds
            : FanCalibrationSnapshot.Bootstrap.DeadTimeSeconds;

    protected override void RefreshDerivedState()
    {
        base.RefreshDerivedState();
        RefreshAdaptiveState();
    }

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
        // Only adopt from the service while the user has nothing staged; otherwise a telemetry tick would
        // overwrite a slider mid-drag.
        if (settings is null || Page.HasStagedAdaptiveSettings)
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
            LegendEntries = [];
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
            LegendEntries = [];
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
        LegendEntries = entries;
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

        var confidence = learning.ConfidenceAt(DateTimeOffset.UtcNow);
        List<AdaptiveKnownFact> facts = [];

        switch (confidence)
        {
            case AdaptiveConfidence.Confident:
                ConfidenceIconKind = "CheckDecagram";
                ConfidenceChip = "Settled";
                ConfidenceHeadline = "Knows this fan well";
                ConfidenceBody =
                    "The model has been steady across hundreds of quiet periods. Nothing to do here — and it "
                    + "will notice on its own if the machine changes.";
                break;

            case AdaptiveConfidence.Converging:
                ConfidenceIconKind = "ChartBellCurveCumulative";
                ConfidenceChip = "Refining";
                ConfidenceHeadline = $"Learned from {learning.ObservationCount} quiet periods";
                ConfidenceBody =
                    "Adaptive has its own model of this fan now and keeps refining it. These numbers are "
                    + "already better than the defaults.";
                break;

            default:
                ConfidenceIconKind = "SchoolOutline";
                ConfidenceChip = "Learning";
                ConfidenceHeadline = "Still getting to know this fan";
                ConfidenceBody =
                    "Adaptive is running on safe defaults and watching how this machine behaves. It only "
                    + "learns from settled, quiet moments, so this takes a while — the fan is doing its job "
                    + "the whole time.";
                break;
        }

        facts.Add(new AdaptiveKnownFact("Quiet periods seen", learning.ObservationCount.ToString(System.Globalization.CultureInfo.CurrentCulture), "EyeOutline"));

        if (learning.IdentifiedProcessGainCelsiusPerPercent is double gain)
        {
            facts.Add(new AdaptiveKnownFact(
                "Cooling per 1% fan",
                _unitFormattingService.FormatTemperature(gain, decimals: 2),
                "SnowflakeThermometer"));
        }
        else
        {
            facts.Add(new AdaptiveKnownFact("Running on", "Safe defaults", "ShieldHalfFull"));
        }

        if (state?.Calibration is { MinimumSpinRpm: > 0d } calibrated)
        {
            facts.Add(new AdaptiveKnownFact(
                "Stalls below",
                _unitFormattingService.FormatFanSpeed(calibrated.MinimumSpinRpm, decimals: 0),
                "FanMinus"));
        }

        KnownFacts = facts;
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
public sealed record AdaptiveKnownFact(string Label, string Value, string IconKind);
