using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Power.Models;

public partial class PowerCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public PowerCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        ChargeLabelFormatter = CreateChargeLabelFormatter();
        CurrentLabelFormatter = CreateCurrentLabelFormatter();
        VoltageLabelFormatter = CreateVoltageLabelFormatter();
        // Axis limits depend only on the unit preference (no battery data), so they are safe to seed here.
        ChargeAxisMaxLimit = unitFormattingService.RatioAxisMaximum;
        VoltageAxisMinLimit = unitFormattingService.ConvertVoltage(10d);
        VoltageAxisMaxLimit = unitFormattingService.ConvertVoltage(18d);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryTitle))]
    [NotifyPropertyChangedFor(nameof(ChargePercentValue))]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    [NotifyPropertyChangedFor(nameof(CyclesDisplay))]
    [NotifyPropertyChangedFor(nameof(PowerDisplay))]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    [NotifyPropertyChangedFor(nameof(PowerBrush))]
    [NotifyPropertyChangedFor(nameof(ShowBatteryState))]
    public partial BatteryTelemetrySnapshot BatterySnapshot { get; set; } = default!;

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    // The axis formatters follow the unit preference; stored (fresh closure) so PropertyChanged fires on a
    // unit change and LiveCharts rebinds the labelers.
    [ObservableProperty]
    public partial Func<double, string> ChargeLabelFormatter { get; private set; }

    [ObservableProperty]
    public partial Func<double, string> CurrentLabelFormatter { get; private set; }

    [ObservableProperty]
    public partial Func<double, string> VoltageLabelFormatter { get; private set; }

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

    [ObservableProperty]
    public partial string ChargePercentDisplay { get; private set; } = "--";

    public string StateDisplay => ShowBatteryState ? (BatterySnapshot.BatteryState?.ToString() ?? "Unknown") : "--";

    public string PowerDisplay => BatterySnapshot.PowerSourceState switch
    {
        FrameworkPowerSourceState.None => "Unknown",
        FrameworkPowerSourceState.AcAndBattery => "AC and Battery",
        FrameworkPowerSourceState.AcOnly => "AC Only",
        FrameworkPowerSourceState.BatteryOnly => "Battery only",
        _ => "Unknown"
    };

    [ObservableProperty]
    public partial string ChargeAndStateDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RateDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RateKind { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentHistoryTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string VoltageDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string VoltageLabel { get; private set; } = string.Empty;

    public string CyclesDisplay => BatterySnapshot.CycleCount is uint cycles
        ? cycles.ToString()
        : "--";

    [ObservableProperty]
    public partial string RemainingCapacityAmpereHours { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RemainingCapacityLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DesignCapacityAmpereHours { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DesignCapacityLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastFullChargeCapacityAmpereHours { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastFullChargeCapacityLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DesignVoltage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DesignVoltageLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BatteryLife { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ChargeHistoryTitle { get; private set; } = "Charge (%)";

    [ObservableProperty]
    public partial string VoltageHistoryTitle { get; private set; } = string.Empty;

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
    public partial double ChargeAxisMaxLimit { get; private set; }

    [ObservableProperty]
    public partial double VoltageAxisMinLimit { get; private set; }

    [ObservableProperty]
    public partial double VoltageAxisMaxLimit { get; private set; }

    public void UpdateMetricHistory(TelemetryMetric metric, IReadOnlyList<DateTimePoint> overviewHistory, IReadOnlyList<DateTimePoint> cardHistory)
    {
        // The history collections are ObservableCollections bound as LiveCharts Values; mutating them in
        // place raises CollectionChanged, which drives the charts directly — no revision nudge needed.
        switch (metric)
        {
            case TelemetryMetric.BatteryChargePercent:
                SynchronizePoints(ChargeOverviewHistory, overviewHistory);
                SynchronizePoints(ChargeHistory, cardHistory);
                UpdateChargeHistory();
                break;
            case TelemetryMetric.BatteryPresentRateAmperes:
                SynchronizePoints(CurrentOverviewHistory, overviewHistory);
                SynchronizePoints(CurrentHistory, cardHistory);
                UpdateCurrentHistory();
                break;
            case TelemetryMetric.BatteryPresentVoltageVolts:
                SynchronizePoints(VoltageOverviewHistory, overviewHistory);
                SynchronizePoints(VoltageHistory, cardHistory);
                UpdateVoltageHistory();
                break;
        }
    }

    partial void OnBatterySnapshotChanged(BatteryTelemetrySnapshot value) => RefreshUnitFormattedDisplays();

    public void RefreshUnitFormatting()
    {
        // Fresh closures + unit-only axis limits follow the unit preference; the snapshot displays reformat
        // under it too.
        ChargeLabelFormatter = CreateChargeLabelFormatter();
        CurrentLabelFormatter = CreateCurrentLabelFormatter();
        VoltageLabelFormatter = CreateVoltageLabelFormatter();
        ChargeAxisMaxLimit = _unitFormattingService.RatioAxisMaximum;
        VoltageAxisMinLimit = _unitFormattingService.ConvertVoltage(10d);
        VoltageAxisMaxLimit = _unitFormattingService.ConvertVoltage(18d);
        RefreshUnitFormattedDisplays();
    }

    // Reassigns the unit-formatted battery displays (change when the snapshot or the unit preference does);
    // stored-property setters raise PropertyChanged only for values that actually changed.
    private void RefreshUnitFormattedDisplays()
    {
        if (BatterySnapshot is null)
        {
            return;
        }

        ChargePercentDisplay = _unitFormattingService.FormatRatio(BatterySnapshot.ChargePercent, decimals: 0);
        ChargeAndStateDisplay = $"{ChargePercentDisplay} ({StateDisplay})";
        RateDisplay = _unitFormattingService.FormatCurrent(BatterySnapshot.Amperage);
        RateKind = BatterySnapshot.BatteryState == FrameworkBatteryState.Charging
            ? $"Charging current ({_unitFormattingService.CurrentUnitSuffix})"
            : $"Discharging current ({_unitFormattingService.CurrentUnitSuffix})";
        CurrentHistoryTitle = RateKind;
        VoltageDisplay = _unitFormattingService.FormatVoltage(BatterySnapshot.Voltage);
        VoltageLabel = $"Battery Voltage ({_unitFormattingService.VoltageUnitSuffix})";
        VoltageHistoryTitle = VoltageLabel;
        RemainingCapacityAmpereHours = _unitFormattingService.FormatChargeCapacity(BatterySnapshot.RemainingCapacityAmpereHours);
        RemainingCapacityLabel = $"Battery Remaining Capacity ({_unitFormattingService.ChargeCapacityUnitSuffix})";
        DesignCapacityAmpereHours = _unitFormattingService.FormatChargeCapacity(BatterySnapshot.DesignCapacityAmpereHours);
        DesignCapacityLabel = $"Battery Design Capacity ({_unitFormattingService.ChargeCapacityUnitSuffix})";
        LastFullChargeCapacityAmpereHours = _unitFormattingService.FormatChargeCapacity(BatterySnapshot.LastFullChargeCapacityAmpereHours);
        LastFullChargeCapacityLabel = $"Battery Last Full Charge ({_unitFormattingService.ChargeCapacityUnitSuffix})";
        DesignVoltage = _unitFormattingService.FormatVoltage(BatterySnapshot.DesignVoltageVolts);
        DesignVoltageLabel = $"Nominal Voltage ({_unitFormattingService.VoltageUnitSuffix})";
        BatteryLife = FormatBatteryHealth(
            BatterySnapshot.LastFullChargeCapacityAmpereHours,
            BatterySnapshot.DesignCapacityAmpereHours);
        ChargeHistoryTitle = string.Equals(_unitFormattingService.RatioUnitSuffix, "%", StringComparison.Ordinal)
            ? "Charge (%)"
            : "Charge (ratio)";
    }

    // Fresh closures per call so the axis-formatter assignments never no-op (delegates over the same
    // method/target compare equal); capturing a local gives each a new target, so PropertyChanged fires.
    private Func<double, string> CreateChargeLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisLabel(value);
    }

    private Func<double, string> CreateCurrentLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatCurrentAxisLabel(value);
    }

    private Func<double, string> CreateVoltageLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatVoltageAxisLabel(value);
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
