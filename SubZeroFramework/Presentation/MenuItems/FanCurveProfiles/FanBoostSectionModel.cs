using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// The "CPU boost" section for the selected fan: the per-fan CPU usage modifier (extra duty points added on
/// top of the active custom curve as CPU load rises, so fans ramp before heat reaches the sensors). Mirrors
/// <see cref="FanLinkSectionModel"/>'s staging overlay: changes stage client-side immediately and are only
/// saved to the service on Apply (the service rejects modifier writes while a preview hold is open, and the
/// Apply flow flushes after the preview commits, so ordering is always safe).
/// </summary>
public partial class FanBoostSectionModel : ObservableObject
{
    /// <summary>Slider floor — 0 would be indistinguishable from "off", which the toggle already expresses.</summary>
    public const double MinimumStrength = 5d;

    public const double MaximumStrength = 100d;

    private const double DefaultStrength = 25d;

    // Two staged strengths closer than this are the same value (slider steps are whole points).
    private const double StrengthTolerance = 0.5d;

    private readonly FanCurveProfilesModel _parent;
    private readonly IUnitFormattingService _unitFormattingService;

    // Pending (not-yet-applied) boost overrides on top of the persisted service state. Keyed by fan index;
    // the value is the staged strength (null = staged-off). The UI shows the staged override when present,
    // else the fan's persisted control-state CpuUsageModifierStrength. Flushed on Apply, discarded on Revert.
    private readonly Dictionary<int, double?> _stagedBoosts = [];

    // True while RefreshFromSelection mirrors service/staged state into the bindable properties, so the
    // property-changed hooks don't re-stage what they are being fed.
    private bool _isRefreshing;

    public FanBoostSectionModel(FanCurveProfilesModel parent, IUnitFormattingService unitFormattingService)
    {
        _parent = parent;
        _unitFormattingService = unitFormattingService;
        RefreshSummary();
    }

    /// <summary>Whether the boost is on for the selected fan (staged or persisted).</summary>
    [ObservableProperty]
    public partial bool IsBoostEnabled { get; set; }

    /// <summary>Duty points added at 100% CPU load (the strength the slider edits, always in percent).</summary>
    [ObservableProperty]
    public partial double BoostStrength { get; set; } = DefaultStrength;

    /// <summary>Plain-language summary under the boost controls.</summary>
    [ObservableProperty]
    public partial string BoostSummaryText { get; set; } = string.Empty;

    /// <summary>
    /// The strength in the user's chosen ratio unit (Settings → Display units), e.g. "+25%" or "+0.25".
    /// Recomputed (not a live getter) so a unit-preference change is picked up by the next state refresh.
    /// </summary>
    [ObservableProperty]
    public partial string BoostStrengthDisplay { get; set; } = string.Empty;

    /// <summary>True when there are pending boost changes not yet saved to the service (cleared on Apply / Revert).</summary>
    public bool HasStagedBoosts => _stagedBoosts.Count > 0;

    partial void OnIsBoostEnabledChanged(bool value)
    {
        if (_isRefreshing)
        {
            return;
        }

        StageForSelectedFan(value ? BoostStrength : null);
        RefreshSummary();
    }

    partial void OnBoostStrengthChanged(double value)
    {
        if (_isRefreshing || !IsBoostEnabled)
        {
            return;
        }

        StageForSelectedFan(value);
        RefreshSummary();
    }

    /// <summary>
    /// Mirrors the selected fan's effective boost (staged override when present, else the persisted
    /// control-state strength) into the bindable properties. Called on selection change and whenever the
    /// control state streams in, so a modifier written by another client is reflected too.
    /// </summary>
    public void RefreshFromSelection()
    {
        if (_parent.SelectedFan is not { } fan)
        {
            BoostSummaryText = string.Empty;
            return;
        }

        var effective = _stagedBoosts.TryGetValue(fan.Snapshot.FanIndex, out var staged)
            ? staged
            : fan.ControlState?.CpuUsageModifierStrength;

        _isRefreshing = true;
        try
        {
            IsBoostEnabled = effective is not null;
            if (effective is double strength)
            {
                BoostStrength = Math.Clamp(strength, MinimumStrength, MaximumStrength);
            }

            // When off, the slider keeps its last position as the resume value for re-enabling.
        }
        finally
        {
            _isRefreshing = false;
        }

        RefreshSummary();
    }

    /// <summary>Persists all staged boost changes to the service (called from Apply), then clears the overlay.</summary>
    public async Task FlushStagedBoostsAsync(CancellationToken cancellationToken)
    {
        if (_stagedBoosts.Count == 0)
        {
            return;
        }

        var pending = _stagedBoosts.ToArray();
        _stagedBoosts.Clear();
        foreach (var (fanIndex, strength) in pending)
        {
            await _parent.PersistFanBoostAsync(fanIndex, strength, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Discards pending boost changes (called from Revert), reverting the UI to the persisted state.</summary>
    public void DiscardStagedBoosts()
    {
        if (_stagedBoosts.Count == 0)
        {
            return;
        }

        _stagedBoosts.Clear();
        RefreshFromSelection();
    }

    // Stages a boost change client-side (no service write) — pruned when it matches the persisted value so it
    // stops counting as a pending change.
    private void StageForSelectedFan(double? strength)
    {
        if (_parent.SelectedFan is not { } fan)
        {
            return;
        }

        var fanIndex = fan.Snapshot.FanIndex;
        var persisted = fan.ControlState?.CpuUsageModifierStrength;
        var matchesPersisted = strength is null
            ? persisted is null
            : persisted is double current && Math.Abs(current - strength.Value) < StrengthTolerance;

        if (matchesPersisted)
        {
            _stagedBoosts.Remove(fanIndex);
        }
        else
        {
            _stagedBoosts[fanIndex] = strength;
        }

        _parent.OnStagedBoostsChanged();
    }

    private void RefreshSummary()
    {
        var formattedStrength = _unitFormattingService.FormatRatio(BoostStrength);
        BoostStrengthDisplay = $"+{formattedStrength}";
        BoostSummaryText = IsBoostEnabled
            ? $"Adds up to {formattedStrength} extra duty on top of the curve as CPU load rises — the ramp is exponential, so it only kicks in under heavy load, before heat reaches the sensors."
            : "Off — this fan follows its curve alone.";
    }
}
