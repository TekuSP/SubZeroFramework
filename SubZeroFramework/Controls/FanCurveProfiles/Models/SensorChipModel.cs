using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// Toggleable temperature sensor chip used by the Custom curve sensor selector. Only sensors in the
/// <see cref="FrameworkTemperatureState.Ok"/> state are selectable; unusable sensors are shown disabled
/// with a state label (Error / Not powered / Not calibrated) so the user understands why.
/// </summary>
public partial class SensorChipModel : ObservableObject
{
    public SensorChipModel(int sensorIndex, string displayName)
    {
        SensorIndex = sensorIndex;
        DisplayName = displayName;
    }

    public int SensorIndex { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemperatureDisplay))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    public partial double? CurrentTemperatureCelsius { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    [NotifyPropertyChangedFor(nameof(StateIcon))]
    public partial FrameworkTemperatureState State { get; set; } = FrameworkTemperatureState.Ok;

    /// <summary>Only an OK sensor can be selected to drive a curve.</summary>
    public bool IsUsable => State == FrameworkTemperatureState.Ok;

    /// <summary>Current reading shown under the chip name (e.g. "69°C", or "—" when unread).</summary>
    public string TemperatureDisplay => CurrentTemperatureCelsius is double t
        ? $"{t:0}°C"
        : "—";

    /// <summary>Second line under the name: the reading when OK, otherwise the unusable-state reason.</summary>
    public string SubLabel => State switch
    {
        FrameworkTemperatureState.NotPowered => "Not powered",
        FrameworkTemperatureState.NotCalibrated => "Not calibrated",
        FrameworkTemperatureState.Error => "Error",
        FrameworkTemperatureState.NotPresent => "Not present",
        _ => TemperatureDisplay,
    };

    /// <summary>Leading glyph — a thermometer when OK, a state-specific glyph otherwise.</summary>
    public MaterialIconKind StateIcon => State switch
    {
        FrameworkTemperatureState.NotPowered => MaterialIconKind.PowerPlugOffOutline,
        FrameworkTemperatureState.NotCalibrated => MaterialIconKind.Wrench,
        FrameworkTemperatureState.Error => MaterialIconKind.AlertCircleOutline,
        _ => MaterialIconKind.Thermometer,
    };
}
