using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// One onboard-device tile on the Device Capabilities page: an icon-led title ("Sensor 0" / "CPU fan"), a large
/// live value ("54 °C" / "4,748 RPM"), an optional location sub-line ("CPU Tctl" / "Left fan") and a status chip.
/// </summary>
public partial class DeviceCapabilitiesRuntimeStatusItemModel : ObservableObject
{
    public DeviceCapabilitiesRuntimeStatusItemModel(string name, string status, DeviceCapabilitiesStatusTone statusTone)
    {
        Name = name;
        Status = status;
        StatusTone = statusTone;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(StatusIconKind))]
    public partial DeviceCapabilitiesStatusTone StatusTone { get; set; }

    /// <summary>Tile glyph (thermometer / fan / battery).</summary>
    [ObservableProperty]
    public partial MaterialIconKind IconKind { get; set; } = MaterialIconKind.InformationOutline;

    /// <summary>Large live value, e.g. "54 °C" or "4,748 RPM"; empty hides the line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueVisibility))]
    public partial string ValueDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush ValueBrush { get; set; } = AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusErrorColor);

    /// <summary>Physical location sub-line, e.g. "CPU Tctl" or "Left fan"; null hides the line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationVisibility))]
    public partial string? Location { get; set; }

    public Visibility ValueVisibility => string.IsNullOrEmpty(ValueDisplay) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LocationVisibility => string.IsNullOrEmpty(Location) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Extra icon+text lines below the status (battery health / cycles / chemistry); rebuilt on refresh.</summary>
    public System.Collections.ObjectModel.ObservableCollection<DeviceCapabilitiesTileLineModel> DetailLines { get; } = [];

    /// <summary>Overrides the tone-derived status glyph (e.g. a battery-arrow icon for the discharging line).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIconKind))]
    public partial MaterialIconKind? StatusIconOverride { get; set; }

    public Brush StatusBrush => StatusTone switch
    {
        DeviceCapabilitiesStatusTone.Success => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        DeviceCapabilitiesStatusTone.Warning => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        DeviceCapabilitiesStatusTone.Error => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
        _ => AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusErrorColor),
    };

    public MaterialIconKind StatusIconKind => StatusIconOverride ?? StatusTone switch
    {
        DeviceCapabilitiesStatusTone.Success => MaterialIconKind.CheckCircle,
        DeviceCapabilitiesStatusTone.Warning => MaterialIconKind.AlertCircle,
        DeviceCapabilitiesStatusTone.Error => MaterialIconKind.CloseCircle,
        _ => MaterialIconKind.HelpCircleOutline,
    };
}
