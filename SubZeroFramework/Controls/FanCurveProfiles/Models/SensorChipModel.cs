using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// Toggleable temperature sensor chip used by the Custom curve sensor selector. Only sensors in the
/// <see cref="FrameworkTemperatureState.Ok"/> state are selectable; unusable sensors are shown disabled
/// with a state label (Error / Not powered / Not calibrated) so the user understands why.
/// </summary>
public partial class SensorChipModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public SensorChipModel(int sensorIndex, string displayName, IUnitFormattingService unitFormattingService)
    {
        SensorIndex = sensorIndex;
        DisplayName = displayName;
        _unitFormattingService = unitFormattingService;
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
    [NotifyPropertyChangedFor(nameof(ChipOpacity))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    [NotifyPropertyChangedFor(nameof(StateIcon))]
    public partial FrameworkTemperatureState State { get; set; } = FrameworkTemperatureState.Ok;

    /// <summary>
    /// Whether the sensor is currently reporting a reading. NOT a gate on selecting it: a sensor may be
    /// chosen while it is dark (a GPU sensor picked with the machine idle), and one that goes dark stays
    /// chosen — the selection is the user's, availability is just its live status.
    /// </summary>
    public bool IsUsable => State == FrameworkTemperatureState.Ok;

    /// <summary>Dims a chip that has no reading, so it still reads as unavailable while staying selectable.</summary>
    public double ChipOpacity => IsUsable ? 1d : 0.55d;

    /// <summary>Current reading shown under the chip name (e.g. "69°C", or "—" when unread), in the user's display unit.</summary>
    public string TemperatureDisplay => CurrentTemperatureCelsius is double t
        ? _unitFormattingService.FormatTemperature(t)
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
        // Reads the same as "not powered" to the user — the thing it measures is off, so it reports nothing.
        FrameworkTemperatureState.NotPresent => MaterialIconKind.PowerPlugOffOutline,
        _ => MaterialIconKind.Thermometer,
    };
}
