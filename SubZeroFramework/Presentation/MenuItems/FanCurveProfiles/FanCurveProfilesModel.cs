using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using FrameworkDotnet.Enums;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

using Microsoft.UI.Dispatching;
using Microsoft.UI;

using SkiaSharp;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Orchestrates the Fan Curve Profiles page. Reuses the dashboard <see cref="FanCardModel"/>
/// so each tile shows the same gauge + history chart, extended with a red driving-temperature
/// line, and toggles per-fan selection state to highlight the active card.
/// </summary>
public partial class FanCurveProfilesModel : ObservableObject, IDisposable
{
    private const double DefaultManualDutyPercent = 50d;
    private static readonly TimeSpan ManualDutyDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly IFanCapabilityClient _fanCapabilityClient;
    private readonly IFanControlStateClient _fanControlStateClient;
    private readonly IFanStateClient _fanStateClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IFrameworkFanControlClient _fanControlClient;
    private readonly IFanControlActuator _actuator;
    private readonly IFanHistoryStore _historyStore;
    private readonly FanTelemetryHub _hub;
    private readonly IUserUnitPreferencesClient _userUnitPreferencesClient;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<FanCurveProfilesModel> _logger;

    private readonly CompositeDisposable _subscriptions = new();
    private readonly Subject<double> _manualDutyChanges = new();
    // Coalesces temperature-history recomputes; the cross-sensor aggregation is O(points^2), so it must
    // not run on every poll sample (see RefreshTemperatureHistoryDisplays).
    private readonly Subject<int> _temperatureHistoryChanged = new();
    // Editable custom-curve points (persistent so edits survive mode switches). Owns its own change handlers;
    // raises Changed → RefreshCurveSeries. LoadDefaultDraft/LoadDraftFrom seed it before the editor is shown.
    private readonly FanCurveDraftModel _draft = new();

    // Per-fan curve profile slots (the page name's "profiles"). One editable draft at a time, targeting
    // the active slot; up to MaxCurveProfileSlots unique slots per fan.
    private const int MaxCurveProfileSlots = 5;
    private readonly ObservableCollection<FollowOption> _followOptions = [];

    // The custom-curve draft staging state for the selected fan: applied baseline, pending/tested snapshots,
    // the slot being edited, the staged simple mode + duty, and the load/seed guards (see FanEditSession).
    private readonly FanEditSession _session;

    public FanCurveProfilesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFanCapabilityClient fanCapabilityClient,
        IFanControlStateClient fanControlStateClient,
        IFanStateClient fanStateClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IFrameworkFanControlClient fanControlClient,
        IFanControlActuator actuator,
        IFanHistoryStore historyStore,
        FanTelemetryHub hub,
        FanCoordinatorAccessor coordinatorAccessor,
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        SynchronizationContext synchronizationContext,
        DispatcherQueue dispatcherQueue,
        ILogger<FanCurveProfilesModel> logger)
    {
        _frameworkStatusClient = frameworkStatusClient;
        _fanCapabilityClient = fanCapabilityClient;
        _fanControlStateClient = fanControlStateClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _fanControlClient = fanControlClient;
        _actuator = actuator;
        _historyStore = historyStore;
        _hub = hub;
        // Publish this (the page-driven) coordinator so the navigation-resolved mode body VMs bind to THIS
        // instance. Uno's nested-region navigation otherwise hands them a separate DI-resolved coordinator whose
        // SelectedFan is never set, blanking the mode gauges. The displayed page is always constructed before the
        // mode region navigates, so Current is set in time.
        coordinatorAccessor.Current = this;
        _userUnitPreferencesClient = userUnitPreferencesClient;
        _unitFormattingService = unitFormattingService;
        _synchronizationContext = synchronizationContext;
        _dispatcherQueue = dispatcherQueue;
        _logger = logger;

        SensorChart = new FanSensorChartModel(unitFormattingService);
        CurveChart = new FanCurveChartModel(unitFormattingService);
        LinkSection = new FanLinkSectionModel(this);
        BoostSection = new FanBoostSectionModel(this, unitFormattingService);
        SensorSelection = new FanSensorSelectionModel(historyStore, unitFormattingService);
        _session = new FanEditSession(this, actuator, logger);

        _hub.FanAdded += OnFanAdded;
        _hub.FanRemoved += OnFanRemoved;
        SensorSelection.SelectionChanged += RefreshSensorChart;
        SensorSelection.SensorRemoved += OnSensorRemoved;
        SensorSelection.SensorRenamed += SensorChart.UpdateSensorName;

        FollowOptions = new ReadOnlyObservableCollection<FollowOption>(_followOptions);

        _draft.Changed += RefreshCurveSeries;
        RefreshCurveSeries();

        _frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(status => LastStatus = status)
            .DisposeWith(_subscriptions);

        _fanCapabilityClient
            .WatchFanCapabilities()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyCapabilityChanges)
            .DisposeWith(_subscriptions);

        _fanControlStateClient
            .WatchFanControlStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyControlStateChanges)
            .DisposeWith(_subscriptions);

        _fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyFanStateChanges)
            .DisposeWith(_subscriptions);

        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyFanTelemetryChanges)
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyTemperatureChanges)
            .DisposeWith(_subscriptions);

        _userUnitPreferencesClient
            .WatchPreferences()
            .ObserveOn(_synchronizationContext)
            .Subscribe(_ => RefreshUnitFormatting())
            .DisposeWith(_subscriptions);

        _manualDutyChanges
            .Throttle(ManualDutyDebounce)
            .DistinctUntilChanged()
            .ObserveOn(_synchronizationContext)
            .Select(duty => Observable.FromAsync(ct => ApplyDebouncedManualDutyAsync(duty, ct)))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        _manualDutyChanges.DisposeWith(_subscriptions);

        // Sample (not throttle) so continuous polling still yields ~3 refreshes/second without the
        // per-sample O(points^2) recompute that caused chart lag and high CPU.
        _temperatureHistoryChanged
            .Sample(TimeSpan.FromMilliseconds(300))
            .ObserveOn(_synchronizationContext)
            .Subscribe(_ => RefreshTemperatureHistoryDisplays())
            .DisposeWith(_subscriptions);

        _temperatureHistoryChanged.DisposeWith(_subscriptions);

        // The history store owns the per-fan/sensor history subscriptions; we re-render on its change events.
        // Fan history renders immediately; temperature history is funnelled through the sampled subject above.
        _historyStore.FanHistoryChanged += RefreshFanHistory;
        _historyStore.TemperatureHistoryChanged += OnTemperatureHistoryChanged;
    }

    private void OnTemperatureHistoryChanged(int sensorIndex) => _temperatureHistoryChanged.OnNext(sensorIndex);

    public ReadOnlyObservableCollection<FanCardModel> Fans => _hub.Fans;

    public ReadOnlyObservableCollection<SensorChipModel> AvailableSensors => SensorSelection.AvailableSensors;

    /// <summary>The selectable driving-temperature sensor chips for the custom-curve editor.</summary>
    public FanSensorSelectionModel SensorSelection { get; }

    /// <summary>Driving-temperature chart for the custom-curve body (series, axis, legend). Owns its own state.</summary>
    public FanSensorChartModel SensorChart { get; }

    public ReadOnlyObservableCollection<CurvePointModel> CurvePoints => _draft.CurvePoints;

    /// <summary>Custom-curve editor chart: draft + applied-overlay series, theme paints, unit-aware axis labels,
    /// the predicted-duty readout, and the live driving-temperature marker.</summary>
    public FanCurveChartModel CurveChart { get; }

    /// <summary>Driving-source options for the selected slot: this fan's own curve, or follow another fan.</summary>
    public ReadOnlyObservableCollection<FollowOption> FollowOptions { get; }

    /// <summary>The "Applies to" link group (chips + per-leader link sets). Driven by this coordinator.</summary>
    public FanLinkSectionModel LinkSection { get; }

    public FanBoostSectionModel BoostSection { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFollowing))]
    [NotifyPropertyChangedFor(nameof(CurveEditorVisibility))]
    [NotifyPropertyChangedFor(nameof(FollowNoteVisibility))]
    [NotifyPropertyChangedFor(nameof(FollowNoteText))]
    public partial FollowOption? SelectedFollowOption { get; set; }

    partial void OnSelectedFollowOptionChanged(FollowOption? value)
    {
        if (_session.IsLoadingDraft)
        {
            return;
        }

        // Switching between "own curve" and a follow target changes validity, dirty state, and which
        // editor surface is shown.
        RefreshSensorChart();
        RecomputeDirty();
        RefreshPredictedDuty();
    }

    public bool IsFollowing => SelectedFollowOption?.FanIndex is not null;

    public Microsoft.UI.Xaml.Visibility CurveEditorVisibility =>
        IsFollowing ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility FollowNoteVisibility =>
        IsFollowing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string FollowNoteText => SelectedFollowOption?.FanIndex is int leader
        ? $"This profile follows Fan {leader}. It mirrors Fan {leader}'s active curve in real time — edit that curve on Fan {leader}."
        : string.Empty;

    public string ActiveProfileText => SelectedFan?.ControlState is { Mode: FanControlMode.CustomCurve } state
        ? $"Profile {state.ActiveCurveSlot + 1} is currently driving this fan."
        : "No curve profile is currently driving this fan.";

    /// <summary>True when the draft curve differs from the curve the service currently has applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnsavedChangesVisibility))]
    [NotifyPropertyChangedFor(nameof(StagedFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(CleanFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarStagedVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarEditingVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarCleanVisibility))]
    public partial bool IsDirty { get; set; }

    // ===== Master footer + detail action bar (staging chrome) =====
    // Every mode change is staged, not applied immediately: Apply commits, Revert discards, Preview tries it
    // live (volatile). The custom-curve draft stages via IsDirty; Auto/Manual/Max stage via _session.StagedMode.
    private bool CurrentFanHasStagedEdits =>
        IsDirty && (ShowCustomEditor || SelectedFanMode == FanControlMode.CustomCurve);

    /// <summary>The mode the service currently has applied to the selected fan (ignores any staged overlay).</summary>
    internal FanControlMode ServiceFanMode => SelectedFan?.ControlState?.Mode ?? FanControlMode.Auto;

    /// <summary>True while the custom editor / curve mode is the active staging surface.</summary>
    private bool IsCustomStaging => ShowCustomEditor || SelectedFanMode == FanControlMode.CustomCurve;

    /// <summary>
    /// True when Custom curve is the staging surface but the service is not driving this fan with a custom
    /// curve yet: switching into Custom curve is itself a staged mode change, so the pending pill and
    /// Preview/Revert light up immediately — the user should not have to move a curve point first.
    /// </summary>
    private bool IsCustomActivationStaged => IsCustomStaging && ServiceFanMode != FanControlMode.CustomCurve;

    /// <summary>True when a simple mode (Auto/Manual/Max) is staged but not yet applied (differs from live).</summary>
    private bool HasStagedSimpleMode
    {
        get
        {
            if (_session.StagedMode is not { } staged)
            {
                return false;
            }

            if (staged != ServiceFanMode)
            {
                return true;
            }

            // Same mode as live: only Manual can still differ, by its duty target.
            if (staged != FanControlMode.Manual)
            {
                return false;
            }

            var liveDuty = SelectedFan?.ControlState?.LastDutyPercent ?? DefaultManualDutyPercent;
            return Math.Abs(_session.StagedManualDuty - liveDuty) > 0.5d;
        }
    }

    // Pending = unsaved custom edits, a staged Custom-curve activation, a staged simple mode, OR a live
    // preview in progress.
    private bool HasPendingFanWork => CurrentFanHasStagedEdits || IsCustomActivationStaged || HasStagedSimpleMode || LinkSection.HasStagedLinks || BoostSection.HasStagedBoosts || IsTesting;

    // Staged but not yet previewing — the action bar shows Discard + Preview (covers custom + simple modes + links + boost).
    private bool HasStagedNotPreviewing => (CurrentFanHasStagedEdits || IsCustomActivationStaged || HasStagedSimpleMode || LinkSection.HasStagedLinks || BoostSection.HasStagedBoosts) && !IsTesting;

    public Microsoft.UI.Xaml.Visibility StagedFooterVisibility =>
        HasPendingFanWork ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility CleanFooterVisibility =>
        HasPendingFanWork ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public string StagedSummaryText => IsTesting ? "Previewing live on this fan" : "1 fan has unsaved changes";

    // Mirror the staged state onto the selected fan card so its row shows the "Changes pending" pill.
    partial void OnIsDirtyChanged(bool value)
    {
        RefreshStagedPill();
        NotifyCommandStates();
    }

    private void RefreshStagedPill()
    {
        if (SelectedFan is { } fan)
        {
            fan.IsStaged = HasPendingFanWork;
        }
    }

    public Microsoft.UI.Xaml.Visibility ActionBarStagedVisibility =>
        HasPendingFanWork ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // Staged but not yet previewing → action bar shows "Unsaved changes…" + Discard + Preview.
    public Microsoft.UI.Xaml.Visibility ActionBarEditingVisibility =>
        HasStagedNotPreviewing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility ActionBarCleanVisibility =>
        !HasPendingFanWork && SelectedFan is not null && !IsSelectedFanStalled
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>True when the selected fan has an applied custom curve to show as a reference overlay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppliedOverlayVisibility))]
    public partial bool HasAppliedCurveOverlay { get; set; }

    /// <summary>True when the applied curve changed elsewhere while the user has unsaved edits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExternalChangeVisibility))]
    public partial bool AppliedCurveChangedExternally { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PredictedDutyVisibility))]
    public partial bool HasPredictedDuty { get; set; }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Mode selection + staging commands (Preview / Apply / Test / Revert), custom-curve slot loading.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    public FanControlMode SelectedMode
    {
        get => SelectedFanMode;
        set
        {
            if (SelectedFan is null)
            {
                return;
            }

            // Defense in depth: the mode toggles write through this setter (bypassing command
            // CanExecute), so block mutating mode changes when fan control is not authorized.
            if (!CanIssueFanCommands)
            {
                return;
            }

            if (value == FanControlMode.CustomCurve)
            {
                if (!ShowCustomEditor && CanSelectMode)
                {
                    EnterCustomEditor();
                }

                return;
            }

            if (ShowCustomEditor)
            {
                ShowCustomEditor = false;
            }

            // Stage the chosen mode instead of actuating the EC. It is committed by Apply (persist) or tried
            // live by Preview (volatile); Revert discards it. This is the uniform Stage → Preview → Apply flow.
            _session.StageSimpleMode(value);
        }
    }

    // Re-projects the mode-derived UI after the edit session stages or clears a simple mode (Stage / Clear).
    internal void NotifyStagingChanged()
    {
        ControlStateRevision++;
        RecomputeDirty();
        RefreshStagedPill();
    }

    /// <summary>Mode selection as a segmented-control index (0=Auto, 1=Manual, 2=Custom curve, 3=Max).</summary>
    public int SelectedModeIndex
    {
        get => SelectedFanMode switch
        {
            FanControlMode.Manual => 1,
            FanControlMode.CustomCurve => 2,
            FanControlMode.Max => 3,
            _ => 0,
        };
        set => SelectedMode = value switch
        {
            1 => FanControlMode.Manual,
            2 => FanControlMode.CustomCurve,
            3 => FanControlMode.Max,
            _ => FanControlMode.Auto,
        };
    }

    [RelayCommand]
    public void SelectFan(FanCardModel? fan)
    {
        if (fan is null || ReferenceEquals(fan, SelectedFan))
        {
            return;
        }

        SelectedFan = fan;
    }

    partial void OnSelectedFanChanged(FanCardModel? oldValue, FanCardModel? newValue)
    {
        // A linked partner is controlled by its leader — never edit it directly. Redirect selection to the leader,
        // using the *effective* leader (staged override or persisted) so a just-linked partner is non-editable even
        // before Apply. The re-entrant set settles because the leader is not itself a partner.
        if (newValue is { } candidate
            && LinkSection.EffectiveLeaderOf(candidate) is int leaderIndex
            && _hub.GetFan(leaderIndex) is { } leaderFan
            && !ReferenceEquals(leaderFan, candidate))
        {
            SelectedFan = leaderFan;
            return;
        }

        foreach (var fan in _hub.Fans)
        {
            fan.IsSelected = ReferenceEquals(fan, newValue);
        }

        ShowCustomEditor = false;

        // Editor draft/preview/test state belongs to the previously selected fan; reset it.
        _session.AppliedBaseline = null;
        _session.PreTestState = null;
        _session.StagedMode = null;
        _session.StagedManualDuty = DefaultManualDutyPercent;
        IsTesting = false;
        AppliedCurveChangedExternally = false;
        HasAppliedCurveOverlay = CurveChart.SetAppliedOverlay(null);
        // Switching fans discards the previous fan's in-editor draft, so clear its pending pill.
        if (oldValue is { } previous)
        {
            previous.IsStaged = false;
        }

        _session.SelectedSlot = 0;
        UpdateSelectedFanStalled();
        RebuildFollowOptions();
        LinkSection.RebuildLinkChips();
        BoostSection.RefreshFromSelection();
        RecomputeDirty();
        RefreshPredictedDuty();
    }

    private void UpdateSelectedFanStalled() =>
        IsSelectedFanStalled = SelectedFan?.FanState?.FanState == FrameworkFanState.Stalled;

    /// <summary>Re-evaluates the stalled lockdown after the user taps "Re-check fan"; live polling clears it when the fan spins up.</summary>
    [RelayCommand]
    private void RecheckFan() => UpdateSelectedFanStalled();

    [RelayCommand(CanExecute = nameof(CanSelectMode))]
    private void EnterCustomEditor()
    {
        // The custom editor stages via its own curve draft; drop any staged simple mode so it doesn't
        // linger as a phantom pending change while the editor is open.
        _session.StagedMode = null;

        // Open the editor on the slot the service is currently running (or slot 0 if none).
        RebuildFollowOptions();
        _session.SelectedSlot = SelectedFan?.ControlState is { Mode: FanControlMode.CustomCurve } state
            ? Math.Clamp(state.ActiveCurveSlot, 0, MaxCurveProfileSlots - 1)
            : 0;

        ShowCustomEditor = true;
        LoadSelectedSlot();

        // Entering the editor on a fan the service is not curve-driving stages the activation itself, even
        // with a clean draft — surface the pending pill and enable Preview/Revert right away.
        RefreshStagedPill();
        NotifyCommandStates();
    }

    private bool CanCancelCustomEditor() => ShowCustomEditor;

    [RelayCommand(CanExecute = nameof(CanCancelCustomEditor))]
    private void CancelCustomEditor()
    {
        if (_session.PendingSnapshot is { } snapshot)
        {
            SelectedAggregation = snapshot.Aggregation;

            SensorSelection.SetSelected(snapshot.SensorIndices);

            _draft.Load(snapshot.CurvePoints);

            RefreshSensorChart();
            RefreshCurveSeries();
        }

        _session.PendingSnapshot = null;
        ShowCustomEditor = false;
        AppliedCurveChangedExternally = false;
        RefreshAppliedOverlay();
        RecomputeDirty();
        RefreshPredictedDuty();

        // Closing the editor clears a staged Custom-curve activation (RecomputeDirty only notifies when the
        // draft's dirty flag actually changed, which a clean activation never touches).
        RefreshStagedPill();
        NotifyCommandStates();
    }

    // Apply (master footer "Apply all" / commit): commits the staged change for the selected fan. Routes to
    // the simple-mode commit when not in the custom editor; otherwise persists the custom curve below.
    [RelayCommand(CanExecute = nameof(CanApplyStaged))]
    private async Task ApplyCustomCurveAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;
        if (!CanIssueFanCommands) { ReportFanControlBlocked(); return; }

        if (!IsCustomStaging)
        {
            await _session.ApplySimpleModeAsync(cancellationToken).ConfigureAwait(true);
            // Persist any staged "Applies to" link changes (the link is grouping-only when no mode is staged).
            await LinkSection.FlushStagedLinksAsync(cancellationToken).ConfigureAwait(true);
            // Boost flushes after the commit above so no preview hold is open (the service rejects otherwise).
            await BoostSection.FlushStagedBoostsAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        var slot = _session.SelectedSlot;
        var followFanIndex = SelectedFollowOption?.FanIndex;
        var aggregation = SelectedAggregation ?? TemperatureAggregationMode.Maximum;
        var dictionary = new Dictionary<int, double>(_draft.CurvePoints.Count);
        int[] selectedSensors = [];

        if (followFanIndex is null)
        {
            if (_draft.CurvePoints.Count < 2)
            {
                ReportStatus("Custom curve needs at least two points.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                return;
            }

            selectedSensors = SensorSelection.SelectedIndices();

            if (selectedSensors.Length == 0)
            {
                ReportStatus("Select at least one driving temperature sensor before applying a custom curve.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                return;
            }

            foreach (var point in _draft.CurvePoints)
            {
                dictionary[point.TemperatureCelsius] = Math.Clamp(point.DutyPercent, 0d, 100d);
            }
        }

        try
        {
            var name = SelectedFan?.ControlState?.CurveProfiles.ElementAtOrDefault(slot)?.Name;
            var result = await _fanControlClient
                .SaveCurveProfileAsync(fan.Snapshot.FanIndex, slot, name, dictionary, selectedSensors, aggregation, followFanIndex, activate: true, cancellationToken)
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                // Commit ends any live preview: the draft is now the active slot's saved profile. Update the
                // baseline locally so dirty clears immediately; the control-state stream confirms it shortly.
                _session.PreTestState = null;
                _session.TestedSnapshot = null;
                IsTesting = false;
                IsTestDraftChanged = false;
                _session.AppliedBaseline = CurrentDraftSnapshot();
                _session.PendingSnapshot = _session.AppliedBaseline;
                AppliedCurveChangedExternally = false;
                RefreshAppliedOverlay();
                RecomputeDirty();

                // "Applies to" fan-out: when this fan owns the curve (not itself following), linked partners
                // mirror it live by following this fan — they share the curve and apply together.
                var linkedCount = followFanIndex is null
                    ? await ApplyLinkedPartnersAsync(fan.Snapshot.FanIndex, slot, cancellationToken).ConfigureAwait(true)
                    : 0;

                ReportStatus(
                    followFanIndex is int leader
                        ? $"Profile {slot + 1} now follows Fan {leader} and is driving {fan.Snapshot.DisplayName}."
                        : linkedCount > 0
                            ? $"Profile {slot + 1} applied to {fan.Snapshot.DisplayName} and {linkedCount} linked fan(s) ({dictionary.Count} points, {selectedSensors.Length} sensor(s))."
                            : $"Profile {slot + 1} applied to {fan.Snapshot.DisplayName} ({dictionary.Count} points, {selectedSensors.Length} sensor(s)).",
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
            }
            else
            {
                ReportStatus($"Service rejected the profile: {result.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply curve profile {Slot} for fan {FanIndex}", slot, fan.Snapshot.FanIndex);
            ReportStatus($"Failed to apply profile: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }

        // Persist any staged "Applies to" link changes alongside the applied curve.
        await LinkSection.FlushStagedLinksAsync(cancellationToken).ConfigureAwait(true);
        // Boost flushes after the profile commit so no preview hold is open (the service rejects otherwise).
        await BoostSection.FlushStagedBoostsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Pushes a "follow this fan" profile to every linked partner so they mirror the leader's curve live.
    /// Stalled partners are skipped (they cannot accept a curve). Returns the number of partners updated.
    /// </summary>
    private async Task<int> ApplyLinkedPartnersAsync(int leader, int slot, CancellationToken cancellationToken)
    {
        var partners = LinkSection.GetLinkedPartners(leader)
            .Where(index => index != leader && _hub.GetFan(index) is not null)
            .ToArray();

        var applied = 0;
        foreach (var partnerIndex in partners)
        {
            if (_hub.GetFan(partnerIndex) is { } partnerFan
                && partnerFan.FanState?.FanState == FrameworkFanState.Stalled)
            {
                continue;
            }

            try
            {
                var result = await _fanControlClient
                    .SaveCurveProfileAsync(partnerIndex, slot, name: null, new Dictionary<int, double>(), [], TemperatureAggregationMode.Maximum, leader, activate: true, cancellationToken)
                    .ConfigureAwait(true);

                if (result.Succeeded)
                {
                    applied++;
                }
                else
                {
                    _logger.LogWarning("Service rejected linking fan {Partner} to leader {Leader}: {Message}", partnerIndex, leader, result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to link fan {Partner} to leader {Leader}", partnerIndex, leader);
            }
        }

        return applied;
    }

    [RelayCommand(CanExecute = nameof(CanClearProfile))]
    private async Task ClearProfileAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;
        if (!CanIssueFanCommands) { ReportFanControlBlocked(); return; }

        var slot = _session.SelectedSlot;
        try
        {
            await _fanControlClient.ClearCurveProfileAsync(fan.Snapshot.FanIndex, slot, cancellationToken).ConfigureAwait(true);
            ReportStatus($"Cleared Profile {slot + 1} on {fan.Snapshot.DisplayName}.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
            LoadSelectedSlot();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear curve profile {Slot} for fan {FanIndex}", slot, fan.Snapshot.FanIndex);
            ReportStatus($"Failed to clear profile: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    // Revert (master footer "Revert all" / detail "Discard"/"Stop preview"): discards the staged change.
    [RelayCommand(CanExecute = nameof(CanRevertStaged))]
    private async Task RevertToAppliedAsync(CancellationToken cancellationToken)
    {
        // Drop any pending "Applies to" link and CPU boost changes (they were never saved to the service).
        LinkSection.DiscardStagedLinks();
        BoostSection.DiscardStagedBoosts();

        // While previewing, Revert stops the live test and restores the fan's prior state.
        if (IsTesting)
        {
            await DiscardTestAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        // A staged simple mode just clears (the EC was never touched).
        if (!IsCustomStaging)
        {
            _session.ClearStagedSimpleMode();
            ReportStatus("Discarded the staged change.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
            return;
        }

        // Reverting a staged Custom-curve activation (the fan is not curve-driven yet) exits the editor back
        // to the applied mode's view — the pending change IS the activation, so reloading the draft alone
        // would leave it staged forever.
        if (IsCustomActivationStaged)
        {
            CancelCustomEditor();
            ReportStatus("Discarded the staged change.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
            return;
        }

        // Otherwise reload the selected slot's persisted state, discarding the in-progress curve edits.
        LoadSelectedSlot();
    }

    // Preview (master footer "Preview all" / detail "Preview"): tries the staged change live (volatile).
    [RelayCommand(CanExecute = nameof(CanPreviewStaged))]
    private async Task TestOnFanAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;
        if (!CanIssueFanCommands) { ReportFanControlBlocked(); return; }

        if (!IsCustomStaging)
        {
            await _session.PreviewSimpleModeAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        var selectedSensors = SensorSelection.SelectedIndices();

        if (_draft.CurvePoints.Count < 2 || selectedSensors.Length == 0)
        {
            ReportStatus("Add at least two points and one driving sensor before testing.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        var dictionary = new Dictionary<int, double>(_draft.CurvePoints.Count);
        foreach (var point in _draft.CurvePoints)
        {
            dictionary[point.TemperatureCelsius] = Math.Clamp(point.DutyPercent, 0d, 100d);
        }

        // Remember the prior state and flip the testing flag before the call so the resulting
        // control-state stream update is recognized as our own test, not an external change.
        var group = ActuationGroup(fan.Snapshot.FanIndex);
        _session.PreTestState = fan.ControlState;
        // Open the safety hold on the whole linked group so the service reverts them all if this client disconnects.
        await _actuator.OpenPreviewHoldsAsync(group).ConfigureAwait(true);
        IsTesting = true;

        try
        {
            var aggregation = SelectedAggregation ?? TemperatureAggregationMode.Maximum;
            var result = await _actuator.ActuateCurveAsync(fan.Snapshot.FanIndex, dictionary, selectedSensors, aggregation, preview: true, cancellationToken).ConfigureAwait(true);
            if (result.Succeeded)
            {
                // Linked partners preview the same curve live (volatile) so the whole group runs it together.
                foreach (var partner in group)
                {
                    if (partner == fan.Snapshot.FanIndex) continue;
                    await _actuator.ActuateCurveAsync(partner, dictionary, selectedSensors, aggregation, preview: true, cancellationToken).ConfigureAwait(true);
                }

                _session.TestedSnapshot = CurrentDraftSnapshot();
                IsTestDraftChanged = false;
                var scope = group.Count > 1 ? $"{fan.Snapshot.DisplayName} + {group.Count - 1} linked fan(s)" : fan.Snapshot.DisplayName;
                ReportStatus($"Previewing curve on {scope}. Apply to keep it, or Revert to restore the previous state.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
            else
            {
                IsTesting = false;
                _session.PreTestState = null;
                ReportStatus($"Service rejected the test curve: {result.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            IsTesting = false;
            _session.PreTestState = null;
            _logger.LogWarning(ex, "Failed to test custom curve for fan {FanIndex}", fan.Snapshot.FanIndex);
            ReportStatus($"Failed to test curve: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    private bool CanRetest => IsTesting && IsTestDraftChanged && !IsFollowing && HasValidDraft && CanIssueFanCommands;

    // Re-applies the edited draft to the fan during an active test (without disturbing the captured
    // pre-test state, so Discard still restores the original).
    [RelayCommand(CanExecute = nameof(CanRetest))]
    private async Task RetestAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null || !IsTesting) return;
        if (!CanIssueFanCommands) { ReportFanControlBlocked(); return; }

        var selectedSensors = SensorSelection.SelectedIndices();

        if (_draft.CurvePoints.Count < 2 || selectedSensors.Length == 0)
        {
            ReportStatus("Add at least two points and one driving sensor before re-testing.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        var dictionary = new Dictionary<int, double>(_draft.CurvePoints.Count);
        foreach (var point in _draft.CurvePoints)
        {
            dictionary[point.TemperatureCelsius] = Math.Clamp(point.DutyPercent, 0d, 100d);
        }

        try
        {
            var aggregation = SelectedAggregation ?? TemperatureAggregationMode.Maximum;
            var result = await _actuator.ActuateCurveAsync(fan.Snapshot.FanIndex, dictionary, selectedSensors, aggregation, preview: true, cancellationToken).ConfigureAwait(true);
            if (result.Succeeded)
            {
                // Keep the linked group in sync with the edited curve (still volatile — the holds remain open).
                foreach (var partner in ActuationGroup(fan.Snapshot.FanIndex))
                {
                    if (partner == fan.Snapshot.FanIndex) continue;
                    await _actuator.ActuateCurveAsync(partner, dictionary, selectedSensors, aggregation, preview: true, cancellationToken).ConfigureAwait(true);
                }

                _session.TestedSnapshot = CurrentDraftSnapshot();
                IsTestDraftChanged = false;
                NotifyCommandStates();
                ReportStatus($"Updated the test on {fan.Snapshot.DisplayName} with your latest changes.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
            else
            {
                ReportStatus($"Service rejected the updated test curve: {result.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-test custom curve for fan {FanIndex}", fan.Snapshot.FanIndex);
            ReportStatus($"Failed to update test: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanResolveTest))]
    private void KeepTest()
    {
        // The tested curve is already applied; adopt it as the new baseline and stop tracking the revert state.
        _session.PreTestState = null;
        _session.TestedSnapshot = null;
        IsTesting = false;
        IsTestDraftChanged = false;
        _session.AppliedBaseline = CurrentDraftSnapshot();
        AppliedCurveChangedExternally = false;
        RefreshAppliedOverlay();
        RecomputeDirty();

        var fan = SelectedFan;
        ReportStatus(
            fan is null ? "Tested curve kept." : $"Tested curve kept on {fan.Snapshot.DisplayName}.",
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanResolveTest))]
    private async Task DiscardTestAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null)
        {
            IsTesting = false;
            _session.PreTestState = null;
            return;
        }

        var fanIndex = fan.Snapshot.FanIndex;
        var previous = _session.PreTestState;

        try
        {
            // Restore the captured pre-test state by re-actuating it persistently (preview:false).
            if (previous is { Mode: FanControlMode.CustomCurve, CustomCurvePoints.Count: > 0 })
            {
                var restored = new Dictionary<int, double>(previous.CustomCurvePoints.Count);
                foreach (var pair in previous.CustomCurvePoints)
                {
                    restored[pair.Key] = pair.Value;
                }
                await _actuator.ActuateCurveAsync(fanIndex, restored, [.. previous.DrivingSensorIndices], previous.DrivingTemperatureAggregation, preview: false, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                var restoreMode = previous?.Mode is FanControlMode.Manual or FanControlMode.Max ? previous.Mode : FanControlMode.Auto;
                await _actuator.ActuateSimpleAsync(fanIndex, restoreMode, previous?.LastDutyPercent ?? DefaultManualDutyPercent, preview: false, cancellationToken).ConfigureAwait(true);
            }

            ReportStatus($"Reverted {fan.Snapshot.DisplayName} to its previous state.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revert fan {FanIndex} after test", fanIndex);
            ReportStatus($"Failed to revert after test: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            IsTesting = false;
            _session.PreTestState = null;
            _session.TestedSnapshot = null;
            IsTestDraftChanged = false;
            // A simple-mode preview also clears its staged overlay (the restore above returned the live state).
            _session.StagedMode = null;
            ControlStateRevision++;
            RecomputeDirty();
        }
    }

    /// <summary>Baseline for the slot currently being edited (its persisted state), or null if empty.</summary>
    private CustomCurveSnapshot? BuildAppliedBaseline() => BuildSlotBaseline(_session.SelectedSlot);

    private CustomCurveSnapshot? BuildSlotBaseline(int slot)
    {
        var profile = SelectedFan?.ControlState?.CurveProfiles.ElementAtOrDefault(slot);
        if (profile is null || !profile.IsConfigured)
        {
            return null;
        }

        return new CustomCurveSnapshot(
            profile.DrivingTemperatureAggregation,
            [.. profile.DrivingSensorIndices],
            [.. profile.CurvePoints.Select(static pair => (pair.Key, pair.Value))],
            profile.FollowFanIndex);
    }

    /// <summary>
    /// The fans an actuation should reach: the leader plus its non-stalled linked partners. Preview / Apply /
    /// Revert all act on this whole set so linked fans move together. Always includes the leader.
    /// </summary>
    internal IReadOnlyList<int> ActuationGroup(int leader) =>
        LinkSection.GetLinkedPartners(leader)
            .Where(index => index == leader
                || (_hub.GetFan(index) is { } partner && partner.FanState?.FanState != FrameworkFanState.Stalled))
            .Distinct()
            .OrderBy(static index => index)
            .ToArray();

    /// <summary>
    /// Persists one fan's "Applies to" link to the service (the source of truth, so it survives restart). Called
    /// from <see cref="FanLinkSectionModel.FlushStagedLinksAsync"/> on Apply; the change streams back as the fan's
    /// LinkedLeaderIndex.
    /// </summary>
    internal async Task PersistFanLinkAsync(int fanIndex, int? leaderIndex, CancellationToken cancellationToken = default)
    {
        if (!CanIssueFanCommands)
        {
            return;
        }

        try
        {
            await _fanControlClient.SetFanLinkAsync(fanIndex, leaderIndex, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist fan link for fan {FanIndex}", fanIndex);
        }
    }

    /// <summary>The link section staged or discarded a pending link change — refresh the staged pill + command states.</summary>
    internal void OnStagedLinksChanged()
    {
        RefreshStagedPill();
        NotifyCommandStates();
    }

    /// <summary>
    /// Persists one fan's CPU boost (usage modifier) to the service; null clears it. Called from
    /// <see cref="FanBoostSectionModel.FlushStagedBoostsAsync"/> on Apply — after the staged mode/curve has
    /// committed, so no preview hold is open (the service rejects modifier writes during a live preview).
    /// The change streams back as the fan's control-state CpuUsageModifierStrength.
    /// </summary>
    internal async Task PersistFanBoostAsync(int fanIndex, double? strength, CancellationToken cancellationToken = default)
    {
        if (!CanIssueFanCommands)
        {
            return;
        }

        try
        {
            var result = await _fanControlClient.SetUsageModifierAsync(fanIndex, strength, cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                ReportStatus($"Service rejected the CPU boost change: {result.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist CPU boost for fan {FanIndex}", fanIndex);
            ReportStatus($"Failed to save the CPU boost: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    /// <summary>The boost section staged or discarded a pending boost change — refresh the staged pill + command states.</summary>
    internal void OnStagedBoostsChanged()
    {
        RefreshStagedPill();
        NotifyCommandStates();
    }

    private FollowOption? FindFollowOption(int? fanIndex)
        => _followOptions.FirstOrDefault(option => option.FanIndex == fanIndex)
            ?? _followOptions.FirstOrDefault(static option => option.FanIndex is null);

    private void RebuildFollowOptions()
    {
        var currentTarget = SelectedFollowOption?.FanIndex;

        _followOptions.Clear();
        _followOptions.Add(new FollowOption(null, "This fan's own curve"));
        foreach (var fan in _hub.Fans)
        {
            var index = fan.Snapshot.FanIndex;
            if (SelectedFan is null || index != SelectedFan.Snapshot.FanIndex)
            {
                _followOptions.Add(new FollowOption(index, $"Follow Fan {index}"));
            }
        }

        _session.IsLoadingDraft = true;
        try { SelectedFollowOption = FindFollowOption(currentTarget); }
        finally { _session.IsLoadingDraft = false; }
    }

    // Loads the selected slot's persisted state into the editable draft (or sensible defaults if empty).
    private void LoadSelectedSlot()
    {
        _session.AppliedBaseline = BuildSlotBaseline(_session.SelectedSlot);

        _session.IsLoadingDraft = true;
        try
        {
            if (_session.AppliedBaseline is { } applied)
            {
                LoadDraftFrom(applied);
            }
            else
            {
                LoadDefaultDraft();
            }
        }
        finally
        {
            _session.IsLoadingDraft = false;
        }

        // A self-driven slot must open with a usable driving sensor selected — covers a saved/default draft that
        // carried no (or only unusable) sensor indices. LoadDefaultDraft already seeds one; this also guards the
        // LoadDraftFrom path and re-seeds before the first temperature batch would otherwise do so.
        EnsureUsableSensorSelected();

        _session.PendingSnapshot = CurrentDraftSnapshot();
        AppliedCurveChangedExternally = false;
        RefreshAppliedOverlay();
        RefreshSensorChart();
        RecomputeDirty();
        RefreshPredictedDuty();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Telemetry stream handlers, custom-curve draft, dirty/prediction, history refresh, dispose.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private void LoadDefaultDraft()
    {
        SelectedFollowOption = FindFollowOption(null);
        SelectedAggregation = TemperatureAggregationMode.Maximum;

        _draft.Load([(40, 30d), (60, 60d), (80, 100d)]);

        // A self-driven curve must always start with a usable driving sensor — "none" is never a valid state.
        if (SensorSelection.SelectFirstUsableOnly() is int sensorIndex)
        {
            _historyStore.EnsureTemperatureHistory(sensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
        }
    }

    /// <summary>
    /// Keeps a self-driven custom curve from ever reaching the "no driving sensor" state. If the editor is
    /// open, not following another fan, and no <em>usable</em> sensor is selected, auto-selects the first
    /// usable one. Covers sensors that arrive after the default draft loads and a selected sensor that later
    /// becomes unusable. No-op while not editing, while following, or when a usable sensor is already chosen.
    /// </summary>
    private void EnsureUsableSensorSelected()
    {
        if (!ShowCustomEditor || IsFollowing)
        {
            return;
        }

        if (SensorSelection.SelectFirstUsableIfNoneSelected() is int sensorIndex)
        {
            _historyStore.EnsureTemperatureHistory(sensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
            RefreshSensorChart();
        }
    }

    /// <summary>Captures the current draft (follow target, points, aggregation, selected sensors) for comparison/revert.</summary>
    private CustomCurveSnapshot CurrentDraftSnapshot() => new(
        SelectedAggregation ?? TemperatureAggregationMode.Maximum,
        SensorSelection.SelectedIndices(),
        _draft.ToOrderedPairs(),
        SelectedFollowOption?.FanIndex);

    /// <summary>Replaces the editable draft (follow target, points, aggregation, sensor selection) with the given snapshot.</summary>
    private void LoadDraftFrom(CustomCurveSnapshot snapshot)
    {
        SelectedFollowOption = FindFollowOption(snapshot.FollowFanIndex);
        SelectedAggregation = snapshot.Aggregation;

        SensorSelection.SetSelected(snapshot.SensorIndices);

        foreach (var sensorIndex in snapshot.SensorIndices)
        {
            _historyStore.EnsureTemperatureHistory(sensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
        }

        _draft.Load(snapshot.CurvePoints);
    }

    private void RecomputeDirty()
    {
        (IsDirty, IsTestDraftChanged) = _session.ComputeDirty(CurrentDraftSnapshot(), IsTesting);
        NotifyCommandStates();
    }

    private void RefreshAppliedOverlay() =>
        HasAppliedCurveOverlay = CurveChart.SetAppliedOverlay(_session.AppliedBaseline);

    private void RefreshPredictedDuty()
    {
        var isCustom = SelectedFanMode == FanControlMode.CustomCurve;
        var draft = isCustom ? CurrentDraftSnapshot() : null;
        HasPredictedDuty = CurveChart.RefreshPrediction(draft, isCustom ? CurrentDrivingTemperatureCelsius() : null, isCustom);
    }

    private double? CurrentDrivingTemperatureCelsius()
    {
        var readings = new List<double>();
        foreach (var chip in SensorSelection.SelectedChips)
        {
            if (chip.CurrentTemperatureCelsius is double celsius)
            {
                readings.Add(celsius);
            }
        }

        if (readings.Count == 0)
        {
            return null;
        }

        return (SelectedAggregation ?? TemperatureAggregationMode.Maximum) switch
        {
            TemperatureAggregationMode.Average => readings.Average(),
            TemperatureAggregationMode.Maximum => readings.Max(),
            TemperatureAggregationMode.Minimum => readings.Min(),
            TemperatureAggregationMode.Median => TemperatureSeriesMath.Median(readings),
            _ => readings.Max(),
        };
    }

    private void NotifyCommandStates()
    {
        ApplyCustomCurveCommand.NotifyCanExecuteChanged();
        RevertToAppliedCommand.NotifyCanExecuteChanged();
        TestOnFanCommand.NotifyCanExecuteChanged();
        RetestCommand.NotifyCanExecuteChanged();
        ClearProfileCommand.NotifyCanExecuteChanged();
        KeepTestCommand.NotifyCanExecuteChanged();
        DiscardTestCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reconciles the editor with a service-published control-state change for the selected fan,
    /// honoring the multi-instance rule that service state is authoritative while preserving
    /// in-progress local edits.
    /// </summary>
    private void OnSelectedFanAppliedStateChanged()
    {
        if (IsTesting)
        {
            return;
        }

        var newBaseline = BuildAppliedBaseline();

        var unchanged = (newBaseline is null && _session.AppliedBaseline is null)
            || (newBaseline is { } incoming && _session.AppliedBaseline is { } current && incoming.Matches(current));
        if (unchanged)
        {
            return;
        }

        // The applied baseline changed (e.g. an Apply activated the curve, or another client switched the
        // mode) — a staged Custom-curve activation may have just become moot, so re-derive the pending pill
        // and command enablement.
        RefreshStagedPill();
        NotifyCommandStates();

        if (!ShowCustomEditor && SelectedFanMode != FanControlMode.CustomCurve)
        {
            _session.AppliedBaseline = newBaseline;
            RefreshAppliedOverlay();
            RecomputeDirty();
            return;
        }

        if (!IsDirty)
        {
            // No local edits in flight: follow the service by reloading the selected slot.
            LoadSelectedSlot();
        }
        else
        {
            // Preserve the user's unsaved edits, but surface that the running curve changed elsewhere.
            _session.AppliedBaseline = newBaseline;
            RefreshAppliedOverlay();
            AppliedCurveChangedExternally = true;
            RecomputeDirty();
            RefreshPredictedDuty();
        }
    }

    [RelayCommand]
    private void RemoveCurvePoint(CurvePointModel? point)
    {
        if (point is null) return;
        _draft.Remove(point);
    }

    public void AddCurvePointAt(double temperatureCelsius, double dutyPercent) =>
        _draft.Add(temperatureCelsius, dutyPercent);

    public void UpdateCurvePoint(CurvePointModel point, double temperatureCelsius, double dutyPercent) =>
        _draft.Update(point, temperatureCelsius, dutyPercent);

    public CurvePointModel? FindNearestCurvePoint(double temperatureCelsius, double dutyPercent, double maxTemperatureDelta, double maxDutyDelta) =>
        _draft.FindNearest(temperatureCelsius, dutyPercent, maxTemperatureDelta, maxDutyDelta);

    private void RefreshCurveSeries()
    {
        CurveChart.RebuildCurve(_draft.CurvePoints);

        // The draft curve changed (point added/moved/removed or reload): refresh dirty state and preview.
        RecomputeDirty();
        RefreshPredictedDuty();
    }

    private void RefreshUnitFormatting()
    {
        // Rebind the curve temperature axis labeler so it relabels with the new unit.
        CurveChart.RefreshUnitFormatting();

        foreach (var fan in _hub.Fans)
        {
            fan.RefreshUnitFormatting();
            RefreshFanHistory(fan.Snapshot.FanIndex);
        }

        RefreshAllDrivingTemperatureHistory();
        RefreshSensorChart();
    }

    // The five telemetry streams stay subscribed here, but the data lands in FanTelemetryHub; these handlers
    // forward the change set and then run the page's own reactions (selection, link chips, editor reconcile).
    private void ApplyCapabilityChanges(IChangeSet<FanCapabilityState, int> changes)
        => _hub.ApplyCapabilityChanges(changes);

    private void ApplyControlStateChanges(IChangeSet<FanControlStateSnapshot, int> changes)
    {
        _hub.ApplyControlStateChanges(changes);
        RefreshAllDrivingTemperatureHistory();
        // LinkedLeaderIndex rides on the control state, so re-derive the link chips + row-disabling when it streams
        // in (covers persisted groups loaded at startup and links written by another client). The CPU boost
        // strength rides on the control state too.
        LinkSection.UpdateLinkChipStates();
        BoostSection.RefreshFromSelection();

        if (SelectedFan is not null)
        {
            ControlStateRevision++;
            OnSelectedFanAppliedStateChanged();
        }
    }

    private void ApplyFanStateChanges(IChangeSet<FanStateSnapshot, int> changes)
    {
        _hub.ApplyFanStateChanges(changes);
        LinkSection.UpdateLinkChipStates();
        UpdateSelectedFanStalled();
    }

    private void ApplyFanTelemetryChanges(IChangeSet<FanTelemetrySnapshot, int> changes)
    {
        _hub.ApplyFanTelemetryChanges(changes);
        LinkSection.UpdateLinkChipStates();
    }

    // The hub created a new fan card: select it (preserving the original lowest-index auto-select), else relink.
    private void OnFanAdded(int fanIndex)
    {
        if (_hub.GetFan(fanIndex) is not { } fan)
        {
            return;
        }

        RefreshDrivingTemperatureHistory(fan);

        if (SelectedFan is null || fanIndex < SelectedFan.Snapshot.FanIndex)
        {
            SelectFan(fan);
        }
        else
        {
            // A new fan joined the fleet while another stays selected: surface it as a link chip.
            LinkSection.RebuildLinkChips();
        }
    }

    // The hub removed a fan card: re-select another if it was current, else drop it from the link groups.
    private void OnFanRemoved(int fanIndex)
    {
        if (SelectedFan?.Snapshot.FanIndex == fanIndex)
        {
            SelectedFan = _hub.Fans.FirstOrDefault();
        }
        else
        {
            LinkSection.RemoveFanFromSets(fanIndex);
            LinkSection.RebuildLinkChips();
        }
    }

    private void ApplyTemperatureChanges(IChangeSet<TemperatureTelemetrySnapshot, int> changes)
    {
        // The hub caches the snapshots and refreshes each fan's driving-sensor readouts; the page keeps the
        // selectable sensor chips (their selection drives the custom curve, an editor concern).
        _hub.ApplyTemperatureSnapshots(changes);

        var anyChanged = false;

        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                SensorSelection.Remove(change.Key);
                _historyStore.StopTemperatureHistory(change.Key);
                anyChanged = true;
                continue;
            }

            SensorSelection.Upsert(change.Current);
            anyChanged = true;
        }

        if (anyChanged)
        {
            // A late-arriving / removed sensor must not leave a self-driven curve with no driving sensor.
            EnsureUsableSensorSelected();

            // Current driving temperature moved: refresh the "what would this curve do now" preview.
            RefreshPredictedDuty();
        }
    }

    // The sensor chips own their cached chart series; on removal, drop it and re-render.
    private void OnSensorRemoved(int sensorIndex)
    {
        SensorChart.RemoveSensor(sensorIndex);
        RefreshSensorChart();
    }

    private void RefreshSensorChart()
    {
        SensorChart.Rebuild(SensorSelection.SelectedChips, _historyStore.TemperatureHistory, SelectedAggregation ?? TemperatureAggregationMode.Maximum);

        // Sensor selection or aggregation feeds both dirty state and the predicted-duty preview.
        RecomputeDirty();
        RefreshPredictedDuty();
    }

    private void RefreshFanHistory(int fanIndex)
    {
        if (_hub.GetFan(fanIndex) is not { } fan)
        {
            return;
        }

        if (_historyStore.GetFanHistory(fanIndex) is not { Length: > 0 } points)
        {
            fan.FanSpeedHistory = [];
            return;
        }

        var converted = new DateTimePoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            converted[i] = new DateTimePoint(point.ObservedAt.LocalDateTime, _unitFormattingService.ConvertFanSpeed(point.SpeedRpm));
        }

        fan.FanSpeedHistory = converted;
    }

    // Coalesced (sampled ~3 Hz) recompute of all temperature-history visuals. Running the per-timestamp
    // cross-sensor aggregation (O(points^2)) on every poll sample was the source of the editor-chart lag
    // and elevated CPU; sampling keeps it visually live without the churn.
    private void RefreshTemperatureHistoryDisplays()
    {
        foreach (var fan in _hub.Fans)
        {
            RefreshDrivingTemperatureHistory(fan);
        }

        if (SelectedFanMode != FanControlMode.CustomCurve)
        {
            return;
        }

        SensorChart.RefreshLiveData(SensorSelection.SelectedChips, _historyStore.TemperatureHistory, SelectedAggregation ?? TemperatureAggregationMode.Maximum);
    }

    private void RefreshAllDrivingTemperatureHistory()
    {
        foreach (var fan in _hub.Fans)
        {
            RefreshDrivingTemperatureHistory(fan);
        }
    }

    private void RefreshDrivingTemperatureHistory(FanCardModel fan)
    {
        var state = fan.ControlState;
        if (state is null || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            fan.DrivingTemperatureHistory = [];
            return;
        }

        var perSensor = new List<TelemetryPoint[]>(state.DrivingSensorIndices.Length);
        foreach (var sensorIndex in state.DrivingSensorIndices)
        {
            if (_historyStore.TemperatureHistory.TryGetValue(sensorIndex, out var points) && points.Length > 0)
            {
                perSensor.Add(points);
            }
        }

        if (perSensor.Count == 0)
        {
            fan.DrivingTemperatureHistory = [];
            return;
        }

        var timestampSet = new SortedSet<DateTimeOffset>();
        foreach (var series in perSensor)
        {
            foreach (var point in series)
            {
                timestampSet.Add(point.ObservedAt);
            }
        }

        var aggregation = state.DrivingTemperatureAggregation;
        var output = new List<DateTimePoint>(timestampSet.Count);

        foreach (var timestamp in timestampSet)
        {
            var readings = new List<double>(perSensor.Count);
            foreach (var series in perSensor)
            {
                var nearest = TemperatureSeriesMath.FindNearestValue(series, timestamp);
                if (nearest is double value)
                {
                    readings.Add(value);
                }
            }

            if (readings.Count == 0) continue;

            var aggregated = aggregation switch
            {
                TemperatureAggregationMode.Average => readings.Average(),
                TemperatureAggregationMode.Maximum => readings.Max(),
                TemperatureAggregationMode.Minimum => readings.Min(),
                TemperatureAggregationMode.Median => TemperatureSeriesMath.Median(readings),
                _ => readings.Average(),
            };

            output.Add(new DateTimePoint(timestamp.LocalDateTime, _unitFormattingService.ConvertTemperature(aggregated)));
        }

        fan.DrivingTemperatureHistory = output.ToArray();
    }

    internal void ReportStatus(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusVisible = true;
    }

    public void Dispose()
    {
        // The history subscriptions live in the (DI-owned) history store; just detach our render handlers.
        _historyStore.FanHistoryChanged -= RefreshFanHistory;
        _historyStore.TemperatureHistoryChanged -= OnTemperatureHistoryChanged;
        _hub.FanAdded -= OnFanAdded;
        _hub.FanRemoved -= OnFanRemoved;
        SensorSelection.SelectionChanged -= RefreshSensorChart;
        SensorSelection.SensorRemoved -= OnSensorRemoved;
        SensorSelection.SensorRenamed -= SensorChart.UpdateSensorName;
        SensorSelection.DisposeHandlers();

        _draft.Changed -= RefreshCurveSeries;
        _draft.Dispose();

        // Closing any open preview hold makes the service revert an in-flight (uncommitted) preview.
        _actuator.CancelPreviewHold();
        _subscriptions.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Observable live state: mode flags, visibilities, command predicates, manual-duty debounce.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>True while a draft curve is applied transiently for "Test on fan" pending Keep/Discard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestingVisibility))]
    [NotifyPropertyChangedFor(nameof(EditingActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(UnsavedChangesVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarStagedVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarEditingVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarCleanVisibility))]
    [NotifyPropertyChangedFor(nameof(StagedFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(CleanFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(StagedSummaryText))]
    public partial bool IsTesting { get; set; }

    partial void OnIsTestingChanged(bool value)
    {
        NotifyCommandStates();
        RefreshStagedPill();

        // Any path that ends a preview (Apply, Revert, fan switch, dispose) closes the safety hold. A commit
        // already released the service-side hold first, so closing then is a harmless no-op; an uncommitted
        // close makes the service revert the fan.
        if (!value)
        {
            _actuator.CancelPreviewHold();
        }
    }

    /// <summary>True while testing when the draft has been edited since it was last pushed to the fan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetestVisibility))]
    public partial bool IsTestDraftChanged { get; set; }

    public Microsoft.UI.Xaml.Visibility RetestVisibility =>
        IsTesting && IsTestDraftChanged ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility UnsavedChangesVisibility =>
        IsDirty && !IsTesting ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility AppliedOverlayVisibility =>
        HasAppliedCurveOverlay ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility ExternalChangeVisibility =>
        AppliedCurveChangedExternally ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility PredictedDutyVisibility =>
        HasPredictedDuty ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility TestingVisibility =>
        IsTesting ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility EditingActionsVisibility =>
        IsTesting ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    // A follow slot is valid without curve points/sensors (it mirrors another fan); a self-driven slot needs both.
    private bool HasValidDraft => IsFollowing || (_draft.CurvePoints.Count >= 2 && SensorSelection.AnySelected);

    // Whether the slot being edited is the one currently driving the fan.
    private bool IsSelectedSlotActive =>
        SelectedFan?.ControlState is { Mode: FanControlMode.CustomCurve } state && state.ActiveCurveSlot == _session.SelectedSlot;

    // Uniform staging flow across all four modes (custom draft stages via IsDirty; Auto/Manual/Max via
    // _session.StagedMode):
    //   clean       → Apply / Revert / Preview all disabled
    //   staged      → Preview + Revert + Apply enabled (preview optional; Apply commits the staged change)
    //   previewing  → Apply + Revert enabled, Preview disabled (a test is already live)

    // Apply commits the staged change, and is only enabled once it is being previewed live: the flow is
    // strictly Stage → Preview → Apply. A custom draft additionally must be valid.
    private bool CanApplyStaged => HasSelectedFan && CanIssueFanCommands
        && ((IsTesting && (!IsCustomStaging || HasValidDraft)) || LinkSection.HasStagedLinks || BoostSection.HasStagedBoosts);

    // Revert stops a live preview (restoring the prior state) or clears a staged-but-unpreviewed change.
    private bool CanRevertStaged => HasSelectedFan && CanIssueFanCommands
        && (IsTesting || CurrentFanHasStagedEdits || IsCustomActivationStaged || HasStagedSimpleMode || LinkSection.HasStagedLinks || BoostSection.HasStagedBoosts);

    // Preview tries the staged change live (volatile). Custom needs a valid self-driven draft that is either
    // edited or not yet driving the fan (activation staged); simple needs a staged mode. Never offered while
    // a preview is already running.
    private bool CanPreviewStaged => HasSelectedFan && CanIssueFanCommands && !IsTesting
        && (IsCustomStaging
            ? (CurrentFanHasStagedEdits || IsCustomActivationStaged) && !IsFollowing && HasValidDraft
            : HasStagedSimpleMode);

    private bool CanClearProfile => HasSelectedFan && !IsTesting && CanIssueFanCommands && _session.AppliedBaseline is not null;

    private bool CanResolveTest() => IsTesting;

    // Mirrors the six-flag fan-control enablement rule in SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIssueFanCommands))]
    [NotifyPropertyChangedFor(nameof(CanSelectMode))]
    [NotifyPropertyChangedFor(nameof(CanCommandFanMode))]
    [NotifyPropertyChangedFor(nameof(FanControlBlockedMessage))]
    [NotifyPropertyChangedFor(nameof(FanControlBlockedVisibility))]
    [NotifyPropertyChangedFor(nameof(FanControlValidationWarningVisibility))]
    [NotifyPropertyChangedFor(nameof(FanControlValidationWarningMessage))]
    [NotifyCanExecuteChangedFor(nameof(EnterCustomEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomCurveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertToAppliedCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestOnFanCommand))]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    /// <summary>
    /// Whether the UI may issue mutating fan-control RPCs. Per
    /// SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md, reachability alone is not enough.
    /// </summary>
    public bool CanIssueFanCommands =>
        LastStatus is { } status
        && status.IsGrpcActive
        && status.IsLibraryAvailable
        && status.IsFrameworkDevice == true
        && !status.RequiresElevation
        && status.IsConnectionOpen
        && status.IsFanControlEnabled;

    public Microsoft.UI.Xaml.Visibility FanControlBlockedVisibility =>
        CanIssueFanCommands ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>Shown when fan control is enabled but the transport cannot validate caller identity.</summary>
    public Microsoft.UI.Xaml.Visibility FanControlValidationWarningVisibility =>
        CanIssueFanCommands && LastStatus is { HasCallerIdentityValidation: false }
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string FanControlValidationWarningMessage =>
        LastStatus?.FanControlAuthorizationMessage
        ?? "Fan control is enabled, but the service cannot validate caller identity on this transport. Commands are still sent fail-closed.";

    public string FanControlBlockedMessage => DescribeFanControlBlock();

    private string DescribeFanControlBlock()
    {
        if (LastStatus is not { } status)
        {
            return "Waiting for service status before fan-control commands can be enabled.";
        }

        if (!status.IsGrpcActive || !status.IsConnectionOpen)
        {
            return "The background service is not reachable, so fan-control commands are unavailable.";
        }
        if (status.RequiresElevation)
        {
            return "The service requires elevation before fan-control commands are available.";
        }
        if (!status.IsLibraryAvailable)
        {
            return "The Framework library is not available, so fan-control commands are unavailable.";
        }
        if (status.IsFrameworkDevice != true)
        {
            return "This device is not recognized as a supported Framework device, so fan-control commands are unavailable.";
        }
        if (!status.IsFanControlEnabled)
        {
            return status.FanControlAuthorizationMessage
                ?? "Fan-control commands are disabled by the background service. You can edit a curve here, but it cannot be sent to the fan.";
        }

        return string.Empty;
    }

    internal void ReportFanControlBlocked() =>
        ReportStatus(FanControlBlockedMessage, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);

    public bool IsAutoSelected
    {
        get => SelectedFanMode == FanControlMode.Auto && !ShowCustomEditor;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.Auto;
            }
        }
    }

    public bool IsManualSelected
    {
        get => SelectedFanMode == FanControlMode.Manual && !ShowCustomEditor;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.Manual;
            }
        }
    }

    public bool IsMaxSelected
    {
        get => SelectedFanMode == FanControlMode.Max && !ShowCustomEditor;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.Max;
            }
        }
    }

    public bool IsCustomSelected
    {
        get => SelectedFanMode == FanControlMode.CustomCurve || ShowCustomEditor;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.CustomCurve;
                ShowCustomEditor = true;
            }
        }
    }

    public bool IsAutoModeChecked
    {
        get => SelectedMode == FanControlMode.Auto;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.Auto;
            }
        }
    }

    public bool IsManualModeChecked
    {
        get => SelectedMode == FanControlMode.Manual;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.Manual;
            }
        }
    }

    public bool IsMaxModeChecked
    {
        get => SelectedMode == FanControlMode.Max;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.Max;
            }
        }
    }

    public bool IsCustomModeChecked
    {
        get => SelectedMode == FanControlMode.CustomCurve || ShowCustomEditor;
        set
        {
            if (value)
            {
                SelectedMode = FanControlMode.CustomCurve;
            }
            else
            {
                ShowCustomEditor = false;
            }
        }
    }

    public Microsoft.UI.Xaml.Media.Brush AutoTileBackground => GetTileBackground(IsAutoSelected);

    public Microsoft.UI.Xaml.Media.Brush ManualTileBackground => GetTileBackground(IsManualSelected);

    public Microsoft.UI.Xaml.Media.Brush MaxTileBackground => GetTileBackground(IsMaxSelected);

    public Microsoft.UI.Xaml.Media.Brush CustomTileBackground => GetTileBackground(IsCustomSelected || ShowCustomEditor);

    private static Microsoft.UI.Xaml.Media.Brush GetTileBackground(bool selected) => selected
        ? AppThemeBrushes.Get("CardSelectedBackgroundBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : AppThemeBrushes.Get("CardBackgroundBrush", AppThemeBrushes.CardBackgroundColor);

    public IReadOnlyList<TemperatureAggregationMode> AggregationModes { get; } =
    [
        TemperatureAggregationMode.Average,
        TemperatureAggregationMode.Maximum,
        TemperatureAggregationMode.Minimum,
        TemperatureAggregationMode.Median,
    ];

    // Nullable to avoid an NRE in the generated x:Bind TwoWay write-back for ComboBox.SelectedItem,
    // which unboxes a transient null during initial bind ordering.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAggregateMaximum))]
    [NotifyPropertyChangedFor(nameof(IsAggregateAverage))]
    [NotifyPropertyChangedFor(nameof(IsAggregateMedian))]
    [NotifyPropertyChangedFor(nameof(IsAggregateMinimum))]
    [NotifyPropertyChangedFor(nameof(SensorAggregateLabel))]
    public partial TemperatureAggregationMode? SelectedAggregation { get; set; } = TemperatureAggregationMode.Maximum;

    partial void OnSelectedAggregationChanged(TemperatureAggregationMode? value) => RefreshSensorChart();

    // Aggregation segmented control (Maximum / Average / Median / Minimum) — replaces the old combo.
    public bool IsAggregateMaximum => SelectedAggregation == TemperatureAggregationMode.Maximum;

    public bool IsAggregateAverage => SelectedAggregation == TemperatureAggregationMode.Average;

    public bool IsAggregateMedian => SelectedAggregation == TemperatureAggregationMode.Median;

    public bool IsAggregateMinimum => SelectedAggregation == TemperatureAggregationMode.Minimum;

    /// <summary>The aggregation word shown on the driving-temperature chart header (e.g. "Maximum of selected sensors").</summary>
    public string SensorAggregateLabel => $"{SelectedAggregation ?? TemperatureAggregationMode.Maximum} of selected sensors";

    [RelayCommand]
    private void SetAggregation(string? mode)
    {
        if (Enum.TryParse<TemperatureAggregationMode>(mode, out var value))
        {
            SelectedAggregation = value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFan))]
    [NotifyPropertyChangedFor(nameof(CanSelectMode))]
    [NotifyPropertyChangedFor(nameof(CanCommandFanMode))]
    [NotifyPropertyChangedFor(nameof(EditorVisibility))]
    [NotifyPropertyChangedFor(nameof(IsAutoBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsManualBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsMaxBodyVisible))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeVisibility))]
    [NotifyPropertyChangedFor(nameof(ModeTargetText))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetValues))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetRemaining))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetVisibility))]
    [NotifyPropertyChangedFor(nameof(ModeDescriptionTitle))]
    [NotifyPropertyChangedFor(nameof(ModeDescriptionText))]
    [NotifyPropertyChangedFor(nameof(EditorControlsVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarCleanVisibility))]
    [NotifyPropertyChangedFor(nameof(IsAutoSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsMaxSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(IsAutoModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsManualModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsMaxModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsCustomModeChecked))]
    [NotifyPropertyChangedFor(nameof(AutoTileBackground))]
    [NotifyPropertyChangedFor(nameof(ManualTileBackground))]
    [NotifyPropertyChangedFor(nameof(MaxTileBackground))]
    [NotifyPropertyChangedFor(nameof(CustomTileBackground))]
    [NotifyPropertyChangedFor(nameof(SelectedMode))]
    [NotifyPropertyChangedFor(nameof(SelectedModeIndex))]
    [NotifyPropertyChangedFor(nameof(SelectedFanHeading))]
    [NotifyPropertyChangedFor(nameof(SelectedFanModeDisplay))]
    [NotifyPropertyChangedFor(nameof(ActiveProfileText))]
    [NotifyCanExecuteChangedFor(nameof(EnterCustomEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomCurveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertToAppliedCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestOnFanCommand))]
    public partial FanCardModel? SelectedFan { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeVisibility))]
    [NotifyPropertyChangedFor(nameof(IsAutoSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsMaxSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(IsAutoModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsManualModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsMaxModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsCustomModeChecked))]
    [NotifyPropertyChangedFor(nameof(AutoTileBackground))]
    [NotifyPropertyChangedFor(nameof(ManualTileBackground))]
    [NotifyPropertyChangedFor(nameof(MaxTileBackground))]
    [NotifyPropertyChangedFor(nameof(CustomTileBackground))]
    [NotifyPropertyChangedFor(nameof(CanSelectMode))]
    [NotifyCanExecuteChangedFor(nameof(EnterCustomEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCustomEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomCurveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertToAppliedCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestOnFanCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedMode))]
    [NotifyPropertyChangedFor(nameof(SelectedModeIndex))]
    [NotifyPropertyChangedFor(nameof(StagedFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(CleanFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarStagedVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarEditingVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarCleanVisibility))]
    public partial bool ShowCustomEditor { get; set; }

    partial void OnShowCustomEditorChanged(bool value)
    {
        RefreshStagedPill();
        NotifyCommandStates();
    }

    public bool CanSelectMode => HasSelectedFan && !ShowCustomEditor && CanIssueFanCommands;

    // Auto/Manual/Max stay selectable even while the custom editor is open so the user is never stranded
    // in the editor; choosing one exits the editor (via the SelectedMode setter) and switches mode.
    public bool CanCommandFanMode => HasSelectedFan && CanIssueFanCommands;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeTargetText))]
    [NotifyPropertyChangedFor(nameof(ManualDutyDisplay))]
    [NotifyPropertyChangedFor(nameof(IsPreset25))]
    [NotifyPropertyChangedFor(nameof(IsPreset50))]
    [NotifyPropertyChangedFor(nameof(IsPreset80))]
    [NotifyPropertyChangedFor(nameof(IsPreset100))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetValues))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetRemaining))]
    public partial double ManualDutyPercent { get; set; } = DefaultManualDutyPercent;

    // The active quick-preset (the one matching the current duty) is highlighted accent.
    public bool IsPreset25 => Math.Abs(ManualDutyPercent - 25d) < 0.5d;

    public bool IsPreset50 => Math.Abs(ManualDutyPercent - 50d) < 0.5d;

    public bool IsPreset80 => Math.Abs(ManualDutyPercent - 80d) < 0.5d;

    public bool IsPreset100 => Math.Abs(ManualDutyPercent - 100d) < 0.5d;

    // Faint accent "ghost arc" on the mode gauge marking where the fan will settle: Manual → duty%, Max → 100%.
    // Auto has no fixed target, so the ghost arc is hidden there.
    private double ModeGaugeTargetPercent => SelectedFanMode switch
    {
        FanControlMode.Max => 100d,
        FanControlMode.Manual => Math.Clamp(ManualDutyPercent, 0d, 100d),
        _ => 0d,
    };

    public double[] ModeGaugeTargetValues => [ModeGaugeTargetPercent];

    public double[] ModeGaugeTargetRemaining => [Math.Max(0d, 100d - ModeGaugeTargetPercent)];

    public Microsoft.UI.Xaml.Visibility ModeGaugeTargetVisibility =>
        !IsSelectedFanStalled && SelectedFanMode is FanControlMode.Manual or FanControlMode.Max
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public SolidColorPaint ModeGaugeTargetPaint { get; } = new(new SKColor(0x8A, 0xB7, 0xE8, 0x9E));

    public SolidColorPaint ModeGaugeTargetTrackPaint { get; } = new(new SKColor(0x00, 0x00, 0x00, 0x00));

    /// <summary>Quick-preset buttons under the Manual slider (Silent 25 / Balanced 50 / Performance 80 / Full 100).</summary>
    [RelayCommand]
    private void SetManualPreset(string? percent)
    {
        if (double.TryParse(percent, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            ManualDutyPercent = Math.Clamp(value, 0d, 100d);
        }
    }

    partial void OnManualDutyPercentChanged(double value)
    {
        var clamped = Math.Clamp(value, 0d, 100d);

        // The programmatic seed done when staging Manual must not itself re-stage or re-preview.
        if (_session.IsSeedingManualDuty)
        {
            return;
        }

        // Editing the duty while the Manual body is showing stages a Manual change (whether arriving from
        // another mode or adjusting an already-Manual fan) — it does not actuate the EC.
        if (SelectedFanMode == FanControlMode.Manual)
        {
            _session.StagedManualDuty = clamped;
            if (_session.StagedMode != FanControlMode.Manual)
            {
                _session.StagedMode = FanControlMode.Manual;
                ControlStateRevision++;
                RefreshStagedPill();
            }
            RecomputeDirty();
        }

        // Only push live duty changes while an active preview is running (re-preview as the slider moves).
        if (IsTesting && SelectedFanMode == FanControlMode.Manual)
        {
            _manualDutyChanges.OnNext(clamped);
        }
    }

    private async Task ApplyDebouncedManualDutyAsync(double duty, CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null || !IsTesting || SelectedFanMode != FanControlMode.Manual || !CanIssueFanCommands)
        {
            return;
        }

        try
        {
            // Live re-preview (volatile); committing happens only via Apply.
            await _actuator.ActuateSimpleAsync(fan.Snapshot.FanIndex, FanControlMode.Manual, duty, preview: true, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-preview manual duty {Duty:0}% for fan {FanIndex}", duty, fan.Snapshot.FanIndex);
            ReportStatus($"Failed to preview manual duty: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Controls.InfoBarSeverity StatusSeverity { get; set; }
        = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsStatusVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFanModeDisplay))]
    [NotifyPropertyChangedFor(nameof(IsAutoBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsManualBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsMaxBodyVisible))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeVisibility))]
    [NotifyPropertyChangedFor(nameof(ModeTargetText))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetValues))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetRemaining))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetVisibility))]
    [NotifyPropertyChangedFor(nameof(ModeDescriptionTitle))]
    [NotifyPropertyChangedFor(nameof(ModeDescriptionText))]
    [NotifyPropertyChangedFor(nameof(StagedFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(CleanFooterVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarStagedVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarEditingVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarCleanVisibility))]
    [NotifyPropertyChangedFor(nameof(IsAutoSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsMaxSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(IsAutoModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsManualModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsMaxModeChecked))]
    [NotifyPropertyChangedFor(nameof(IsCustomModeChecked))]
    [NotifyPropertyChangedFor(nameof(AutoTileBackground))]
    [NotifyPropertyChangedFor(nameof(ManualTileBackground))]
    [NotifyPropertyChangedFor(nameof(MaxTileBackground))]
    [NotifyPropertyChangedFor(nameof(CustomTileBackground))]
    [NotifyPropertyChangedFor(nameof(SelectedMode))]
    [NotifyPropertyChangedFor(nameof(SelectedModeIndex))]
    [NotifyPropertyChangedFor(nameof(ActiveProfileText))]
    private partial int ControlStateRevision { get; set; }

    public bool HasSelectedFan => SelectedFan is not null;

    public Microsoft.UI.Xaml.Visibility EditorVisibility =>
        SelectedFan is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    // When the selected fan reads as Stalled (0 RPM while driven), the editor locks down: every control is
    // hidden and replaced by a full-height warning panel (design: stalled-fan lockdown).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StalledLockdownVisibility))]
    [NotifyPropertyChangedFor(nameof(EditorControlsVisibility))]
    [NotifyPropertyChangedFor(nameof(IsAutoBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsManualBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsMaxBodyVisible))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeVisibility))]
    [NotifyPropertyChangedFor(nameof(ActionBarCleanVisibility))]
    public partial bool IsSelectedFanStalled { get; set; }

    public Microsoft.UI.Xaml.Visibility StalledLockdownVisibility =>
        IsSelectedFanStalled ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>The Applies-to card + mode selector (hidden while the selected fan is stalled).</summary>
    public Microsoft.UI.Xaml.Visibility EditorControlsVisibility =>
        SelectedFan is not null && !IsSelectedFanStalled
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsAutoBodyVisible =>
        SelectedFanMode == FanControlMode.Auto && !IsSelectedFanStalled ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsManualBodyVisible =>
        SelectedFanMode == FanControlMode.Manual && !IsSelectedFanStalled ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsCustomBodyVisible =>
        (SelectedFanMode == FanControlMode.CustomCurve || ShowCustomEditor) && !IsSelectedFanStalled
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsMaxBodyVisible =>
        SelectedFanMode == FanControlMode.Max && !IsSelectedFanStalled ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>The big current-speed gauge shown for the non-custom modes (Auto/Manual/Max).</summary>
    public Microsoft.UI.Xaml.Visibility ModeGaugeVisibility =>
        SelectedFan is not null
        && !ShowCustomEditor
        && !IsSelectedFanStalled
        && SelectedFanMode is FanControlMode.Auto or FanControlMode.Manual or FanControlMode.Max
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>Target line under the mode gauge ("→ Max target", "→ 80% duty target", or the controller note).</summary>
    public string ModeTargetText => SelectedFanMode switch
    {
        FanControlMode.Max => "→ Max target",
        FanControlMode.Manual => $"→ {ManualDutyPercent:0}% duty target",
        _ => "→ Controller policy",
    };

    public string ModeDescriptionTitle => SelectedFanMode switch
    {
        FanControlMode.Max => "Max mode active",
        FanControlMode.Manual => "Manual control",
        _ => "Auto mode active",
    };

    public string ModeDescriptionText => SelectedFanMode switch
    {
        FanControlMode.Max => "This fan is forced to 100% duty. Expect significantly increased acoustics. Switch to Auto to release.",
        FanControlMode.Manual => "Set a fixed duty cycle. The faint ghost arc previews where the fan will settle once applied.",
        _ => "The embedded controller is driving this fan based on its built-in policy. No user override is active.",
    };

    /// <summary>Big duty readout shown beside the Manual slider (e.g. "43%").</summary>
    public string ManualDutyDisplay => _unitFormattingService.FormatRatio(ManualDutyPercent);

    public string SelectedFanHeading => SelectedFan is null
        ? "Select a fan"
        : $"Editor — {SelectedFan.Snapshot.DisplayName}";

    public string SelectedFanModeDisplay => SelectedFanMode switch
    {
        FanControlMode.Auto => "Auto",
        FanControlMode.Manual => "Manual",
        FanControlMode.CustomCurve => "Custom curve",
        FanControlMode.Max => "Max",
        _ => string.Empty,
    };

    // The mode the editor is currently showing: the custom editor wins, then any staged (not-yet-applied)
    // simple mode, otherwise the live service mode. Staging reflects in the UI without actuating the EC.
    private FanControlMode SelectedFanMode =>
        ShowCustomEditor ? FanControlMode.CustomCurve
        : _session.StagedMode ?? ServiceFanMode;
}
