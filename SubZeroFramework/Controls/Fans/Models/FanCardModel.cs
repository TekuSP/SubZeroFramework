using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using Microsoft.UI.Xaml;

using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FanCardModel : ObservableObject
{
    private const double DefaultMaximumFanSpeedRpm = 7500d;
    private const double FanSpeedHistoryAxisHeadroomMultiplier = 1.1d;
    private readonly IUnitFormattingService _unitFormattingService;

    public FanCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanSpeedGaugeValues))]
    [NotifyPropertyChangedFor(nameof(FanSpeedRemainingGaugeValues))]
    [NotifyPropertyChangedFor(nameof(FanSpeedValueDisplay))]
    [NotifyPropertyChangedFor(nameof(FanSpeedUnitSuffix))]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedAxisLimit))]
    [NotifyPropertyChangedFor(nameof(FanSpeedHistoryAxisMaxLimit))]
    public partial FanTelemetrySnapshot Snapshot { get; set; } = default!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedRpm))]
    [NotifyPropertyChangedFor(nameof(FanSpeedGaugeValues))]
    [NotifyPropertyChangedFor(nameof(FanSpeedRemainingGaugeValues))]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedAxisLimit))]
    [NotifyPropertyChangedFor(nameof(FanSpeedHistoryAxisMaxLimit))]
    public partial FanCapabilityState? Capability { get; set; }

    [ObservableProperty]
    public partial FanControlStateSnapshot? ControlState { get; set; }

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetrySnapshot> DrivingSensors { get; set; } = [];

    [ObservableProperty]
    public partial FanStateSnapshot? FanState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanSpeedHistoryAxisMaxLimit))]
    public partial DateTimePoint[] FanSpeedHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] Separators { get; set; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Status: Checking";

    [ObservableProperty]
    public partial Brush StatusBrush { get; set; } = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetModeDisplay))]
    public partial string TargetMode { get; set; } = "Auto";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureDisplay))]
    public partial string DrivingTemperature { get; set; } = "--";

    [ObservableProperty]
    public partial string OverrideStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush OverrideStateBrush { get; set; } = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);

    [ObservableProperty]
    public partial Visibility OverrideStateVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanSpeedGaugeValues))]
    [NotifyPropertyChangedFor(nameof(FanSpeedRemainingGaugeValues))]
    [NotifyPropertyChangedFor(nameof(FanSpeedValueDisplay))]
    [NotifyPropertyChangedFor(nameof(FanSpeedUnitSuffix))]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedAxisLimit))]
    [NotifyPropertyChangedFor(nameof(FanSpeedHistoryAxisMaxLimit))]
    [NotifyPropertyChangedFor(nameof(FanSpeedLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureDisplay))]
    private partial int UnitFormattingRevision { get; set; }

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public double MaximumFanSpeedRpm => Capability is { MaximumSpeedRpm: > 0 } capability
        ? capability.MaximumSpeedRpm
        : DefaultMaximumFanSpeedRpm;

    public double MaximumFanSpeedAxisLimit => _unitFormattingService.ConvertFanSpeed(MaximumFanSpeedRpm);

    public double FanSpeedHistoryAxisMaxLimit => Math.Max(MaximumFanSpeedAxisLimit, GetMaximumObservedFanSpeed()) * FanSpeedHistoryAxisHeadroomMultiplier;

    public double[] FanSpeedGaugeValues => [Math.Clamp(_unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm), 0d, MaximumFanSpeedAxisLimit)];

    public double[] FanSpeedRemainingGaugeValues => [Math.Max(0d, MaximumFanSpeedAxisLimit - Math.Clamp(_unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm), 0d, MaximumFanSpeedAxisLimit))];

    public string FanSpeedValueDisplay => _unitFormattingService.FormatFanSpeedValue(Snapshot.SpeedRpm);

    public string FanSpeedUnitSuffix => _unitFormattingService.FanSpeedUnitSuffix;

    public Func<double, string> FanSpeedLabelFormatter => _unitFormattingService.FormatFanSpeedAxisLabel;

    public string TargetModeDisplay => $"Mode: {TargetMode}";

    public string DrivingTemperatureDisplay => $"Driving Temp: {DrivingTemperature}";

    public void RefreshUnitFormatting()
    {
        UpdateControlStatePresentation();
        UnitFormattingRevision++;
    }

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

        return TimeChartAxisHelper.BuildSeparators(
            now - PresentationDefaults.RecentTelemetryHistoryWindow,
            now,
            PresentationDefaults.RecentTelemetrySeparatorStep);
    }

    private double GetMaximumObservedFanSpeed()
    {
        var maximum = Snapshot is null ? 0d : _unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm);

        foreach (var point in FanSpeedHistory)
        {
            if (point.Value is double value)
            {
                maximum = Math.Max(maximum, value);
            }
        }

        return maximum;
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
            DrivingTemperature = Capability?.SupportsThermalReporting == false ? "n/a" : _unitFormattingService.FormatTemperature(null);
            UpdateOverrideStatePresentation();
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
            UpdateOverrideStatePresentation();
            return;
        }

        if (ControlState.Mode != FanControlMode.CustomCurve)
        {
            DrivingTemperature = _unitFormattingService.FormatTemperature(null);
            UpdateOverrideStatePresentation();
            return;
        }

        DrivingTemperature = FormatDrivingTemperature(
            ControlState.DrivingTemperatureAggregation,
            ControlState.DrivingSensorIndices,
            DrivingSensors);
        UpdateOverrideStatePresentation();
    }

    private void UpdateOverrideStatePresentation()
    {
        if (ControlState?.LastAutoRestoreAttemptFailed == true)
        {
            OverrideStateText = "Auto restore failed";
            OverrideStateBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
            OverrideStateVisibility = Visibility.Visible;
            return;
        }

        if (ControlState?.HasActiveOverride == true)
        {
            OverrideStateText = ControlState.Mode == FanControlMode.CustomCurve
                ? "Curve override active"
                : "Manual override active";
            OverrideStateBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
            OverrideStateVisibility = Visibility.Visible;
            return;
        }

        OverrideStateText = string.Empty;
        OverrideStateBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
        OverrideStateVisibility = Visibility.Collapsed;
    }

    private void UpdateFanStatePresentation()
    {
        if (FanState is null)
        {
            StatusText = Snapshot.IsAvailable ? "Status: Checking" : "Status: Unavailable";
            StatusBrush = Snapshot.IsAvailable
                ? AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor)
                : AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
            return;
        }

        if (!FanState.IsAvailable)
        {
            StatusText = "Status: Unavailable";
            StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
            return;
        }

        switch (FanState.FanState)
        {
            case FrameworkFanState.Ok:
                StatusText = "Status: OK";
                StatusBrush = AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor);
                break;
            case FrameworkFanState.Stalled:
                StatusText = "Status: Stalled";
                StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
                break;
            case FrameworkFanState.NotPresent:
                StatusText = "Status: Not Present";
                StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
                break;
            default:
                StatusText = "Status: Unknown";
                StatusBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
                break;
        }
    }

    private string FormatDrivingTemperature(TemperatureAggregationMode aggregationMode, ImmutableArray<int> sensorIndices, ImmutableArray<TemperatureTelemetrySnapshot> drivingSensors)
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
            ? _unitFormattingService.FormatTemperature(null)
            : _unitFormattingService.FormatTemperature(ComputeAggregateTemperature(aggregationMode, temperatures), decimals: 0);

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
