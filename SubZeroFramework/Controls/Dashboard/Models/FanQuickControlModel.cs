using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
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
    public FanQuickControlModel(FanCardModel fan)
    {
        ArgumentNullException.ThrowIfNull(fan);

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
    [NotifyPropertyChangedFor(nameof(AutoSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(ManualSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(MaxSegmentForeground))]
    [NotifyPropertyChangedFor(nameof(CurveSegmentForeground))]
    public partial string NowDrivingText { get; private set; } = "Waiting for state";

    /// <summary>Progress-bar fraction under the "Now driving" line (last commanded duty; 0 in Auto). Stored; assigned by <see cref="RefreshDerivedState"/>.</summary>
    [ObservableProperty]
    public partial double DutyBarValue { get; private set; }

    // Read-only mode indicator: the active segment fills with the brand accent (brushes created at bind
    // time — UI thread; see uno-vm-thread-affinity).
    public Brush AutoSegmentBackground => SegmentBackground(FanControlMode.Auto);

    public Brush ManualSegmentBackground => SegmentBackground(FanControlMode.Manual);

    public Brush MaxSegmentBackground => SegmentBackground(FanControlMode.Max);

    public Brush CurveSegmentBackground => SegmentBackground(FanControlMode.CustomCurve);

    public Brush AutoSegmentForeground => SegmentForeground(FanControlMode.Auto);

    public Brush ManualSegmentForeground => SegmentForeground(FanControlMode.Manual);

    public Brush MaxSegmentForeground => SegmentForeground(FanControlMode.Max);

    public Brush CurveSegmentForeground => SegmentForeground(FanControlMode.CustomCurve);

    public void Detach() => Fan.PropertyChanged -= OnFanChanged;

    private Brush SegmentBackground(FanControlMode mode) => Fan.ControlState?.Mode == mode
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private Brush SegmentForeground(FanControlMode mode) => Fan.ControlState?.Mode == mode
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

    private void OnFanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FanCardModel.ControlState) or nameof(FanCardModel.Snapshot))
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

        var modeLabel = state.Mode switch
        {
            FanControlMode.Auto => "Auto",
            FanControlMode.Manual => "Manual",
            FanControlMode.Max => "Max",
            FanControlMode.CustomCurve => "Custom curve",
            _ => state.Mode.ToString(),
        };

        return state.Mode != FanControlMode.Auto && state.LastDutyPercent is double duty
            ? $"{modeLabel} · {duty:0}%"
            : modeLabel;
    }
}
