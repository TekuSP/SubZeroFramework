using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// One entry in the custom (left-aligned) legend under the driving-temperature chart: a coloured line
/// swatch, the series name, and its current value (e.g. "Driving (Maximum)" + "72°C").
/// </summary>
public sealed class SensorLegendItem
{
    public required string Name { get; init; }

    public required Brush Swatch { get; init; }

    public required string ValueDisplay { get; init; }
}
