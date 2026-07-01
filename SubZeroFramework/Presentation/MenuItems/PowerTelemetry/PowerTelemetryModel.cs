using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using Material.Icons;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

public partial class PowerTelemetryModel : ObservableObject, IDisposable
{
    private static readonly TelemetryMetric[] TrendMetrics =
    [
        TelemetryMetric.BatteryChargePercent,
        TelemetryMetric.BatteryPresentRateAmperes,
        TelemetryMetric.BatteryPresentVoltageVolts,
    ];

    private static readonly TimeSpan TrendWindow = TimeSpan.FromMinutes(5);

    private readonly CompositeDisposable _subscriptions = [];
    private readonly Dictionary<TelemetryMetric, IDisposable> _trendSubscriptions = [];
    private readonly IBatteryTelemetryClient _batteryTelemetryClient;
    private readonly IFrameworkFanControlClient _fanControlClient;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly SynchronizationContext _synchronizationContext;
    private BatteryTelemetrySnapshot? _battery;
    private PowerDeliveryPortStatus? _activePort;
    private int? _trendBatteryIndex;
    private bool _chargeLimitsLoaded;


    public PowerTelemetryModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IPowerDeliveryClient powerDeliveryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IFrameworkFanControlClient fanControlClient,
        IFrameworkStatusClient frameworkStatusClient,
        IUnitFormattingService unitFormattingService,
        SynchronizationContext synchronizationContext)
    {
        ArgumentNullException.ThrowIfNull(powerDeliveryClient);
        ArgumentNullException.ThrowIfNull(batteryTelemetryClient);
        ArgumentNullException.ThrowIfNull(fanControlClient);
        ArgumentNullException.ThrowIfNull(frameworkStatusClient);
        _batteryTelemetryClient = batteryTelemetryClient;
        _fanControlClient = fanControlClient;
        _unitFormattingService = unitFormattingService;
        _synchronizationContext = synchronizationContext;

        _subscriptions.Add(powerDeliveryClient.WatchPorts()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdatePorts));

        _subscriptions.Add(batteryTelemetryClient.WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateBatteries));

        _subscriptions.Add(frameworkStatusClient.WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(OnStatusChanged));
    }

    private void OnStatusChanged(FrameworkSystemStatus status)
    {
        IsChargeControlEnabled = status.IsFanControlEnabled;

        var ecAvailable = status.IsGrpcActive && status.IsLibraryAvailable && status.IsFrameworkDevice == true;
        if (ecAvailable && !_chargeLimitsLoaded)
        {
            _chargeLimitsLoaded = true;
            _ = LoadChargeLimitsAsync();
        }
    }

    /// <summary>The reported USB-C Power Delivery ports, kept in sync (in place) with the live stream.</summary>
    public ObservableCollection<PowerDeliveryPortViewModel> Ports { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPorts))]
    [NotifyPropertyChangedFor(nameof(PortsVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial int PortsRevision { get; private set; }

    public bool HasPorts => Ports.Count > 0;

    public Visibility PortsVisibility => HasPorts ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => HasPorts ? Visibility.Collapsed : Visibility.Visible;

    // ----- Trends (last 5 min sparklines) -----

    // Sparkline values, mutated in place. The XAML declares the XamlLineSeries and binds Values to these stable
    // collections — mirroring PowerCardView's battery history charts, the proven LiveCharts pattern in this app.
    public ObservableCollection<DateTimePoint> ChargeTrendValues { get; } = [];

    public ObservableCollection<DateTimePoint> CurrentTrendValues { get; } = [];

    public ObservableCollection<DateTimePoint> VoltageTrendValues { get; } = [];

    // Per-metric Y-axis limits. Without these, a flat metric (e.g. charge sitting at 97% for 5 min) auto-ranges to a
    // zero-height span and the polyline renders degenerate/invisible; the limits guarantee a visible band.
    [ObservableProperty]
    public partial double? ChargeTrendMin { get; set; }

    [ObservableProperty]
    public partial double? ChargeTrendMax { get; set; }

    [ObservableProperty]
    public partial double? CurrentTrendMin { get; set; }

    [ObservableProperty]
    public partial double? CurrentTrendMax { get; set; }

    [ObservableProperty]
    public partial double? VoltageTrendMin { get; set; }

    [ObservableProperty]
    public partial double? VoltageTrendMax { get; set; }

    // ----- Charge limits (EC write, gated by service authorization) -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeMinimumDisplay))]
    [NotifyCanExecuteChangedFor(nameof(SetChargeLimitsCommand))]
    public partial double ChargeLimitMinimum { get; set; } = 20d;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeMaximumDisplay))]
    [NotifyCanExecuteChangedFor(nameof(SetChargeLimitsCommand))]
    public partial double ChargeLimitMaximum { get; set; } = 100d;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetChargeLimitsCommand))]
    public partial bool IsChargeControlEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChargeLimitsStatusVisibility))]
    public partial string ChargeLimitsStatus { get; set; } = string.Empty;

    /// <summary>Collapses the status line until there is something to say, keeping the card compact.</summary>
    public Visibility ChargeLimitsStatusVisibility => string.IsNullOrWhiteSpace(ChargeLimitsStatus) ? Visibility.Collapsed : Visibility.Visible;

    public string ChargeMinimumDisplay => $"{(int)Math.Round(ChargeLimitMinimum)}%";

    public string ChargeMaximumDisplay => $"{(int)Math.Round(ChargeLimitMaximum)}%";

    [RelayCommand(CanExecute = nameof(CanSetChargeLimits))]
    private async Task SetChargeLimitsAsync()
    {
        try
        {
            var result = await _fanControlClient.SetChargeLimitsAsync(
                (int)Math.Round(ChargeLimitMinimum),
                (int)Math.Round(ChargeLimitMaximum));
            ChargeLimitsStatus = result.Succeeded
                ? $"Applied {result.MinimumPercent}–{result.MaximumPercent}%."
                : result.Message;
        }
        catch (Exception exception)
        {
            ChargeLimitsStatus = exception.Message;
        }
    }

    private bool CanSetChargeLimits() => IsChargeControlEnabled && ChargeLimitMinimum <= ChargeLimitMaximum;

    private async Task LoadChargeLimitsAsync()
    {
        try
        {
            var result = await _fanControlClient.GetChargeLimitsAsync();
            if (result.IsAvailable)
            {
                ChargeLimitMinimum = result.MinimumPercent;
                ChargeLimitMaximum = result.MaximumPercent;
            }
        }
        catch (Exception)
        {
            // Charge limits stay at their defaults when the read is unavailable.
        }
    }

    // ----- Power-flow hero + battery overview (recomputed from the active PD port + battery snapshot) -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySectionVisibility))]
    [NotifyPropertyChangedFor(nameof(AdapterInputDisplay))]
    [NotifyPropertyChangedFor(nameof(AdapterDetailDisplay))]
    [NotifyPropertyChangedFor(nameof(SystemDrawDisplay))]
    [NotifyPropertyChangedFor(nameof(SystemDrawCaption))]
    [NotifyPropertyChangedFor(nameof(BatteryPowerDisplay))]
    [NotifyPropertyChangedFor(nameof(BatteryPowerBrush))]
    [NotifyPropertyChangedFor(nameof(ChargePercentDisplay))]
    [NotifyPropertyChangedFor(nameof(ChargePercentNumberDisplay))]
    [NotifyPropertyChangedFor(nameof(ChargeFraction))]
    [NotifyPropertyChangedFor(nameof(BatteryStateDisplay))]
    [NotifyPropertyChangedFor(nameof(BatteryStateBrush))]
    [NotifyPropertyChangedFor(nameof(BatteryStatePillBackground))]
    [NotifyPropertyChangedFor(nameof(IsBatteryAnimating))]
    [NotifyPropertyChangedFor(nameof(IsBatteryDischarging))]
    [NotifyPropertyChangedFor(nameof(BatteryStateIconKind))]
    [NotifyPropertyChangedFor(nameof(AdapterArrowsActive))]
    [NotifyPropertyChangedFor(nameof(BatteryArrowsReversed))]
    [NotifyPropertyChangedFor(nameof(BatteryArrowsAccent))]
    [NotifyPropertyChangedFor(nameof(SourceDisplay))]
    [NotifyPropertyChangedFor(nameof(VoltageDisplay))]
    [NotifyPropertyChangedFor(nameof(CurrentDisplay))]
    [NotifyPropertyChangedFor(nameof(CurrentBrush))]
    [NotifyPropertyChangedFor(nameof(BatterySummaryDisplay))]
    [NotifyPropertyChangedFor(nameof(HealthyDisplay))]
    [NotifyPropertyChangedFor(nameof(DesignCapacityDisplay))]
    [NotifyPropertyChangedFor(nameof(LastFullCapacityDisplay))]
    [NotifyPropertyChangedFor(nameof(RemainingCapacityDisplay))]
    [NotifyPropertyChangedFor(nameof(WearDisplay))]
    [NotifyPropertyChangedFor(nameof(CycleCountDisplay))]
    [NotifyPropertyChangedFor(nameof(ChemistryDisplay))]
    [NotifyPropertyChangedFor(nameof(ManufacturerDisplay))]
    [NotifyPropertyChangedFor(nameof(ModelDisplay))]
    [NotifyPropertyChangedFor(nameof(WearFraction))]
    [NotifyPropertyChangedFor(nameof(WearBarCaption))]
    public partial int FlowRevision { get; private set; }

    public bool HasBattery => _battery is { IsAvailable: true };

    public Visibility BatterySectionVisibility => HasBattery ? Visibility.Visible : Visibility.Collapsed;

    private bool HasAdapter => _activePort is { HasContract: true };

    private double AdapterInputWatts => HasAdapter ? _activePort!.VoltageVolts * _activePort.CurrentAmperes : 0d;

    /// <summary>Adapter→system arrows pulse only while an adapter is attached; static and dim when on battery.</summary>
    public bool AdapterArrowsActive => HasAdapter;

    /// <summary>Reverse the system↔battery arrows to point battery→system while discharging (draining the pack).</summary>
    public bool BatteryArrowsReversed => _battery?.BatteryState == FrameworkBatteryState.Discharging;

    /// <summary>Green flowing into the battery while charging, amber while draining, muted grey when idle / full.</summary>
    public Brush BatteryArrowsAccent => _battery?.BatteryState switch
    {
        FrameworkBatteryState.Charging => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        FrameworkBatteryState.Discharging => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        _ => AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor),
    };

    private double SignedBatteryWatts
    {
        get
        {
            if (_battery is null || _battery.Voltage is not double volts || _battery.Amperage is not double amps)
            {
                return 0d;
            }

            var magnitude = Math.Abs(volts * amps);
            return _battery.BatteryState switch
            {
                FrameworkBatteryState.Charging => magnitude,
                FrameworkBatteryState.Discharging => -magnitude,
                _ => 0d,
            };
        }
    }

    public string AdapterInputDisplay => HasAdapter ? FormatWatts(AdapterInputWatts) : "—";

    public string AdapterDetailDisplay => HasAdapter
        ? $"{_unitFormattingService.FormatVoltage(_activePort!.VoltageVolts)} · {_unitFormattingService.FormatCurrent(_activePort.CurrentAmperes)} · USB-C {_activePort.SlotIndex + 1}"
        : "No adapter attached";

    public string SystemDrawDisplay => $"≈{FormatWatts(AdapterInputWatts - SignedBatteryWatts)}";

    public string SystemDrawCaption => "input − battery charge power";

    public string BatteryPowerDisplay
    {
        get
        {
            var watts = SignedBatteryWatts;
            var rounded = Math.Round(watts);
            if (rounded > 0d)
            {
                return $"+{FormatWatts(rounded)}";
            }

            return rounded < 0d ? $"−{FormatWatts(Math.Abs(rounded))}" : FormatWatts(0d);
        }
    }

    public Brush BatteryPowerBrush => SignedBatteryWatts switch
    {
        > 0d => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        < 0d => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        _ => AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor),
    };

    public string ChargePercentDisplay => _battery is null
        ? "--"
        : _unitFormattingService.FormatRatio(_battery.ChargePercent, decimals: 0);

    /// <summary>Charge percent as a bare number (no unit) for the ring centre, where "%" is rendered smaller.</summary>
    public string ChargePercentNumberDisplay => _battery?.ChargePercent is double percent
        ? ((int)Math.Round(percent)).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "--";

    public double ChargeFraction => Math.Clamp((_battery?.ChargePercent ?? 0d) / 100d, 0d, 1d);

    public string BatteryStateDisplay => _battery?.BatteryState switch
    {
        FrameworkBatteryState.Charging => "Charging",
        FrameworkBatteryState.Discharging => "Discharging",
        FrameworkBatteryState.Idle => "Idle",
        FrameworkBatteryState.Critical => "Critical",
        FrameworkBatteryState.ChargingAndDischarging => "Charging / discharging",
        FrameworkBatteryState.NotPresent => "Not present",
        _ => "Unknown",
    };

    public Brush BatteryStateBrush => _battery?.BatteryState switch
    {
        FrameworkBatteryState.Charging => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        FrameworkBatteryState.Discharging => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        FrameworkBatteryState.Critical => AppThemeBrushes.Get("SeverityCriticalBrush", AppThemeBrushes.SeverityCriticalColor),
        _ => AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor),
    };

    /// <summary>Faint fill behind the state pill (state colour at low alpha) — a soft pill, not a solid oval.</summary>
    public Brush BatteryStatePillBackground
    {
        get
        {
            var color = _battery?.BatteryState switch
            {
                FrameworkBatteryState.Charging => AppThemeBrushes.StatusSuccessColor,
                FrameworkBatteryState.Discharging => AppThemeBrushes.StatusWarningColor,
                FrameworkBatteryState.Critical => AppThemeBrushes.SeverityCriticalColor,
                _ => AppThemeBrushes.TextSecondaryColor,
            };
            return new SolidColorBrush(ColorHelper.FromArgb(36, color.R, color.G, color.B));
        }
    }

    /// <summary>Drives the ring's flowing dash overlay — animated only while charging or discharging.</summary>
    public bool IsBatteryAnimating => _battery?.BatteryState is FrameworkBatteryState.Charging or FrameworkBatteryState.Discharging;

    /// <summary>Reverses the ring dash flow when discharging.</summary>
    public bool IsBatteryDischarging => _battery?.BatteryState == FrameworkBatteryState.Discharging;

    /// <summary>Glyph for the battery state pill — a bolt while charging.</summary>
    public MaterialIconKind BatteryStateIconKind => _battery?.BatteryState switch
    {
        FrameworkBatteryState.Charging => MaterialIconKind.Flash,
        _ => MaterialIconKind.Battery,
    };

    public string SourceDisplay => _battery?.PowerSourceState switch
    {
        FrameworkPowerSourceState.AcOnly => "AC",
        FrameworkPowerSourceState.BatteryOnly => "Battery",
        FrameworkPowerSourceState.AcAndBattery => "AC + battery",
        _ => "Unknown",
    };

    public string VoltageDisplay => _unitFormattingService.FormatVoltage(_battery?.Voltage);

    public string CurrentDisplay
    {
        get
        {
            if (_battery?.Amperage is not double amps)
            {
                return _unitFormattingService.FormatCurrent(null);
            }

            var formatted = _unitFormattingService.FormatCurrent(Math.Abs(amps));
            return _battery.BatteryState switch
            {
                FrameworkBatteryState.Charging => $"+{formatted}",
                FrameworkBatteryState.Discharging => $"−{formatted}",
                _ => formatted,
            };
        }
    }

    public Brush CurrentBrush => BatteryPowerBrush;

    public string BatterySummaryDisplay => HasBattery
        ? $"{ChargePercentDisplay} · {BatteryStateDisplay.ToLowerInvariant()}"
        : "No battery";

    // ----- Health & capacity (energy in Wh = capacity Ah × nominal voltage) -----

    private double? DesignWattHours => WattHours(_battery?.DesignCapacityAmpereHours);

    private double? LastFullWattHours => WattHours(_battery?.LastFullChargeCapacityAmpereHours);

    private double? RemainingWattHours => WattHours(_battery?.RemainingCapacityAmpereHours);

    private double? WattHours(double? ampereHours) =>
        ampereHours is double ah && _battery?.DesignVoltageVolts is double volts && volts > 0d ? ah * volts : null;

    private double? HealthFraction =>
        DesignWattHours is double design && design > 0d && LastFullWattHours is double lastFull
            ? Math.Clamp(lastFull / design, 0d, 1d)
            : null;

    public string DesignCapacityDisplay => FormatWattHours(DesignWattHours);

    public string LastFullCapacityDisplay => FormatWattHours(LastFullWattHours);

    public string RemainingCapacityDisplay => FormatWattHours(RemainingWattHours);

    public string HealthyDisplay => HealthFraction is double fraction ? $"{fraction * 100d:0.0}% healthy" : "—";

    public string WearDisplay => HealthFraction is double fraction ? $"{(1d - fraction) * 100d:0.0}%" : "--";

    public double WearFraction => HealthFraction ?? 0d;

    public string WearBarCaption => DesignWattHours is double design && LastFullWattHours is double lastFull
        ? $"last-full {lastFull:0.0} Wh of {design:0.0} Wh design"
        : "Wear data unavailable";

    public string CycleCountDisplay => _battery?.CycleCount is uint cycles ? cycles.ToString() : "--";

    public string ChemistryDisplay => string.IsNullOrWhiteSpace(_battery?.BatteryType) ? "--" : _battery!.BatteryType!;

    public string ManufacturerDisplay => string.IsNullOrWhiteSpace(_battery?.Manufacturer) ? "--" : _battery!.Manufacturer!;

    public string ModelDisplay => string.IsNullOrWhiteSpace(_battery?.ModelNumber) ? "--" : _battery!.ModelNumber!;

    private string FormatWatts(double watts) => $"{Math.Round(watts):0} W";

    private static string FormatWattHours(double? wattHours) => wattHours is double wh ? $"{wh:0.0} Wh" : "--";

    private void UpdateBatteries(IChangeSet<BatteryTelemetrySnapshot, int> set)
    {
        foreach (var change in set)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    // Track the primary battery (lowest index).
                    if (_battery is null || change.Current.BatteryIndex <= _battery.BatteryIndex)
                    {
                        _battery = change.Current;
                    }

                    break;
                case ChangeReason.Remove:
                    if (_battery is not null && change.Key == _battery.BatteryIndex)
                    {
                        _battery = null;
                    }

                    break;
            }
        }

        if (_battery is { IsAvailable: true })
        {
            EnsureTrendSubscriptions(_battery.BatteryIndex);
        }

        FlowRevision++;
    }

    private void EnsureTrendSubscriptions(int batteryIndex)
    {
        if (_trendBatteryIndex == batteryIndex)
        {
            return;
        }

        foreach (var subscription in _trendSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _trendSubscriptions.Clear();
        _trendBatteryIndex = batteryIndex;

        foreach (var metric in TrendMetrics)
        {
            var captured = metric;
            _trendSubscriptions[metric] = _batteryTelemetryClient
                .WatchBatteryHistory(batteryIndex, metric, TrendWindow)
                .ToCollection()
                .ObserveOn(_synchronizationContext)
                .Subscribe(points => UpdateTrend(captured, points));
        }
    }

    private void UpdateTrend(TelemetryMetric metric, IReadOnlyCollection<TelemetryPoint> points)
    {
        var trend = points
            .OrderBy(point => point.ObservedAt)
            .ThenBy(point => point.SampleId)
            .Select(point => new DateTimePoint(point.ObservedAt.LocalDateTime, point.NumericValue))
            .ToArray();

        var (min, max) = TrendLimits(trend);

        switch (metric)
        {
            case TelemetryMetric.BatteryChargePercent:
                SynchronizePoints(ChargeTrendValues, trend);
                (ChargeTrendMin, ChargeTrendMax) = (min, max);
                break;
            case TelemetryMetric.BatteryPresentRateAmperes:
                SynchronizePoints(CurrentTrendValues, trend);
                (CurrentTrendMin, CurrentTrendMax) = (min, max);
                break;
            case TelemetryMetric.BatteryPresentVoltageVolts:
                SynchronizePoints(VoltageTrendValues, trend);
                (VoltageTrendMin, VoltageTrendMax) = (min, max);
                break;
        }
    }

    /// <summary>Reconciles the sparkline's backing collection to <paramref name="source"/> in place (replace changed
    /// points, trim, extend) so the bound series observes granular changes instead of a collection swap.</summary>
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

    /// <summary>Y-axis band for a sparkline: the data range padded by 5% (or ±0.5 when perfectly flat) so a constant
    /// series still draws a centred horizontal line instead of collapsing to a zero-height, invisible range.</summary>
    private static (double? Min, double? Max) TrendLimits(IReadOnlyList<DateTimePoint> points)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var point in points)
        {
            if (point.Value is not double value)
            {
                continue;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (min > max)
        {
            return (null, null);
        }

        var pad = max - min < 1e-6 ? 0.5 : (max - min) * 0.05;
        return (min - pad, max + pad);
    }

    private void UpdatePorts(IReadOnlyList<PowerDeliveryPortStatus> ports)
    {
        var bySlot = Ports.ToDictionary(static vm => vm.SlotIndex);
        foreach (var status in ports.OrderBy(PositionSortKey).ThenBy(static p => p.SlotIndex))
        {
            if (bySlot.TryGetValue(status.SlotIndex, out var existing))
            {
                existing.Update(status);
            }
            else
            {
                Ports.Add(new PowerDeliveryPortViewModel(_unitFormattingService, status));
            }
        }

        var keep = ports.Select(static p => p.SlotIndex).ToHashSet();
        for (var index = Ports.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(Ports[index].SlotIndex))
            {
                Ports.RemoveAt(index);
            }
        }

        _activePort = ports.FirstOrDefault(static p => p.IsActivePort && p.HasContract)
            ?? ports.FirstOrDefault(static p => p.IsActivePort);

        PortsRevision++;
        FlowRevision++;
    }

    // Orders ports so the 2-column grid mirrors the chassis: left ports in the left column, right ports in the
    // right column, top-to-bottom Back → Middle → Front. Row-major fill key = rank*2 + column (left = 0, right = 1).
    // The graphics-module port comes last; undocumented platforms fall back to slot order.
    private static int PositionSortKey(PowerDeliveryPortStatus port)
    {
        if (string.Equals(port.PortSource, "GraphicsModule", System.StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        var position = port.PortPosition;
        if (string.IsNullOrEmpty(position))
        {
            return 2000 + port.SlotIndex;
        }

        var rank = position.Contains("Back", System.StringComparison.OrdinalIgnoreCase) ? 0
            : position.Contains("Middle", System.StringComparison.OrdinalIgnoreCase) ? 1
            : position.Contains("Front", System.StringComparison.OrdinalIgnoreCase) ? 2
            : 3;
        var column = port.PortIsLeft ? 0 : 1;
        return (rank * 2) + column;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        foreach (var subscription in _trendSubscriptions.Values)
        {
            subscription.Dispose();
        }
    }
}
