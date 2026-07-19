using FrameworkDotnet.Enums;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// One fan's editing session on the Fan Control page. Owns the custom-curve draft's staging bookkeeping
/// (applied baseline, pending / tested snapshots, the slot being edited), the staged simple mode
/// (Auto / Manual / Max), and the simple-mode Stage → Preview → Apply orchestration, so the coordinator stays a
/// thin shell. The coordinator keeps one session PER FAN (keyed by fan index): switching fans parks the
/// outgoing fan's in-progress edits here (<see cref="DraftSnapshot"/>) and restores them on return, so
/// staged work survives fan switches until Apply or Discard. Reaches back into the coordinator (the
/// established parent-injection pattern) for the shared selection / authorization / status.
/// </summary>
public sealed class FanEditSession
{
    private const double DefaultManualDuty = 50d;

    private readonly FanCurveProfilesModel _parent;
    private readonly IFanControlActuator _actuator;
    private readonly ILogger _logger;

    public FanEditSession(FanCurveProfilesModel parent, IFanControlActuator actuator, ILogger logger)
    {
        _parent = parent;
        _actuator = actuator;
        _logger = logger;
    }

    /// <summary>The draft as last applied / loaded, for the pending pill + external-change detection.</summary>
    public CustomCurveSnapshot? PendingSnapshot { get; set; }

    /// <summary>
    /// The editor content parked when the user switched to another fan while this fan had staged work
    /// (curve points, sensor selection, aggregation, follow target). Restored — and cleared — when the fan
    /// is selected again, so in-progress edits survive fan switches until Apply or Discard. Null when the
    /// fan left the editor clean (it reloads fresh from the service on return).
    /// </summary>
    public CustomCurveSnapshot? DraftSnapshot { get; set; }

    /// <summary>Whether the custom editor was open when this fan's staged work was parked.</summary>
    public bool WasCustomEditorOpen { get; set; }

    /// <summary>
    /// Baseline describing the curve the service currently has applied to the selected fan. Null when the fan
    /// has no applied custom curve. Used for dirty detection, the read-only "applied" overlay, and revert.
    /// </summary>
    public CustomCurveSnapshot? AppliedBaseline { get; set; }

    /// <summary>The draft last pushed to the fan as a test, so edits made during a test can offer a re-apply.</summary>
    public CustomCurveSnapshot? TestedSnapshot { get; set; }

    /// <summary>
    /// The control state captured before a transient "Test on fan" apply so Discard can restore the prior
    /// mode / curve / duty without the service inventing it.
    /// </summary>
    public FanControlStateSnapshot? PreTestState { get; set; }

    /// <summary>
    /// Staged (not-yet-applied) simple mode for the selected fan, or null to reflect the live service mode.
    /// Picking Auto / Manual / Max stages it here without actuating the EC; Preview tries it live (volatile),
    /// Apply persists it, Revert clears it. CustomCurve stages via the curve draft (IsDirty), not this field.
    /// </summary>
    public FanControlMode? StagedMode { get; set; }

    /// <summary>Duty captured when Manual is staged, so Preview / Apply use the chosen duty (not per-drag actuation).</summary>
    public double StagedManualDuty { get; set; } = 50d; // matches FanCurveProfilesModel.DefaultManualDutyPercent

    /// <summary>The curve profile slot currently being edited (0 .. MaxCurveProfileSlots - 1).</summary>
    public int SelectedSlot { get; set; }

    /// <summary>Suppresses draft-change reactions (dirty/predict) while a draft is loaded programmatically.</summary>
    public bool IsLoadingDraft { get; set; }

    /// <summary>Guards the programmatic duty seed done when Manual is staged, so it doesn't re-stage re-entrantly.</summary>
    public bool IsSeedingManualDuty { get; set; }

    /// <summary>
    /// Editor dirty state for the current <paramref name="draft"/>: dirty when it differs from the applied
    /// baseline (or there is none), and "test draft changed" when a preview is live and the draft has since
    /// moved off the tested snapshot.
    /// </summary>
    public (bool IsDirty, bool IsTestDraftChanged) ComputeDirty(CustomCurveSnapshot draft, bool isTesting)
    {
        var isDirty = AppliedBaseline is not { } baseline || !draft.Matches(baseline);
        var isTestDraftChanged = isTesting && (TestedSnapshot is null || !draft.Matches(TestedSnapshot));
        return (isDirty, isTestDraftChanged);
    }

    /// <summary>
    /// Stages a simple mode (Auto/Manual/Max) for the selected fan without touching the EC. Re-picking the live
    /// service mode clears the stage. For Manual, seeds the duty editor from the live duty (or default).
    /// </summary>
    public void StageSimpleMode(FanControlMode mode)
    {
        if (mode == FanControlMode.Manual)
        {
            var seed = _parent.ServiceFanMode == FanControlMode.Manual
                ? Math.Clamp(_parent.SelectedFan?.ControlState?.LastDutyPercent ?? DefaultManualDuty, 0d, 100d)
                : Math.Clamp(_parent.ManualDutyPercent, 0d, 100d);

            StagedManualDuty = seed;
            IsSeedingManualDuty = true;
            try { _parent.ManualDutyPercent = seed; }
            finally { IsSeedingManualDuty = false; }

            StagedMode = FanControlMode.Manual;
        }
        else
        {
            // Re-picking the live mode clears the stage (no pending change); otherwise stage the new mode.
            StagedMode = mode == _parent.ServiceFanMode ? null : mode;
        }

        _parent.NotifyStagingChanged();
    }

    /// <summary>Clears any staged simple mode and re-projects the mode-derived UI.</summary>
    public void ClearStagedSimpleMode()
    {
        if (StagedMode is null)
        {
            return;
        }

        StagedMode = null;
        _parent.NotifyStagingChanged();
    }

    // Previews the staged simple mode live (volatile): the EC actuates and the change streams to clients, but the
    // service does not persist it. Captures the prior state so Revert restores it.
    public async Task PreviewSimpleModeAsync(CancellationToken cancellationToken)
    {
        var fan = _parent.SelectedFan;
        if (fan is null || StagedMode is not { } mode) return;
        if (!_parent.CanIssueFanCommands) { _parent.ReportFanControlBlocked(); return; }

        var group = _parent.ActuationGroup(fan.Snapshot.FanIndex);
        PreTestState = fan.ControlState;
        // Open the safety hold on the whole linked group before actuating so the service captures each fan's
        // pre-preview state and reverts them all if this client disconnects mid-preview.
        await _actuator.OpenPreviewHoldsAsync(group).ConfigureAwait(true);
        _parent.IsTesting = true;

        try
        {
            foreach (var fanIndex in group)
            {
                await _actuator.ActuateSimpleAsync(fanIndex, mode, StagedManualDuty, preview: true, cancellationToken).ConfigureAwait(true);
            }

            var detail = mode == FanControlMode.Manual ? $" at {StagedManualDuty:0}% duty" : string.Empty;
            var scope = group.Count > 1 ? $"{fan.Snapshot.DisplayName} + {group.Count - 1} linked fan(s)" : fan.Snapshot.DisplayName;
            _parent.ReportStatus(
                $"Previewing {DescribeSimpleMode(mode)}{detail} on {scope}. Apply to keep it, or Revert to restore.",
                InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            _parent.IsTesting = false;
            PreTestState = null;
            _logger.LogWarning(ex, "Failed to preview {Mode} for fan {FanIndex}", mode, fan.Snapshot.FanIndex);
            _parent.ReportStatus($"Failed to preview {DescribeSimpleMode(mode)}: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    // Commits the staged simple mode: actuates with persistence, then clears the stage and any live preview.
    public async Task ApplySimpleModeAsync(CancellationToken cancellationToken)
    {
        var fan = _parent.SelectedFan;
        if (fan is null || StagedMode is not { } mode) return;
        if (!_parent.CanIssueFanCommands) { _parent.ReportFanControlBlocked(); return; }

        try
        {
            // Persist the mode to the whole linked group so linked fans apply together.
            var group = _parent.ActuationGroup(fan.Snapshot.FanIndex);
            foreach (var fanIndex in group)
            {
                await _actuator.ActuateSimpleAsync(fanIndex, mode, StagedManualDuty, preview: false, cancellationToken).ConfigureAwait(true);
            }

            PreTestState = null;
            TestedSnapshot = null;
            _parent.IsTesting = false;
            ClearStagedSimpleMode();

            var scope = group.Count > 1 ? $"{fan.Snapshot.DisplayName} + {group.Count - 1} linked fan(s)" : fan.Snapshot.DisplayName;
            _parent.ReportStatus(
                mode switch
                {
                    FanControlMode.Manual => $"{scope} set to {StagedManualDuty:0}% manual duty.",
                    FanControlMode.Max => $"{scope} set to Max (100%). Acoustics will be loud.",
                    _ => $"{scope} restored to Auto.",
                },
                mode == FanControlMode.Max ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply {Mode} for fan {FanIndex}", mode, fan.Snapshot.FanIndex);
            _parent.ReportStatus($"Failed to apply {DescribeSimpleMode(mode)}: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private static string DescribeSimpleMode(FanControlMode mode) => mode switch
    {
        FanControlMode.Manual => "Manual",
        FanControlMode.Max => "Max",
        _ => "Auto",
    };
}
