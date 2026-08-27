using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>
/// One dashboard fan card: wraps the shared <see cref="FanCardModel"/> (which feeds the ring gauge and live
/// telemetry) and adds the read-only quick-view surface — function chip, "Now driving" line, and the
/// Auto/Manual/Max/Curve mode indicator. The dashboard shows state only; fans are controlled from the Fan
/// Curve Profiles page.
/// </summary>
public partial class FanQuickControlModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FanQuickControlModel(FanCardModel fan, IUnitFormattingService unitFormattingService)
    {
        ArgumentNullException.ThrowIfNull(fan);
        ArgumentNullException.ThrowIfNull(unitFormattingService);

        _unitFormattingService = unitFormattingService;
        Fan = fan;
        fan.PropertyChanged += OnFanChanged;
        RefreshDerivedState();
    }

    /// <summary>The shared fan card model driving the ring gauge and telemetry displays.</summary>
    public FanCardModel Fan { get; }

    public int FanIndex => Fan.Snapshot.FanIndex;

    /// <summary>Function chip label (GPU/CPU/Sys) derived from the fan's role. Stored; assigned by <see cref="RefreshDerivedState"/>.</summary>
    [ObservableProperty]
    public partial string FunctionChipLabel { get; private set; } = "Sys";

    /// <summary>Function chip icon matching <see cref="FunctionChipLabel"/>. Stored; assigned by <see cref="RefreshDerivedState"/>.</summary>
    [ObservableProperty]
    public partial MaterialIconKind FunctionChipIcon { get; private set; } = MaterialIconKind.Fan;

    /// <summary>
    /// The "Now driving" line (mode label + last commanded duty). Stored; assigned by
    /// <see cref="RefreshDerivedState"/>. Every mode transition changes this text, so it also re-raises the
    /// UI-affine segment brushes below (those stay computed — brushes are created at bind time on the UI thread).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSegmentBackground))]
    [NotifyPropertyChangedFor(nameof(ManualSegmentBackground))]
    [NotifyPropertyChangedFor(nameof(MaxSegmentBackground))]
    [NotifyPropertyChangedFor(nameof(CurveSegmentBackground))]
    [NotifyPropertyChangedFor(nameof(AdaptiveSegmentBackground))]
    [NotifyPropertyChangedFor(nameof(AutoSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(ManualSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(MaxSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(CurveSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(AdaptiveSegmentForeground))]
    public partial string NowDrivingText { get; private set; } = "Waiting for state";

    /// <summary>Progress-bar fraction under the "Now driving" line (last commanded duty; 0 in Auto). Stored; assigned by <see cref="RefreshDerivedState"/>.</summary>
    [ObservableProperty]
    public partial double DutyBarValue { get; private set; }

    /// <summary>
    /// The saved profile the fans currently match, or null when none does.
    /// </summary>
    /// <remarks>
    /// Pushed in by the page rather than resolved here: whether a profile is in effect is a statement about
    /// EVERY fan, and a card that only knows its own state cannot answer it.
    /// </remarks>
    public string? ActiveProfileName
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            RefreshDerivedState();
        }
    }

    // Read-only mode indicator: the active segment fills with the brand accent (brushes created at bind
    // time — UI thread; see uno-vm-thread-affinity).
    public Brush AutoSegmentBackground => SegmentBackground(FanControlMode.Auto);

    public Brush ManualSegmentBackground => SegmentBackground(FanControlMode.Manual);

    public Brush MaxSegmentBackground => SegmentBackground(FanControlMode.Max);

    public Brush CurveSegmentBackground => SegmentBackground(FanControlMode.CustomCurve);

    public Brush AdaptiveSegmentBackground => SegmentBackground(FanControlMode.Adaptive);

    public Brush AutoSegmentForeground => SegmentForeground(FanControlMode.Auto);

    public Brush ManualSegmentForeground => SegmentForeground(FanControlMode.Manual);

    public Brush MaxSegmentForeground => SegmentForeground(FanControlMode.Max);

    public Brush CurveSegmentForeground => SegmentForeground(FanControlMode.CustomCurve);

    /// <summary>
    /// Adaptive's segment.
    /// </summary>
    /// <remarks>
    /// It was missing entirely, so a fan running Adaptive lit NONE of the four segments and the row read as
    /// though the fan were in no mode at all — the one state the indicator most needed to show, since
    /// Adaptive is the mode a user is least able to infer from the numbers.
    /// </remarks>
    public Brush AdaptiveSegmentForeground => SegmentForeground(FanControlMode.Adaptive);

    public void Detach() => Fan.PropertyChanged -= OnFanChanged;

    private Brush SegmentBackground(FanControlMode mode) => Fan.ControlState?.Mode == mode
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private Brush SegmentForeground(FanControlMode mode) => Fan.ControlState?.Mode == mode
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

    private void OnFanChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the "everything on the source changed" signal the fan card raises when the display
        // units change — NowDrivingText embeds a formatted duty, so it has to be rebuilt then too.
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(FanCardModel.ControlState) or nameof(FanCardModel.Snapshot))
        {
            RefreshDerivedState();
        }
    }

    /// <summary>
    /// Recomputes and ASSIGNS the stored displays derived from the wrapped fan's snapshot and control state.
    /// Assignment raises PropertyChanged only for values that actually changed; <see cref="NowDrivingText"/>
    /// additionally re-raises the segment brushes. Every caller is already on the UI thread.
    /// </summary>
    private void RefreshDerivedState()
    {
        FunctionChipLabel = Fan.Snapshot.FanName?.ToString() is string role
            ? role.Contains("Gpu", StringComparison.OrdinalIgnoreCase) ? "GPU"
                : role.Contains("Apu", StringComparison.OrdinalIgnoreCase) || role.Contains("Cpu", StringComparison.OrdinalIgnoreCase) ? "CPU"
                : "Sys"
            : "Sys";

        FunctionChipIcon = FunctionChipLabel switch
        {
            "GPU" => MaterialIconKind.ExpansionCard,
            "CPU" => MaterialIconKind.Chip,
            _ => MaterialIconKind.Fan,
        };

        NowDrivingText = ComputeNowDrivingText();

        DutyBarValue = Fan.ControlState is { Mode: not FanControlMode.Auto, LastDutyPercent: double duty } ? duty : 0d;
    }

    private string ComputeNowDrivingText()
    {
        if (Fan.ControlState is not { } state)
        {
            return "Waiting for state";
        }

        // The profile's name wins over the mode's when one is in effect, because it is the more useful
        // answer: "Balanced · 62%" says which decision produced this, where "Adaptive · 62%" only says how.
        // It falls back to the mode the moment the fans stop matching any profile, which is exactly when the
        // profile name would start being a lie.
        var modeLabel = ActiveProfileName ?? state.Mode switch
        {
            FanControlMode.Auto => "Auto",
            FanControlMode.Manual => "Manual",
            FanControlMode.Max => "Max",
            FanControlMode.CustomCurve => "Custom curve",
            FanControlMode.Adaptive => "Adaptive",
            _ => state.Mode.ToString(),
        };

        return state.Mode != FanControlMode.Auto && state.LastDutyPercent is double duty
            ? $"{modeLabel} · {_unitFormattingService.FormatRatio(duty, decimals: 0)}"
            : modeLabel;
    }
}
