using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class PowerCardModel : ObservableObject
{
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
    [NotifyPropertyChangedFor(nameof(DesignVoltage))]
    [NotifyPropertyChangedFor(nameof(DesignCapacityAmepereHours))]
    [NotifyPropertyChangedFor(nameof(LastFullChargeCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(RemainingCapacityAmpereHours))]
    [NotifyPropertyChangedFor(nameof(BatteryLife))]


    public partial BatteryTelemetrySnapshot BatterySnapshot { get; set; } = default!;

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public string BatteryTitle
    {
        get
        {
            var primaryName = string.IsNullOrWhiteSpace(BatterySnapshot.ModelNumber)
                ? string.IsNullOrWhiteSpace(BatterySnapshot.DisplayName)
                    ? $"Battery {BatterySnapshot.BatteryIndex + 1}"
                    : BatterySnapshot.DisplayName
                : BatterySnapshot.ModelNumber;

            if (!string.IsNullOrWhiteSpace(BatterySnapshot.Manufacturer))
            {
                return $"Battery {BatterySnapshot.BatteryIndex + 1} ({BatterySnapshot.Manufacturer} {primaryName})";
            }

            return $"Battery {BatterySnapshot.BatteryIndex + 1} ({primaryName})";
        }
    }

    public double ChargePercentValue => Math.Clamp(BatterySnapshot.ChargePercent ?? 0d, 0d, 100d);

    public string ChargePercentDisplay => BatterySnapshot.ChargePercent is double value
        ? $"{value:N0}%"
        : "--";

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

    public string RateDisplay => BatterySnapshot.Amperage is double value
        ? $"{value * 1000d:N0} mA"
        : "--";

    public string RateKind => BatterySnapshot.BatteryState == FrameworkBatteryState.Charging ? "Charging current (A)" : "Discharging current (A)";

    public string VoltageDisplay => BatterySnapshot.Voltage is double value
        ? $"{value:N1} V"
        : "--";

    public string CyclesDisplay => BatterySnapshot.CycleCount is uint cycles
        ? cycles.ToString()
        : "--";

    public string RemainingCapacityAmpereHours => BatterySnapshot.RemainingCapacityAmpereHours is double value ? $"{value:N1} Ah" : "--";
    public string DesignCapacityAmepereHours => BatterySnapshot.DesignCapacityAmpereHours is double value ? $"{value:N1} Ah" : "--";
    public string LastFullChargeCapacityAmpereHours => BatterySnapshot.LastFullChargeCapacityAmpereHours is double value ? $"{value:N1} Ah" : "--";
    public string DesignVoltage => BatterySnapshot.DesignVoltageVolts is double value ? $"{value:N1} V" : "--";

    public string BatteryLife => BatterySnapshot.LastFullChargeCapacityAmpereHours is double lastFullVal && BatterySnapshot.DesignCapacityAmpereHours is double designFullVal ? $"{(designFullVal / lastFullVal) * 100:N1} %" : "--";

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
    public partial double ChargeMin {  get; set; }

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

    public void UpdateMetricHistory(TelemetryMetric metric, IReadOnlyList<DateTimePoint> overviewHistory, IReadOnlyList<DateTimePoint> cardHistory)
    {
        switch (metric)
        {
            case TelemetryMetric.BatteryChargePercent:
                SynchronizePoints(ChargeOverviewHistory, overviewHistory);
                SynchronizePoints(ChargeHistory, cardHistory);
                UpdateChargeHistory();
                OnPropertyChanged(nameof(ChargeOverviewHistory));
                OnPropertyChanged(nameof(ChargeHistory));
                break;
            case TelemetryMetric.BatteryPresentRateAmperes:
                SynchronizePoints(CurrentOverviewHistory, overviewHistory);
                SynchronizePoints(CurrentHistory, cardHistory);
                UpdateCurrentHistory();
                OnPropertyChanged(nameof(CurrentOverviewHistory));
                OnPropertyChanged(nameof(CurrentHistory));
                break;
            case TelemetryMetric.BatteryPresentVoltageVolts:
                SynchronizePoints(VoltageOverviewHistory, overviewHistory);
                SynchronizePoints(VoltageHistory, cardHistory);
                UpdateVoltageHistory();
                OnPropertyChanged(nameof(VoltageOverviewHistory));
                OnPropertyChanged(nameof(VoltageHistory));
                break;
        }
    }

    private void UpdateChargeHistory()
    {
        var historyPoints = ChargeOverviewHistory
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var axisEnd = historyPoints.Length == 0
            ? DateTime.Now
            : historyPoints[^1] > DateTime.Now ? historyPoints[^1] : DateTime.Now;

        var earliestPoint = historyPoints.Length == 0
            ? axisEnd - TimeSpan.FromHours(1)
            : historyPoints[0];

        var axisStart = earliestPoint < axisEnd - TimeSpan.FromHours(1)
            ? axisEnd - TimeSpan.FromHours(1)
            : earliestPoint;

        var visibleSpan = axisEnd - axisStart;
        var separatorStep = GetHistorySeparatorStep(visibleSpan);

        ChargeMin = axisStart.Ticks;
        ChargeMax = axisEnd.Ticks;

        List<double> separators = [axisStart.Ticks];
        for (var tick = axisStart + separatorStep; tick < axisEnd; tick += separatorStep)
        {
            separators.Add(tick.Ticks);
        }

        if (separators.Count == 0 || separators[^1] != axisEnd.Ticks)
        {
            separators.Add(axisEnd.Ticks);
        }

        ChargeSeparators = [.. separators];
    }

    private void UpdateCurrentHistory()
    {
        var historyPoints = CurrentOverviewHistory
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var axisEnd = historyPoints.Length == 0
            ? DateTime.Now
            : historyPoints[^1] > DateTime.Now ? historyPoints[^1] : DateTime.Now;

        var earliestPoint = historyPoints.Length == 0
            ? axisEnd - TimeSpan.FromHours(1)
            : historyPoints[0];

        var axisStart = earliestPoint < axisEnd - TimeSpan.FromHours(1)
            ? axisEnd - TimeSpan.FromHours(1)
            : earliestPoint;

        var visibleSpan = axisEnd - axisStart;
        var separatorStep = GetHistorySeparatorStep(visibleSpan);

        CurrentMin = axisStart.Ticks;
        CurrentMax = axisEnd.Ticks;

        List<double> separators = [axisStart.Ticks];
        for (var tick = axisStart + separatorStep; tick < axisEnd; tick += separatorStep)
        {
            separators.Add(tick.Ticks);
        }

        if (separators.Count == 0 || separators[^1] != axisEnd.Ticks)
        {
            separators.Add(axisEnd.Ticks);
        }

        CurrentSeparators = [.. separators];
    }
    private void UpdateVoltageHistory()
    {
        var historyPoints = VoltageOverviewHistory
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var axisEnd = historyPoints.Length == 0
            ? DateTime.Now
            : historyPoints[^1] > DateTime.Now ? historyPoints[^1] : DateTime.Now;

        var earliestPoint = historyPoints.Length == 0
            ? axisEnd - TimeSpan.FromHours(1)
            : historyPoints[0];

        var axisStart = earliestPoint < axisEnd - TimeSpan.FromHours(1)
            ? axisEnd - TimeSpan.FromHours(1)
            : earliestPoint;

        var visibleSpan = axisEnd - axisStart;
        var separatorStep = GetHistorySeparatorStep(visibleSpan);

        VoltageMin = axisStart.Ticks;
        VoltageMax = axisEnd.Ticks;

        List<double> separators = [axisStart.Ticks];
        for (var tick = axisStart + separatorStep; tick < axisEnd; tick += separatorStep)
        {
            separators.Add(tick.Ticks);
        }

        if (separators.Count == 0 || separators[^1] != axisEnd.Ticks)
        {
            separators.Add(axisEnd.Ticks);
        }

        VoltageSeparators = [.. separators];
    }
    private static TimeSpan GetHistorySeparatorStep(TimeSpan visibleSpan)
    {
        if (visibleSpan <= TimeSpan.FromMinutes(1))
        {
            return TimeSpan.FromSeconds(5);
        }

        if (visibleSpan <= TimeSpan.FromMinutes(5))
        {
            return TimeSpan.FromSeconds(30);
        }

        if (visibleSpan <= TimeSpan.FromMinutes(15))
        {
            return TimeSpan.FromMinutes(1);
        }

        if (visibleSpan <= TimeSpan.FromMinutes(30))
        {
            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromMinutes(15);
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
            FrameworkBatteryState.Charging => GetBrush("BrandPrimaryBrush", ColorHelper.FromArgb(255, 108, 203, 95)),
            FrameworkBatteryState.Discharging => GetBrush("BrandSecondaryBrush", ColorHelper.FromArgb(255, 197, 153, 78)),
            FrameworkBatteryState.Critical => GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38)),
            FrameworkBatteryState.NotPresent => GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38)),
            _ => GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38)),
        };
    }
    private Brush GetPowerBrush(FrameworkPowerSourceState? powerSourceState)
    {
        return powerSourceState switch
        {
            FrameworkPowerSourceState.None => GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38)),
            FrameworkPowerSourceState.AcAndBattery => GetBrush("BrandSecondaryBrush", ColorHelper.FromArgb(255, 197, 153, 78)),
            FrameworkPowerSourceState.AcOnly => GetBrush("BrandPrimaryBrush", ColorHelper.FromArgb(255, 108, 203, 95)),
            FrameworkPowerSourceState.BatteryOnly => GetBrush("BrandPrimaryBrush", ColorHelper.FromArgb(255, 108, 203, 95)),
            _ => GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38)),
        };
    }
    private static Brush GetBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);
    }
}
