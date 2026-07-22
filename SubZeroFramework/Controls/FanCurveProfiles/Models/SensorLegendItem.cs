using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// One entry in the custom (left-aligned) legend under the driving-temperature chart: a coloured line
/// swatch, the series name, and its current value (e.g. "Driving (Maximum)" + "72°C").
/// </summary>
/// <remarks>
/// <see cref="ValueDisplay"/> is observable and updated in place every poll (see
/// <c>FanSensorChartModel.RefreshLiveData</c>): the legend items keep their identity while only their
/// value text changes, so the row never churns. Name and swatch are fixed for the item's lifetime.
/// </remarks>
public sealed partial class SensorLegendItem : ObservableObject
{
    public required string Name { get; init; }

    public required Brush Swatch { get; init; }

    /// <summary>The current reading (e.g. "72°C"); refreshed live on every telemetry poll.</summary>
    [ObservableProperty]
    public required partial string ValueDisplay { get; set; }
}
