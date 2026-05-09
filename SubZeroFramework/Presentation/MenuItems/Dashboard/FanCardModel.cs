using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using Microsoft.UI;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class FanCardModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanSpeedGaugeValues))]
    [NotifyPropertyChangedFor(nameof(FanSpeedRemainingGaugeValues))]
    public partial FanTelemetrySnapshot Snapshot { get; set; } = default!;

    private const double MaximumFanSpeedRpm = 8000d;

    [ObservableProperty]
    public partial FanCapabilityState? Capability { get; set; }

    [ObservableProperty]
    public partial FanControlStateSnapshot? ControlState { get; set; }

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetrySnapshot> DrivingSensors { get; set; } = [];

    [ObservableProperty]
    public partial FanStateSnapshot? FanState { get; set; }

    [ObservableProperty]
    public partial DateTimePoint[] FanSpeedHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] Separators { get; set; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Status: Checking";

    [ObservableProperty]
    public partial Brush StatusBrush { get; set; } = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetModeDisplay))]
    public partial string TargetMode { get; set; } = "Auto";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureDisplay))]
    public partial string DrivingTemperature { get; set; } = "--°C";

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public double[] FanSpeedGaugeValues => [Math.Clamp((double)Snapshot.SpeedRpm, 0d, MaximumFanSpeedRpm)];

    public double[] FanSpeedRemainingGaugeValues => [Math.Max(0d, MaximumFanSpeedRpm - Math.Clamp((double)Snapshot.SpeedRpm, 0d, MaximumFanSpeedRpm))];

    public string TargetModeDisplay => $"Mode: {TargetMode}";

    public string DrivingTemperatureDisplay => $"Driving Temp: {DrivingTemperature}";

    partial void OnFanSpeedHistoryChanged(DateTimePoint[] value)
    {
        Separators = GetSeparators();
    }

    partial void OnCapabilityChanged(FanCapabilityState? value)
    {
        UpdateCapabilityPresentation();
    }

    partial void OnControlStateChanged(FanControlStateSnapshot? value)
    {
        UpdateControlStatePresentation();
    }

    partial void OnDrivingSensorsChanged(ImmutableArray<TemperatureTelemetrySnapshot> value)
    {
        UpdateControlStatePresentation();
    }

    partial void OnFanStateChanged(FanStateSnapshot? value)
    {
        UpdateFanStatePresentation();
    }

    private double[] GetSeparators()
    {
        var now = DateTime.Now;

        return
        [
            now.AddSeconds(-30).Ticks,
            now.AddSeconds(-25).Ticks,
            now.AddSeconds(-20).Ticks,
            now.AddSeconds(-15).Ticks,
            now.AddSeconds(-10).Ticks,
            now.AddSeconds(-5).Ticks,
            now.Ticks
        ];
    }

    private void UpdateCapabilityPresentation()
    {
        UpdateControlStatePresentation();
        UpdateFanStatePresentation();
    }

    private void UpdateControlStatePresentation()
    {
        if (ControlState is null || !ControlState.IsAvailable)
        {
            TargetMode = "Auto";
            DrivingTemperature = Capability?.SupportsThermalReporting == false ? "n/a" : "--°C";
            return;
        }

        TargetMode = ControlState.Mode switch
        {
            FanControlMode.Auto => "Auto",
            FanControlMode.Manual => "Manual",
            FanControlMode.CustomCurve => "Curve",
            _ => "Auto",
        };

        if (Capability?.SupportsThermalReporting == false)
        {
            DrivingTemperature = "n/a";
            return;
        }

        if (ControlState.Mode != FanControlMode.CustomCurve)
        {
            DrivingTemperature = "--°C";
            return;
        }

        DrivingTemperature = FormatDrivingTemperature(
            ControlState.DrivingTemperatureAggregation,
            ControlState.DrivingSensorIndices,
            DrivingSensors);
    }

    private void UpdateFanStatePresentation()
    {
        if (FanState is null)
        {
            StatusText = Snapshot.IsAvailable ? "Status: Checking" : "Status: Unavailable";
            StatusBrush = Snapshot.IsAvailable
                ? GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78))
                : GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
            return;
        }

        if (!FanState.IsAvailable)
        {
            StatusText = "Status: Unavailable";
            StatusBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
            return;
        }

        switch (FanState.FanState)
        {
            case FrameworkFanState.Ok:
                StatusText = "Status: OK";
                StatusBrush = GetBrush("StatusSuccessBrush", ColorHelper.FromArgb(255, 108, 203, 95));
                break;
            case FrameworkFanState.Stalled:
                StatusText = "Status: Stalled";
                StatusBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
                break;
            case FrameworkFanState.NotPresent:
                StatusText = "Status: Not Present";
                StatusBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
                break;
            default:
                StatusText = "Status: Unknown";
                StatusBrush = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));
                break;
        }
    }

    private static Brush GetBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);
    }

    private static string FormatDrivingTemperature(TemperatureAggregationMode aggregationMode, ImmutableArray<int> sensorIndices, ImmutableArray<TemperatureTelemetrySnapshot> drivingSensors)
    {
        var aggregationLabel = aggregationMode switch
        {
            TemperatureAggregationMode.Average => "avg",
            TemperatureAggregationMode.Median => "median",
            TemperatureAggregationMode.Maximum => "max",
            TemperatureAggregationMode.Minimum => "min",
            _ => "avg",
        };

        var temperatures = drivingSensors
            .Where(sensor => sensor.IsAvailable
                && sensor.TemperatureCelsius is not null
                && (sensor.TemperatureState is null || sensor.TemperatureState == FrameworkTemperatureState.Ok))
            .Select(sensor => sensor.TemperatureCelsius!.Value)
            .OrderBy(value => value)
            .ToArray();

        var temperatureDisplay = temperatures.Length == 0
            ? "--°C"
            : $"{ComputeAggregateTemperature(aggregationMode, temperatures):N0}°C";

        if (sensorIndices.IsDefaultOrEmpty)
        {
            return $"{temperatureDisplay} {aggregationLabel}";
        }

        return $"{temperatureDisplay} {aggregationLabel} [{string.Join(",", sensorIndices)}]";
    }

    private static double ComputeAggregateTemperature(TemperatureAggregationMode aggregationMode, double[] orderedTemperatures)
    {
        return aggregationMode switch
        {
            TemperatureAggregationMode.Average => orderedTemperatures.Average(),
            TemperatureAggregationMode.Median => ComputeMedianTemperature(orderedTemperatures),
            TemperatureAggregationMode.Maximum => orderedTemperatures[^1],
            TemperatureAggregationMode.Minimum => orderedTemperatures[0],
            _ => orderedTemperatures.Average(),
        };
    }

    private static double ComputeMedianTemperature(double[] orderedTemperatures)
    {
        var midpoint = orderedTemperatures.Length / 2;

        return orderedTemperatures.Length % 2 == 0
            ? (orderedTemperatures[midpoint - 1] + orderedTemperatures[midpoint]) / 2d
            : orderedTemperatures[midpoint];
    }

    public static string Formatter(DateTime date)
    {
        var secsAgo = (DateTime.Now - date).TotalSeconds;

        return secsAgo < 1
            ? "now"
            : $"{secsAgo:N0}s";
    }
}
