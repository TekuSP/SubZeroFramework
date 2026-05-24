using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Presentation.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Power.Models;

public partial class PowerCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public PowerCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryTitle))]
    [NotifyPropertyChangedFor(nameof(ChargePercentValue))]
    [NotifyPropertyChangedFor(nameof(ChargePercentDisplay))]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    [NotifyPropertyChangedFor(nameof(ChargeAndStateDisplay))]
    [NotifyPropertyChangedFor(nameof(RateDisplay))]
    [NotifyPropertyChangedFor(nameof(VoltageDisplay))]
    [NotifyPropertyChangedFor(nameof(CyclesDisplay))]
    [NotifyPropertyChangedFor(nameof(PowerDisplay))]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    [NotifyPropertyChangedFor(nameof(PowerBrush))]
    [NotifyPropertyChangedFor(nameof(ShowBatteryState))]
    [NotifyPropertyChangedFor(nameof(RateKind))]
    [NotifyPropertyChangedFor(nameof(CurrentHistoryTitle))]
    [NotifyPropertyChangedFor(nameof(DesignVoltage))]
    [NotifyPropertyChangedFor(nameof(VoltageLabel))]
    [NotifyPropertyChangedFor(nameof(DesignVoltageLabel))]
    [NotifyPropertyChangedFor(nameof(DesignCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(DesignCapacityLabel))]
    [NotifyPropertyChangedFor(nameof(LastFullChargeCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(LastFullChargeCapacityLabel))]
    [NotifyPropertyChangedFor(nameof(RemainingCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(RemainingCapacityLabel))]
    [NotifyPropertyChangedFor(nameof(BatteryLife))]
    public partial BatteryTelemetrySnapshot BatterySnapshot { get; set; } = default!;

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public Func<double, string> ChargeLabelFormatter => _unitFormattingService.FormatRatioAxisLabel;

    public Func<double, string> CurrentLabelFormatter => _unitFormattingService.FormatCurrentAxisLabel;

    public Func<double, string> VoltageLabelFormatter => _unitFormattingService.FormatVoltageAxisLabel;

    public string BatteryTitle
    {
        get
        {
            var primaryName = string.IsNullOrWhiteSpace(BatterySnapshot.ModelNumber)
                ? string.IsNullOrWhiteSpace(BatterySnapshot.DisplayName)
                    ? $"Battery {BatterySnapshot.BatteryIndex}"
                    : BatterySnapshot.DisplayName
                : BatterySnapshot.ModelNumber;

            if (!string.IsNullOrWhiteSpace(BatterySnapshot.Manufacturer))
            {
                return $"Battery {BatterySnapshot.BatteryIndex} ({BatterySnapshot.Manufacturer} {primaryName})";
            }

            return $"Battery {BatterySnapshot.BatteryIndex} ({primaryName})";
        }
    }

    public double ChargePercentValue => Math.Clamp(BatterySnapshot.ChargePercent ?? 0d, 0d, 100d);

    public string ChargePercentDisplay => _unitFormattingService.FormatRatio(BatterySnapshot.ChargePercent, decimals: 0);

    public string StateDisplay => ShowBatteryState ? (BatterySnapshot.BatteryState?.ToString() ?? "Unknown") : "--";

    public string PowerDisplay => BatterySnapshot.PowerSourceState switch
    {
        FrameworkPowerSourceState.None => "Unknown",
        FrameworkPowerSourceState.AcAndBattery => "AC and Battery",
        FrameworkPowerSourceState.AcOnly => "AC Only",
        FrameworkPowerSourceState.BatteryOnly => "Battery only",
        _ => "Unknown"
    };

    public string ChargeAndStateDisplay => $"{ChargePercentDisplay} ({StateDisplay})";

    public string RateDisplay => _unitFormattingService.FormatCurrent(BatterySnapshot.Amperage);

    public string RateKind => BatterySnapshot.BatteryState == FrameworkBatteryState.Charging
        ? $"Charging current ({_unitFormattingService.CurrentUnitSuffix})"
        : $"Discharging current ({_unitFormattingService.CurrentUnitSuffix})";

    public string CurrentHistoryTitle => RateKind;

    public string VoltageDisplay => _unitFormattingService.FormatVoltage(BatterySnapshot.Voltage);

    public string VoltageLabel => $"Battery Voltage ({_unitFormattingService.VoltageUnitSuffix})";

    public string CyclesDisplay => BatterySnapshot.CycleCount is uint cycles
        ? cycles.ToString()
        : "--";

    public string RemainingCapacityAmpereHours => _unitFormattingService.FormatChargeCapacity(BatterySnapshot.RemainingCapacityAmpereHours);

    public string RemainingCapacityLabel => $"Battery Remaining Capacity ({_unitFormattingService.ChargeCapacityUnitSuffix})";

    public string DesignCapacityAmpereHours => _unitFormattingService.FormatChargeCapacity(BatterySnapshot.DesignCapacityAmpereHours);

    public string DesignCapacityLabel => $"Battery Design Capacity ({_unitFormattingService.ChargeCapacityUnitSuffix})";

    public string LastFullChargeCapacityAmpereHours => _unitFormattingService.FormatChargeCapacity(BatterySnapshot.LastFullChargeCapacityAmpereHours);

    public string LastFullChargeCapacityLabel => $"Battery Last Full Charge ({_unitFormattingService.ChargeCapacityUnitSuffix})";

    public string DesignVoltage => _unitFormattingService.FormatVoltage(BatterySnapshot.DesignVoltageVolts);

    public string DesignVoltageLabel => $"Nominal Voltage ({_unitFormattingService.VoltageUnitSuffix})";

    public string BatteryLife => FormatBatteryHealth(
        BatterySnapshot.LastFullChargeCapacityAmpereHours,
        BatterySnapshot.DesignCapacityAmpereHours);

    public string ChargeHistoryTitle => string.Equals(_unitFormattingService.RatioUnitSuffix, "%", StringComparison.Ordinal)
        ? "Charge (%)"
        : "Charge (ratio)";

    public string VoltageHistoryTitle => VoltageLabel;

    public Brush StateBrush => GetStateBrush(BatterySnapshot.BatteryState);

    public Brush PowerBrush => GetPowerBrush(BatterySnapshot.PowerSourceState);

    public bool ShowBatteryState => BatterySnapshot.PowerSourceState == FrameworkPowerSourceState.BatteryOnly || !(BatterySnapshot.PowerSourceState == FrameworkPowerSourceState.AcAndBattery && BatterySnapshot.BatteryState == FrameworkBatteryState.Discharging);

    public ObservableCollection<DateTimePoint> ChargeHistory { get; } = [];

    public ObservableCollection<DateTimePoint> ChargeOverviewHistory { get; } = [];

    public ObservableCollection<DateTimePoint> CurrentHistory { get; } = [];

    public ObservableCollection<DateTimePoint> CurrentOverviewHistory { get; } = [];

    public ObservableCollection<DateTimePoint> VoltageHistory { get; } = [];

    public ObservableCollection<DateTimePoint> VoltageOverviewHistory { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeHistory))]
    [NotifyPropertyChangedFor(nameof(ChargeOverviewHistory))]
    [NotifyPropertyChangedFor(nameof(CurrentHistory))]
    [NotifyPropertyChangedFor(nameof(CurrentOverviewHistory))]
    [NotifyPropertyChangedFor(nameof(VoltageHistory))]
    [NotifyPropertyChangedFor(nameof(VoltageOverviewHistory))]
    public partial int HistoryRevision { get; set; }

    [ObservableProperty]
    public partial double[] ChargeSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double[] CurrentSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double[] VoltageSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double ChargeMin { get; set; }

    [ObservableProperty]
    public partial double ChargeMax { get; set; }

    [ObservableProperty]
    public partial double CurrentMin { get; set; }

    [ObservableProperty]
    public partial double CurrentMax { get; set; }

    [ObservableProperty]
    public partial double VoltageMin { get; set; }

    [ObservableProperty]
    public partial double VoltageMax { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargePercentDisplay))]
    [NotifyPropertyChangedFor(nameof(ChargeAndStateDisplay))]
    [NotifyPropertyChangedFor(nameof(RateDisplay))]
    [NotifyPropertyChangedFor(nameof(RateKind))]
    [NotifyPropertyChangedFor(nameof(CurrentHistoryTitle))]
    [NotifyPropertyChangedFor(nameof(VoltageDisplay))]
    [NotifyPropertyChangedFor(nameof(VoltageLabel))]
    [NotifyPropertyChangedFor(nameof(DesignVoltage))]
    [NotifyPropertyChangedFor(nameof(DesignVoltageLabel))]
    [NotifyPropertyChangedFor(nameof(DesignCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(DesignCapacityLabel))]
    [NotifyPropertyChangedFor(nameof(RemainingCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(RemainingCapacityLabel))]
    [NotifyPropertyChangedFor(nameof(LastFullChargeCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(LastFullChargeCapacityLabel))]
    [NotifyPropertyChangedFor(nameof(BatteryLife))]
    [NotifyPropertyChangedFor(nameof(ChargeHistoryTitle))]
    [NotifyPropertyChangedFor(nameof(VoltageHistoryTitle))]
    [NotifyPropertyChangedFor(nameof(ChargeLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(CurrentLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(VoltageLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(ChargeAxisMaxLimit))]
    [NotifyPropertyChangedFor(nameof(VoltageAxisMinLimit))]
    [NotifyPropertyChangedFor(nameof(VoltageAxisMaxLimit))]
    private partial int UnitFormattingRevision { get; set; }

    public double ChargeAxisMaxLimit => _unitFormattingService.RatioAxisMaximum;

    public double VoltageAxisMinLimit => _unitFormattingService.ConvertVoltage(10d);

    public double VoltageAxisMaxLimit => _unitFormattingService.ConvertVoltage(18d);

    public void UpdateMetricHistory(TelemetryMetric metric, IReadOnlyList<DateTimePoint> overviewHistory, IReadOnlyList<DateTimePoint> cardHistory)
    {
        switch (metric)
        {
            case TelemetryMetric.BatteryChargePercent:
                SynchronizePoints(ChargeOverviewHistory, overviewHistory);
                SynchronizePoints(ChargeHistory, cardHistory);
                UpdateChargeHistory();
                HistoryRevision++;
                break;
            case TelemetryMetric.BatteryPresentRateAmperes:
                SynchronizePoints(CurrentOverviewHistory, overviewHistory);
                SynchronizePoints(CurrentHistory, cardHistory);
                UpdateCurrentHistory();
                HistoryRevision++;
                break;
            case TelemetryMetric.BatteryPresentVoltageVolts:
                SynchronizePoints(VoltageOverviewHistory, overviewHistory);
                SynchronizePoints(VoltageHistory, cardHistory);
                UpdateVoltageHistory();
                HistoryRevision++;
                break;
        }
    }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
    }

    private void UpdateChargeHistory()
    {
        var (axisStart, axisEnd, separators) = BuildOverviewHistoryAxis(ChargeOverviewHistory);

        ChargeMin = axisStart.Ticks;
        ChargeMax = axisEnd.Ticks;
        ChargeSeparators = separators;
    }

    private void UpdateCurrentHistory()
    {
        var (axisStart, axisEnd, separators) = BuildOverviewHistoryAxis(CurrentOverviewHistory);

        CurrentMin = axisStart.Ticks;
        CurrentMax = axisEnd.Ticks;
        CurrentSeparators = separators;
    }

    private void UpdateVoltageHistory()
    {
        var (axisStart, axisEnd, separators) = BuildOverviewHistoryAxis(VoltageOverviewHistory);

        VoltageMin = axisStart.Ticks;
        VoltageMax = axisEnd.Ticks;
        VoltageSeparators = separators;
    }

    private static (DateTime AxisStart, DateTime AxisEnd, double[] Separators) BuildOverviewHistoryAxis(IEnumerable<DateTimePoint> historyPoints)
    {
        return TimeChartAxisHelper.BuildAxis(
            [.. historyPoints.Select(point => point.DateTime).OrderBy(point => point)],
            TelemetryHistoryLimits.MaximumHistoryWindow,
            PresentationDefaults.StandardTelemetryHistorySeparatorStep);
    }

    public static string Formatter(DateTime date)
    {
        var elapsed = DateTime.Now - date;

        if (elapsed.TotalSeconds < 1d)
        {
            return "now";
        }

        if (elapsed.TotalMinutes < 1d)
        {
            return $"{elapsed.TotalSeconds:N0}s";
        }

        if (elapsed.TotalHours < 1d)
        {
            return $"{elapsed.TotalMinutes:N0}m";
        }

        var hours = (int)Math.Floor(elapsed.TotalHours);
        var minutes = (int)Math.Round(elapsed.TotalMinutes - (hours * 60d), MidpointRounding.AwayFromZero);

        if (minutes == 60)
        {
            hours++;
            minutes = 0;
        }

        return minutes == 0
            ? $"{hours}h"
            : $"{hours}h {minutes}m";
    }

    public void ClearMetricHistory(TelemetryMetric metric)
    {
        UpdateMetricHistory(metric, [], []);
    }

    private static void SynchronizePoints(ObservableCollection<DateTimePoint> target, IReadOnlyList<DateTimePoint> source)
    {
        var commonCount = Math.Min(target.Count, source.Count);

        for (var index = 0; index < commonCount; index++)
        {
            var current = target[index];
            var next = source[index];

            if (current.DateTime != next.DateTime || current.Value != next.Value)
            {
                target[index] = next;
            }
        }

        for (var index = target.Count - 1; index >= source.Count; index--)
        {
            target.RemoveAt(index);
        }

        for (var index = commonCount; index < source.Count; index++)
        {
            target.Add(source[index]);
        }
    }

    private static Brush GetStateBrush(FrameworkBatteryState? state)
    {
        return state switch
        {
            FrameworkBatteryState.Charging => AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.StatusSuccessColor),
            FrameworkBatteryState.Discharging => AppThemeBrushes.Get("BrandSecondaryBrush", AppThemeBrushes.StatusWarningColor),
            FrameworkBatteryState.Critical => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
            FrameworkBatteryState.NotPresent => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
            _ => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
        };
    }

    private static Brush GetPowerBrush(FrameworkPowerSourceState? powerSourceState)
    {
        return powerSourceState switch
        {
            FrameworkPowerSourceState.None => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
            FrameworkPowerSourceState.AcAndBattery => AppThemeBrushes.Get("BrandSecondaryBrush", AppThemeBrushes.StatusWarningColor),
            FrameworkPowerSourceState.AcOnly => AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.StatusSuccessColor),
            FrameworkPowerSourceState.BatteryOnly => AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.StatusSuccessColor),
            _ => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
        };
    }

    private string FormatBatteryHealth(double? lastFullCapacityAmpereHours, double? designCapacityAmpereHours)
    {
        if (lastFullCapacityAmpereHours is not double lastFullCapacity
            || designCapacityAmpereHours is not double designCapacity
            || lastFullCapacity < 0d
            || designCapacity <= 0d)
        {
            return "--";
        }

        var healthPercent = Math.Clamp(lastFullCapacity / designCapacity * 100d, 0d, 100d);
        return _unitFormattingService.FormatRatio(
            healthPercent,
            decimals: string.Equals(_unitFormattingService.RatioUnitSuffix, "%", StringComparison.Ordinal) ? 1 : 2);
    }
}
