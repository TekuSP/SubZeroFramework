using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Extensions;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SubZeroFramework.Models;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class FanCardModel : ObservableObject
{
    [ObservableProperty]
    public partial FanTelemetrySnapshot Snapshot { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ISeries> SparklineSeries { get; set; } = [];
}
