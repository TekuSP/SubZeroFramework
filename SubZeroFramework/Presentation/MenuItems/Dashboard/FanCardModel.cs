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
    public partial FanTelemetrySnapshot Snapshot { get; set; } = default!;

    [ObservableProperty]
    public partial DateTimePoint[] FanSpeedHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] Separators { get; set; } = [];
    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    partial void OnFanSpeedHistoryChanged(DateTimePoint[] value)
    {
        Separators = GetSeparators(value.LastOrDefault());
    }
    private double[] GetSeparators(DateTimePoint? lastPoint)
    {
        if (lastPoint is null)
            return [];

        var now = lastPoint.DateTime;

        return
        [
            now.AddSeconds(-25).Ticks,
            now.AddSeconds(-20).Ticks,
            now.AddSeconds(-15).Ticks,
            now.AddSeconds(-10).Ticks,
            now.AddSeconds(-5).Ticks,
            now.Ticks
        ];
    }

    public static string Formatter(DateTime date)
    {
        var secsAgo = (DateTime.Now - date).TotalSeconds;

        return secsAgo < 1
            ? "now"
            : $"{secsAgo:N0}s ago";
    }
}
