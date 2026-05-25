using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using FrameworkDotnet.Enums;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.UI.Dispatching;

using SkiaSharp;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Orchestrates the Fan Curve Profiles page. Reuses the dashboard <see cref="FanCardModel"/>
/// so each tile shows the same gauge + history chart, extended with a red driving-temperature
/// line, and toggles per-fan selection state to highlight the active card.
/// </summary>
public partial class FanCurveProfilesModel : ObservableObject, IDisposable
{
    private const double DefaultManualDutyPercent = 50d;
    private static readonly TimeSpan ManualDutyDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IFanCapabilityClient _fanCapabilityClient;
    private readonly IFanControlStateClient _fanControlStateClient;
    private readonly IFanStateClient _fanStateClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IFrameworkFanControlClient _fanControlClient;
    private readonly IUserUnitPreferencesClient _userUnitPreferencesClient;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<FanCurveProfilesModel> _logger;

    private readonly CompositeDisposable _subscriptions = new();
    private readonly Subject<double> _manualDutyChanges = new();
    private readonly ObservableCollection<FanCardModel> _fans = [];
    private readonly Dictionary<int, FanCardModel> _fanCardsByIndex = [];
    private readonly Dictionary<int, FanCapabilityState> _capabilities = [];
    private readonly Dictionary<int, FanControlStateSnapshot> _controlStates = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStates = [];
    private readonly Dictionary<int, FanTelemetrySnapshot> _fanSnapshots = [];
    private readonly Dictionary<int, TemperatureTelemetrySnapshot> _temperatureSnapshots = [];
    private readonly Dictionary<int, FanTelemetrySeriesPoint[]> _fanHistoryPoints = [];
    private readonly Dictionary<int, TelemetryPoint[]> _temperatureHistoryPoints = [];
    private readonly Dictionary<int, IDisposable> _fanHistorySubscriptions = [];
    private readonly Dictionary<int, IDisposable> _temperatureHistorySubscriptions = [];
    private readonly ObservableCollection<SensorChipModel> _availableSensors = [];
    private readonly Dictionary<int, SensorChipModel> _sensorChipIndex = [];
    private readonly ObservableCollection<CurvePointModel> _curvePoints =
    [
        new CurvePointModel(40, 30d),
        new CurvePointModel(60, 60d),
        new CurvePointModel(80, 100d),
    ];
    private readonly ObservableCollection<ObservablePoint> _curveSeriesPoints = [];

    private readonly Dictionary<int, ObservableCollection<DateTimePoint>> _sensorChartPointsBySensorIndex = [];
    private readonly Dictionary<int, ISeries> _sensorChartSeriesBySensorIndex = [];
    private readonly Dictionary<SensorChipModel, PropertyChangedEventHandler> _sensorChipHandlers = [];
    private readonly ObservableCollection<DateTimePoint> _drivingTemperaturePoints = [];
    private LineSeries<DateTimePoint>? _drivingTemperatureSeries;
    private bool _suppressSensorSelectionReentry;
    private CustomCurveSnapshot? _pendingCurveSnapshot;

    private static readonly SKColor DrivingTemperatureColor = new(80, 150, 255);

    private static readonly SKColor[] SensorChartPalette =
    [
        new(138, 183, 232),
        new(232, 168, 124),
        new(168, 220, 158),
        new(220, 158, 220),
        new(244, 213, 134),
        new(158, 220, 220),
        new(232, 138, 138),
    ];

    public FanCurveProfilesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFanCapabilityClient fanCapabilityClient,
        IFanControlStateClient fanControlStateClient,
        IFanStateClient fanStateClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IFrameworkFanControlClient fanControlClient,
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        SynchronizationContext synchronizationContext,
        DispatcherQueue dispatcherQueue,
        ILogger<FanCurveProfilesModel> logger)
    {
        _fanCapabilityClient = fanCapabilityClient;
        _fanControlStateClient = fanControlStateClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _fanControlClient = fanControlClient;
        _userUnitPreferencesClient = userUnitPreferencesClient;
        _unitFormattingService = unitFormattingService;
        _synchronizationContext = synchronizationContext;
        _dispatcherQueue = dispatcherQueue;
        _logger = logger;

        Fans = new ReadOnlyObservableCollection<FanCardModel>(_fans);
        AvailableSensors = new ReadOnlyObservableCollection<SensorChipModel>(_availableSensors);
        CurvePoints = new ReadOnlyObservableCollection<CurvePointModel>(_curvePoints);
        CurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_curveSeriesPoints);

        _curvePoints.CollectionChanged += OnCurvePointsCollectionChanged;
        foreach (var point in _curvePoints)
        {
            point.PropertyChanged += OnCurvePointPropertyChanged;
        }
        RefreshCurveSeries();

        _fanCapabilityClient
            .WatchFanCapabilities()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyCapabilityChanges)
            .DisposeWith(_subscriptions);

        _fanControlStateClient
            .WatchFanControlStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyControlStateChanges)
            .DisposeWith(_subscriptions);

        _fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyFanStateChanges)
            .DisposeWith(_subscriptions);

        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyFanTelemetryChanges)
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyTemperatureChanges)
            .DisposeWith(_subscriptions);

        _userUnitPreferencesClient
            .WatchPreferences()
            .ObserveOn(_synchronizationContext)
            .Subscribe(_ => RefreshUnitFormatting())
            .DisposeWith(_subscriptions);

        _manualDutyChanges
            .Throttle(ManualDutyDebounce)
            .DistinctUntilChanged()
            .ObserveOn(_synchronizationContext)
            .Select(duty => Observable.FromAsync(ct => ApplyDebouncedManualDutyAsync(duty, ct)))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        _manualDutyChanges.DisposeWith(_subscriptions);
    }

    public ReadOnlyObservableCollection<FanCardModel> Fans { get; }

    public ReadOnlyObservableCollection<SensorChipModel> AvailableSensors { get; }

    public ReadOnlyObservableCollection<CurvePointModel> CurvePoints { get; }

    public ReadOnlyObservableCollection<ObservablePoint> CurveSeriesPoints { get; }

    public SolidColorPaint CurveStrokePaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartAccentColor.R,
        AppThemeBrushes.ChartAccentColor.G,
        AppThemeBrushes.ChartAccentColor.B,
        AppThemeBrushes.ChartAccentColor.A), 2.5f);

    public SolidColorPaint CurveGeometryFillPaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartAccentColor.R,
        AppThemeBrushes.ChartAccentColor.G,
        AppThemeBrushes.ChartAccentColor.B,
        AppThemeBrushes.ChartAccentColor.A));

    public SolidColorPaint CurveAxisLabelsPaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartSubtleAxisLabelColor.R,
        AppThemeBrushes.ChartSubtleAxisLabelColor.G,
        AppThemeBrushes.ChartSubtleAxisLabelColor.B,
        AppThemeBrushes.ChartSubtleAxisLabelColor.A));

    public SolidColorPaint CurveAxisSeparatorsPaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartSeparatorColor.R,
        AppThemeBrushes.ChartSeparatorColor.G,
        AppThemeBrushes.ChartSeparatorColor.B,
        AppThemeBrushes.ChartSeparatorColor.A));

    public Func<double, string> CurveTemperatureLabelFormatter { get; } = static value => $"{value:0}°";

    public Func<double, string> CurveDutyLabelFormatter { get; } = static value => $"{value:0}%";

    public Func<DateTime, string> SensorChartDateFormatter { get; } = static dt => dt.ToString("HH:mm:ss");

    public Func<double, string> SensorChartTemperatureFormatter { get; } = static value => $"{value:0.#}°";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorChartVisibility))]
    public partial ISeries[] SensorChartSeries { get; set; } = [];

    [ObservableProperty]
    public partial double[] SensorChartSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? SensorChartMinLimit { get; set; }

    [ObservableProperty]
    public partial double? SensorChartMaxLimit { get; set; }

    public Microsoft.UI.Xaml.Visibility SensorChartVisibility =>
        SensorChartSeries.Length > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private sealed record CustomCurveSnapshot(
        TemperatureAggregationMode Aggregation,
        int[] SensorIndices,
        (int Temperature, double Duty)[] CurvePoints);

    public bool IsAutoSelected => SelectedFanMode == FanControlMode.Auto && !ShowCustomEditor;

    public bool IsManualSelected => SelectedFanMode == FanControlMode.Manual && !ShowCustomEditor;

    public bool IsMaxSelected => SelectedFanMode == FanControlMode.Max && !ShowCustomEditor;

    public bool IsCustomSelected => SelectedFanMode == FanControlMode.CustomCurve || ShowCustomEditor;

    public Microsoft.UI.Xaml.Media.Brush AutoTileBackground => GetTileBackground(IsAutoSelected);

    public Microsoft.UI.Xaml.Media.Brush ManualTileBackground => GetTileBackground(IsManualSelected);

    public Microsoft.UI.Xaml.Media.Brush MaxTileBackground => GetTileBackground(IsMaxSelected);

    public Microsoft.UI.Xaml.Media.Brush CustomTileBackground => GetTileBackground(IsCustomSelected);

    private static Microsoft.UI.Xaml.Media.Brush GetTileBackground(bool selected) => selected
        ? AppThemeBrushes.Get("CardSelectedBackgroundBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : AppThemeBrushes.Get("CardBackgroundBrush", AppThemeBrushes.CardBackgroundColor);

    public IReadOnlyList<TemperatureAggregationMode> AggregationModes { get; } =
    [
        TemperatureAggregationMode.Average,
        TemperatureAggregationMode.Maximum,
        TemperatureAggregationMode.Minimum,
        TemperatureAggregationMode.Median,
    ];

    // Nullable to avoid an NRE in the generated x:Bind TwoWay write-back for ComboBox.SelectedItem,
    // which unboxes a transient null during initial bind ordering.
    [ObservableProperty]
    public partial TemperatureAggregationMode? SelectedAggregation { get; set; } = TemperatureAggregationMode.Maximum;

    partial void OnSelectedAggregationChanged(TemperatureAggregationMode? value) => RefreshSensorChart();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFan))]
    [NotifyPropertyChangedFor(nameof(CanSelectMode))]
    [NotifyPropertyChangedFor(nameof(EditorVisibility))]
    [NotifyPropertyChangedFor(nameof(IsAutoBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsManualBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsMaxBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsAutoSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsMaxSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(AutoTileBackground))]
    [NotifyPropertyChangedFor(nameof(ManualTileBackground))]
    [NotifyPropertyChangedFor(nameof(MaxTileBackground))]
    [NotifyPropertyChangedFor(nameof(CustomTileBackground))]
    [NotifyPropertyChangedFor(nameof(SelectedFanHeading))]
    [NotifyPropertyChangedFor(nameof(SelectedFanModeDisplay))]
    [NotifyCanExecuteChangedFor(nameof(SetAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetMaxCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyManualDutyCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterCustomEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomCurveCommand))]
    public partial FanCardModel? SelectedFan { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsAutoSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsMaxSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(AutoTileBackground))]
    [NotifyPropertyChangedFor(nameof(ManualTileBackground))]
    [NotifyPropertyChangedFor(nameof(MaxTileBackground))]
    [NotifyPropertyChangedFor(nameof(CustomTileBackground))]
    [NotifyPropertyChangedFor(nameof(CanSelectMode))]
    [NotifyCanExecuteChangedFor(nameof(SetAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetMaxCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterCustomEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCustomEditorCommand))]
    public partial bool ShowCustomEditor { get; set; }

    public bool CanSelectMode => HasSelectedFan && !ShowCustomEditor;

    [ObservableProperty]
    public partial double ManualDutyPercent { get; set; } = DefaultManualDutyPercent;

    partial void OnManualDutyPercentChanged(double value)
    {
        if (SelectedFan?.ControlState?.Mode != FanControlMode.Manual)
        {
            return;
        }

        _manualDutyChanges.OnNext(Math.Clamp(value, 0d, 100d));
    }

    private async Task ApplyDebouncedManualDutyAsync(double duty, CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null || fan.ControlState?.Mode != FanControlMode.Manual)
        {
            return;
        }

        try
        {
            await _fanControlClient.SetFanDutyAsync(fan.Snapshot.FanIndex, duty, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-apply manual duty {Duty:0}% for fan {FanIndex}", duty, fan.Snapshot.FanIndex);
            ReportStatus($"Failed to apply manual duty: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Controls.InfoBarSeverity StatusSeverity { get; set; }
        = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsStatusVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFanModeDisplay))]
    [NotifyPropertyChangedFor(nameof(IsAutoBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsManualBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsCustomBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsMaxBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsAutoSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    [NotifyPropertyChangedFor(nameof(IsMaxSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(AutoTileBackground))]
    [NotifyPropertyChangedFor(nameof(ManualTileBackground))]
    [NotifyPropertyChangedFor(nameof(MaxTileBackground))]
    [NotifyPropertyChangedFor(nameof(CustomTileBackground))]
    private partial int ControlStateRevision { get; set; }

    public bool HasSelectedFan => SelectedFan is not null;

    public Microsoft.UI.Xaml.Visibility EditorVisibility =>
        SelectedFan is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility IsAutoBodyVisible =>
        SelectedFanMode == FanControlMode.Auto ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsManualBodyVisible =>
        SelectedFanMode == FanControlMode.Manual ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsCustomBodyVisible =>
        (SelectedFanMode == FanControlMode.CustomCurve || ShowCustomEditor)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility IsMaxBodyVisible =>
        SelectedFanMode == FanControlMode.Max ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string SelectedFanHeading => SelectedFan is null
        ? "Select a fan"
        : $"Editor — {SelectedFan.Snapshot.DisplayName}";

    public string SelectedFanModeDisplay => SelectedFanMode switch
    {
        FanControlMode.Auto => "Auto",
        FanControlMode.Manual => "Manual",
        FanControlMode.CustomCurve => "Custom curve",
        FanControlMode.Max => "Max",
        _ => string.Empty,
    };

    private FanControlMode SelectedFanMode => SelectedFan?.ControlState?.Mode ?? FanControlMode.Auto;

    [RelayCommand]
    public void SelectFan(FanCardModel? fan)
    {
        if (fan is null || ReferenceEquals(fan, SelectedFan))
        {
            return;
        }

        SelectedFan = fan;
    }

    partial void OnSelectedFanChanged(FanCardModel? oldValue, FanCardModel? newValue)
    {
        foreach (var fan in _fans)
        {
            fan.IsSelected = ReferenceEquals(fan, newValue);
        }

        ShowCustomEditor = false;
    }

    [RelayCommand(CanExecute = nameof(CanSelectMode))]
    private void EnterCustomEditor()
    {
        _pendingCurveSnapshot = new CustomCurveSnapshot(
            SelectedAggregation ?? TemperatureAggregationMode.Maximum,
            _availableSensors.Where(static c => c.IsSelected).Select(static c => c.SensorIndex).ToArray(),
            _curvePoints.Select(static p => (p.TemperatureCelsius, p.DutyPercent)).ToArray());

        if (!_availableSensors.Any(static c => c.IsSelected) && _availableSensors.Count > 0)
        {
            _suppressSensorSelectionReentry = true;
            try { _availableSensors[0].IsSelected = true; }
            finally { _suppressSensorSelectionReentry = false; }
            EnsureTemperatureHistorySubscription(_availableSensors[0].SensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
            RefreshSensorChart();
        }

        ShowCustomEditor = true;
    }

    private bool CanCancelCustomEditor() => ShowCustomEditor;

    [RelayCommand(CanExecute = nameof(CanCancelCustomEditor))]
    private void CancelCustomEditor()
    {
        if (_pendingCurveSnapshot is { } snapshot)
        {
            SelectedAggregation = snapshot.Aggregation;

            _suppressSensorSelectionReentry = true;
            try
            {
                var desired = new HashSet<int>(snapshot.SensorIndices);
                foreach (var chip in _availableSensors)
                {
                    chip.IsSelected = desired.Contains(chip.SensorIndex);
                }
            }
            finally
            {
                _suppressSensorSelectionReentry = false;
            }

            foreach (var point in _curvePoints)
            {
                point.PropertyChanged -= OnCurvePointPropertyChanged;
            }
            _curvePoints.Clear();
            foreach (var (t, d) in snapshot.CurvePoints)
            {
                _curvePoints.Add(new CurvePointModel(t, d));
            }

            RefreshSensorChart();
            RefreshCurveSeries();
        }

        _pendingCurveSnapshot = null;
        ShowCustomEditor = false;
    }

    [RelayCommand(CanExecute = nameof(CanSelectMode))]
    private async Task SetAutoAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;

        try
        {
            await _fanControlClient.RestoreAutoFanControlAsync(fan.Snapshot.FanIndex, cancellationToken).ConfigureAwait(true);
            ReportStatus($"Fan {fan.Snapshot.DisplayName} restored to Auto.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore Auto mode for fan {FanIndex}", fan.Snapshot.FanIndex);
            ReportStatus($"Failed to restore Auto: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectMode))]
    private async Task SetManualAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;

        await ApplyManualDutyAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanSelectMode))]
    private async Task SetMaxAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;

        try
        {
            await _fanControlClient.SetFanMaxAsync(fan.Snapshot.FanIndex, cancellationToken).ConfigureAwait(true);
            ReportStatus($"Fan {fan.Snapshot.DisplayName} set to Max (100%). Acoustics will be loud.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply Max mode for fan {FanIndex}", fan.Snapshot.FanIndex);
            ReportStatus($"Failed to apply Max: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFan))]
    private async Task ApplyManualDutyAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;

        var duty = Math.Clamp(ManualDutyPercent, 0d, 100d);

        try
        {
            await _fanControlClient.SetFanDutyAsync(fan.Snapshot.FanIndex, duty, cancellationToken).ConfigureAwait(true);
            ReportStatus($"Fan {fan.Snapshot.DisplayName} set to {duty:0}% manual duty.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply manual duty for fan {FanIndex}", fan.Snapshot.FanIndex);
            ReportStatus($"Failed to apply manual duty: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFan))]
    private async Task ApplyCustomCurveAsync(CancellationToken cancellationToken)
    {
        var fan = SelectedFan;
        if (fan is null) return;

        if (_curvePoints.Count < 2)
        {
            ReportStatus("Custom curve needs at least two points.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        var selectedSensors = _availableSensors
            .Where(static chip => chip.IsSelected)
            .Select(static chip => chip.SensorIndex)
            .ToArray();

        if (selectedSensors.Length == 0)
        {
            ReportStatus("Select at least one driving temperature sensor before applying a custom curve.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        var dictionary = new Dictionary<int, double>(_curvePoints.Count);
        foreach (var point in _curvePoints)
        {
            dictionary[point.TemperatureCelsius] = Math.Clamp(point.DutyPercent, 0d, 100d);
        }

        try
        {
            var aggregation = SelectedAggregation ?? TemperatureAggregationMode.Maximum;
            var result = await _fanControlClient.SetCustomCurveAsync(fan.Snapshot.FanIndex, dictionary, selectedSensors, aggregation, cancellationToken).ConfigureAwait(true);
            if (result.Succeeded)
            {
                _pendingCurveSnapshot = null;
                ShowCustomEditor = false;
                ReportStatus($"Custom curve applied to {fan.Snapshot.DisplayName} ({dictionary.Count} points, {selectedSensors.Length} sensor(s)).", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
            }
            else
            {
                ReportStatus($"Service rejected the custom curve: {result.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply custom curve for fan {FanIndex}", fan.Snapshot.FanIndex);
            ReportStatus($"Failed to apply custom curve: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void RemoveCurvePoint(CurvePointModel? point)
    {
        if (point is null) return;
        if (_curvePoints.Count <= 2) return;
        _curvePoints.Remove(point);
    }

    public void AddCurvePointAt(double temperatureCelsius, double dutyPercent)
    {
        var t = (int)Math.Round(Math.Clamp(temperatureCelsius, 0d, 130d));
        var d = Math.Clamp(dutyPercent, 0d, 100d);
        _curvePoints.Add(new CurvePointModel(t, d));
    }

    public void UpdateCurvePoint(CurvePointModel point, double temperatureCelsius, double dutyPercent)
    {
        point.TemperatureCelsius = (int)Math.Round(Math.Clamp(temperatureCelsius, 0d, 130d));
        point.DutyPercent = Math.Clamp(dutyPercent, 0d, 100d);
    }

    public CurvePointModel? FindNearestCurvePoint(double temperatureCelsius, double dutyPercent, double maxTemperatureDelta, double maxDutyDelta)
    {
        CurvePointModel? best = null;
        var bestDistanceSquared = double.PositiveInfinity;
        foreach (var point in _curvePoints)
        {
            var dt = (point.TemperatureCelsius - temperatureCelsius) / maxTemperatureDelta;
            var dd = (point.DutyPercent - dutyPercent) / maxDutyDelta;
            var distanceSquared = (dt * dt) + (dd * dd);
            if (distanceSquared <= 1d && distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = point;
            }
        }
        return best;
    }

    private void OnCurvePointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CurvePointModel point in e.OldItems)
            {
                point.PropertyChanged -= OnCurvePointPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (CurvePointModel point in e.NewItems)
            {
                point.PropertyChanged += OnCurvePointPropertyChanged;
            }
        }

        RefreshCurveSeries();
    }

    private void OnCurvePointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CurvePointModel.TemperatureCelsius) or nameof(CurvePointModel.DutyPercent))
        {
            RefreshCurveSeries();
        }
    }

    private void RefreshCurveSeries()
    {
        _curveSeriesPoints.Clear();
        var ordered = _curvePoints.OrderBy(p => p.TemperatureCelsius).ToArray();
        if (ordered.Length == 0)
        {
            _curveSeriesPoints.Add(new ObservablePoint(0d, 0d));
            _curveSeriesPoints.Add(new ObservablePoint(130d, 100d));
            return;
        }

        if (ordered[0].TemperatureCelsius > 0)
        {
            _curveSeriesPoints.Add(new ObservablePoint(0d, 0d));
        }
        foreach (var point in ordered)
        {
            _curveSeriesPoints.Add(new ObservablePoint(point.TemperatureCelsius, point.DutyPercent));
        }
        if (ordered[^1].TemperatureCelsius < 130)
        {
            _curveSeriesPoints.Add(new ObservablePoint(130d, ordered[^1].DutyPercent));
        }
    }

    private void RefreshUnitFormatting()
    {
        foreach (var fan in _fans)
        {
            fan.RefreshUnitFormatting();
            RefreshFanHistory(fan.Snapshot.FanIndex);
        }

        RefreshAllDrivingTemperatureHistory();
        RefreshSensorChart();
    }

    private void ApplyCapabilityChanges(IChangeSet<FanCapabilityState, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _capabilities.Remove(change.Key);
                if (_fanCardsByIndex.TryGetValue(change.Key, out var existing))
                {
                    existing.Capability = null;
                }
                continue;
            }

            _capabilities[change.Key] = change.Current;
            var fan = EnsureFanCard(change.Key);
            fan.Capability = change.Current;
        }
    }

    private void ApplyControlStateChanges(IChangeSet<FanControlStateSnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _controlStates.Remove(change.Key);
                if (_fanCardsByIndex.TryGetValue(change.Key, out var existing))
                {
                    existing.ControlState = null;
                    existing.DrivingSensors = [];
                    existing.DrivingTemperatureHistory = [];
                }
                continue;
            }

            _controlStates[change.Key] = change.Current;
            var fan = EnsureFanCard(change.Key);
            fan.ControlState = change.Current;
            UpdateDrivingSensors(fan);
            EnsureTemperatureHistorySubscriptionsForFan(fan);
            RefreshDrivingTemperatureHistory(fan);
        }

        if (SelectedFan is not null)
        {
            ControlStateRevision++;
        }
    }

    private void ApplyFanStateChanges(IChangeSet<FanStateSnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _fanStates.Remove(change.Key);
                if (_fanCardsByIndex.TryGetValue(change.Key, out var existing))
                {
                    existing.FanState = null;
                }
                continue;
            }

            _fanStates[change.Key] = change.Current;
            var fan = EnsureFanCard(change.Key);
            fan.FanState = change.Current;
        }
    }

    private void ApplyFanTelemetryChanges(IChangeSet<FanTelemetrySnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Add || change.Reason == ChangeReason.Update || change.Reason == ChangeReason.Refresh)
            {
                _fanSnapshots[change.Key] = change.Current;
                var fan = EnsureFanCard(change.Key);
                fan.Snapshot = change.Current;
                continue;
            }

            if (change.Reason == ChangeReason.Remove)
            {
                _fanSnapshots.Remove(change.Key);
                RemoveFanCard(change.Key);
            }
        }
    }

    private FanCardModel EnsureFanCard(int fanIndex)
    {
        if (_fanCardsByIndex.TryGetValue(fanIndex, out var existing))
        {
            return existing;
        }

        var fan = new FanCardModel(_unitFormattingService)
        {
            Snapshot = _fanSnapshots.GetValueOrDefault(fanIndex) ?? new FanTelemetrySnapshot
            {
                FanIndex = fanIndex,
                DisplayName = $"Fan {fanIndex}",
                UnitSymbol = "rpm",
                ObservedAt = DateTimeOffset.UtcNow,
                SpeedRpm = 0d,
                IsAvailable = false,
            },
            Capability = _capabilities.GetValueOrDefault(fanIndex),
            ControlState = _controlStates.GetValueOrDefault(fanIndex),
            DrivingSensors = GetDrivingSensors(_controlStates.GetValueOrDefault(fanIndex)),
            FanState = _fanStates.GetValueOrDefault(fanIndex),
        };

        _fanCardsByIndex[fanIndex] = fan;
        InsertSorted(fan);
        EnsureFanHistorySubscription(fanIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
        EnsureTemperatureHistorySubscriptionsForFan(fan);
        RefreshDrivingTemperatureHistory(fan);

        if (SelectedFan is null || fanIndex < SelectedFan.Snapshot.FanIndex)
        {
            SelectFan(fan);
        }

        return fan;
    }

    private void ApplyTemperatureChanges(IChangeSet<TemperatureTelemetrySnapshot, int> changes)
    {
        var anyChanged = false;

        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _temperatureSnapshots.Remove(change.Key);
                RemoveSensorChip(change.Key);
                StopTemperatureHistorySubscription(change.Key);
                anyChanged = true;
                continue;
            }

            _temperatureSnapshots[change.Key] = change.Current;
            UpsertSensorChip(change.Current);
            anyChanged = true;
        }

        if (anyChanged)
        {
            foreach (var fan in _fans)
            {
                UpdateDrivingSensors(fan);
                EnsureTemperatureHistorySubscriptionsForFan(fan);
            }
        }
    }

    private void UpsertSensorChip(TemperatureTelemetrySnapshot snapshot)
    {
        if (!_sensorChipIndex.TryGetValue(snapshot.SensorIndex, out var chip))
        {
            chip = new SensorChipModel(snapshot.SensorIndex, snapshot.DisplayName);
            _sensorChipIndex[snapshot.SensorIndex] = chip;
            AttachSensorChipHandler(chip);

            var insertIndex = 0;
            while (insertIndex < _availableSensors.Count && _availableSensors[insertIndex].SensorIndex < snapshot.SensorIndex)
            {
                insertIndex++;
            }
            _availableSensors.Insert(insertIndex, chip);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.DisplayName))
        {
            chip.DisplayName = snapshot.DisplayName;
        }

        chip.CurrentTemperatureCelsius = snapshot.IsAvailable ? snapshot.TemperatureCelsius : null;
    }

    private void RemoveSensorChip(int sensorIndex)
    {
        if (_sensorChipIndex.Remove(sensorIndex, out var chip))
        {
            DetachSensorChipHandler(chip);
            _availableSensors.Remove(chip);
            _sensorChartSeriesBySensorIndex.Remove(sensorIndex);
            _sensorChartPointsBySensorIndex.Remove(sensorIndex);
            RefreshSensorChart();
        }
    }

    private void AttachSensorChipHandler(SensorChipModel chip)
    {
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SensorChipModel.IsSelected))
            {
                if (_suppressSensorSelectionReentry)
                {
                    return;
                }

                if (!chip.IsSelected && !_availableSensors.Any(static c => c.IsSelected))
                {
                    _suppressSensorSelectionReentry = true;
                    try { chip.IsSelected = true; }
                    finally { _suppressSensorSelectionReentry = false; }
                    return;
                }

                if (chip.IsSelected)
                {
                    EnsureTemperatureHistorySubscription(chip.SensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
                }
                RefreshSensorChart();
            }
            else if (args.PropertyName == nameof(SensorChipModel.DisplayName))
            {
                if (_sensorChartSeriesBySensorIndex.TryGetValue(chip.SensorIndex, out var series))
                {
                    series.Name = chip.DisplayName;
                }
            }
        };

        chip.PropertyChanged += handler;
        _sensorChipHandlers[chip] = handler;
    }

    private void DetachSensorChipHandler(SensorChipModel chip)
    {
        if (_sensorChipHandlers.Remove(chip, out var handler))
        {
            chip.PropertyChanged -= handler;
        }
    }

    private void RefreshSensorChart()
    {
        var selected = _availableSensors.Where(static c => c.IsSelected).ToArray();

        foreach (var chip in selected)
        {
            UpdateSensorChartPoints(chip.SensorIndex);
        }

        UpdateDrivingTemperaturePoints(selected);

        var series = new List<ISeries>(selected.Length + 1);
        foreach (var chip in selected)
        {
            series.Add(GetOrCreateSensorChartSeries(chip));
        }
        if (selected.Length > 0)
        {
            series.Add(GetOrCreateDrivingTemperatureSeries());
        }

        SensorChartSeries = [.. series];
        UpdateSensorChartAxis(selected);
    }

    private void UpdateDrivingTemperaturePoints(IReadOnlyList<SensorChipModel> selectedChips)
    {
        _drivingTemperaturePoints.Clear();
        if (selectedChips.Count == 0)
        {
            return;
        }

        var perSensor = new List<TelemetryPoint[]>(selectedChips.Count);
        foreach (var chip in selectedChips)
        {
            if (_temperatureHistoryPoints.TryGetValue(chip.SensorIndex, out var points) && points.Length > 0)
            {
                perSensor.Add(points);
            }
        }
        if (perSensor.Count == 0)
        {
            return;
        }

        var timestampSet = new SortedSet<DateTimeOffset>();
        foreach (var series in perSensor)
        {
            foreach (var p in series)
            {
                timestampSet.Add(p.ObservedAt);
            }
        }

        var aggregation = SelectedAggregation ?? TemperatureAggregationMode.Maximum;
        foreach (var timestamp in timestampSet)
        {
            var readings = new List<double>(perSensor.Count);
            foreach (var s in perSensor)
            {
                if (FindNearestValue(s, timestamp) is double value)
                {
                    readings.Add(value);
                }
            }
            if (readings.Count == 0) continue;

            var aggregated = aggregation switch
            {
                TemperatureAggregationMode.Average => readings.Average(),
                TemperatureAggregationMode.Maximum => readings.Max(),
                TemperatureAggregationMode.Minimum => readings.Min(),
                TemperatureAggregationMode.Median => ComputeMedian(readings),
                _ => readings.Average(),
            };

            _drivingTemperaturePoints.Add(new DateTimePoint(
                timestamp.LocalDateTime,
                _unitFormattingService.ConvertTemperature(aggregated)));
        }
    }

    private LineSeries<DateTimePoint> GetOrCreateDrivingTemperatureSeries()
    {
        if (_drivingTemperatureSeries is null)
        {
            _drivingTemperatureSeries = new LineSeries<DateTimePoint>
            {
                Values = _drivingTemperaturePoints,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
                Stroke = new SolidColorPaint(DrivingTemperatureColor, 3),
            };
        }

        _drivingTemperatureSeries.Name = $"Driving ({SelectedAggregation ?? TemperatureAggregationMode.Maximum})";
        return _drivingTemperatureSeries;
    }

    private void UpdateSensorChartPoints(int sensorIndex)
    {
        if (!_sensorChartPointsBySensorIndex.TryGetValue(sensorIndex, out var collection))
        {
            collection = [];
            _sensorChartPointsBySensorIndex[sensorIndex] = collection;
        }

        collection.Clear();
        if (_temperatureHistoryPoints.TryGetValue(sensorIndex, out var points))
        {
            foreach (var point in points)
            {
                collection.Add(new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    _unitFormattingService.ConvertTemperature(point.NumericValue)));
            }
        }
    }

    private ISeries GetOrCreateSensorChartSeries(SensorChipModel chip)
    {
        if (_sensorChartSeriesBySensorIndex.TryGetValue(chip.SensorIndex, out var existing))
        {
            existing.Name = chip.DisplayName;
            return existing;
        }

        var color = SensorChartPalette[chip.SensorIndex % SensorChartPalette.Length];
        var values = _sensorChartPointsBySensorIndex.GetValueOrDefault(chip.SensorIndex)
            ?? (_sensorChartPointsBySensorIndex[chip.SensorIndex] = []);

        var series = new LineSeries<DateTimePoint>
        {
            Values = values,
            Name = chip.DisplayName,
            Fill = null,
            GeometrySize = 5,
            GeometryFill = new SolidColorPaint(color),
            GeometryStroke = new SolidColorPaint(color, 2),
            LineSmoothness = 0.4,
            Stroke = new SolidColorPaint(color, 2),
        };

        _sensorChartSeriesBySensorIndex[chip.SensorIndex] = series;
        return series;
    }

    private void UpdateSensorChartAxis(IReadOnlyList<SensorChipModel> selectedChips)
    {
        if (selectedChips.Count == 0)
        {
            SensorChartMinLimit = null;
            SensorChartMaxLimit = null;
            SensorChartSeparators = [];
            return;
        }

        var timestamps = selectedChips
            .SelectMany(c => _sensorChartPointsBySensorIndex.TryGetValue(c.SensorIndex, out var col)
                ? col.Select(p => p.DateTime)
                : [])
            .Concat(_drivingTemperaturePoints.Select(p => p.DateTime))
            .OrderBy(t => t)
            .ToArray();

        var (axisStart, axisEnd, separators) = TimeChartAxisHelper.BuildAxis(
            timestamps,
            PresentationDefaults.RecentTelemetryHistoryWindow,
            PresentationDefaults.RecentTelemetrySeparatorStep);

        SensorChartMinLimit = axisStart.Ticks;
        SensorChartMaxLimit = axisEnd.Ticks;
        SensorChartSeparators = separators;
    }

    private void RemoveFanCard(int fanIndex)
    {
        if (!_fanCardsByIndex.Remove(fanIndex, out var fan))
        {
            return;
        }

        var wasSelected = ReferenceEquals(fan, SelectedFan);
        _fans.Remove(fan);

        if (_fanHistorySubscriptions.Remove(fanIndex, out var subscription))
        {
            subscription.Dispose();
        }
        _fanHistoryPoints.Remove(fanIndex);

        if (wasSelected)
        {
            SelectedFan = _fans.FirstOrDefault();
        }
    }

    private void UpdateDrivingSensors(FanCardModel fan)
    {
        fan.DrivingSensors = GetDrivingSensors(fan.ControlState);
    }

    private ImmutableArray<TemperatureTelemetrySnapshot> GetDrivingSensors(FanControlStateSnapshot? state)
    {
        if (state is null || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<TemperatureTelemetrySnapshot>(state.DrivingSensorIndices.Length);
        foreach (var sensorIndex in state.DrivingSensorIndices)
        {
            if (_temperatureSnapshots.TryGetValue(sensorIndex, out var snapshot))
            {
                builder.Add(snapshot);
            }
        }

        return builder.ToImmutable();
    }

    private void EnsureFanHistorySubscription(int fanIndex, TimeSpan range)
    {
        if (_fanHistorySubscriptions.ContainsKey(fanIndex))
        {
            return;
        }

        var subscription = _fanTelemetryClient
            .WatchFanHistory(fanIndex, range)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                _fanHistoryPoints[fanIndex] =
                [
                    .. pts
                        .OrderBy(p => p.ObservedAt)
                        .ThenBy(p => p.SampleId)
                ];

                RefreshFanHistory(fanIndex);
            });

        _fanHistorySubscriptions[fanIndex] = subscription;
        _subscriptions.Add(subscription);
    }

    private void RefreshFanHistory(int fanIndex)
    {
        if (!_fanCardsByIndex.TryGetValue(fanIndex, out var fan))
        {
            return;
        }

        if (!_fanHistoryPoints.TryGetValue(fanIndex, out var points) || points.Length == 0)
        {
            fan.FanSpeedHistory = [];
            return;
        }

        var converted = new DateTimePoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            converted[i] = new DateTimePoint(point.ObservedAt.LocalDateTime, _unitFormattingService.ConvertFanSpeed(point.SpeedRpm));
        }

        fan.FanSpeedHistory = converted;
    }

    private void EnsureTemperatureHistorySubscriptionsForFan(FanCardModel fan)
    {
        if (fan.ControlState is null || fan.ControlState.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var sensorIndex in fan.ControlState.DrivingSensorIndices)
        {
            EnsureTemperatureHistorySubscription(sensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
        }
    }

    private void EnsureTemperatureHistorySubscription(int sensorIndex, TimeSpan range)
    {
        if (_temperatureHistorySubscriptions.ContainsKey(sensorIndex))
        {
            return;
        }

        var subscription = _temperatureTelemetryClient
            .WatchTemperatureHistory(sensorIndex, range)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                _temperatureHistoryPoints[sensorIndex] =
                [
                    .. pts
                        .OrderBy(p => p.ObservedAt)
                        .ThenBy(p => p.SampleId)
                ];

                RefreshDrivingTemperatureHistoryForSensor(sensorIndex);
            });

        _temperatureHistorySubscriptions[sensorIndex] = subscription;
        _subscriptions.Add(subscription);
    }

    private void StopTemperatureHistorySubscription(int sensorIndex)
    {
        if (_temperatureHistorySubscriptions.Remove(sensorIndex, out var subscription))
        {
            subscription.Dispose();
        }
        _temperatureHistoryPoints.Remove(sensorIndex);
    }

    private void RefreshDrivingTemperatureHistoryForSensor(int sensorIndex)
    {
        foreach (var fan in _fans)
        {
            if (fan.ControlState is { } state
                && !state.DrivingSensorIndices.IsDefaultOrEmpty
                && state.DrivingSensorIndices.Contains(sensorIndex))
            {
                RefreshDrivingTemperatureHistory(fan);
            }
        }

        if (_sensorChipIndex.TryGetValue(sensorIndex, out var chip) && chip.IsSelected)
        {
            UpdateSensorChartPoints(sensorIndex);
            UpdateSensorChartAxis(_availableSensors.Where(static c => c.IsSelected).ToArray());
        }
    }

    private void RefreshAllDrivingTemperatureHistory()
    {
        foreach (var fan in _fans)
        {
            RefreshDrivingTemperatureHistory(fan);
        }
    }

    private void RefreshDrivingTemperatureHistory(FanCardModel fan)
    {
        var state = fan.ControlState;
        if (state is null || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            fan.DrivingTemperatureHistory = [];
            return;
        }

        var perSensor = new List<TelemetryPoint[]>(state.DrivingSensorIndices.Length);
        foreach (var sensorIndex in state.DrivingSensorIndices)
        {
            if (_temperatureHistoryPoints.TryGetValue(sensorIndex, out var points) && points.Length > 0)
            {
                perSensor.Add(points);
            }
        }

        if (perSensor.Count == 0)
        {
            fan.DrivingTemperatureHistory = [];
            return;
        }

        var timestampSet = new SortedSet<DateTimeOffset>();
        foreach (var series in perSensor)
        {
            foreach (var point in series)
            {
                timestampSet.Add(point.ObservedAt);
            }
        }

        var aggregation = state.DrivingTemperatureAggregation;
        var output = new List<DateTimePoint>(timestampSet.Count);

        foreach (var timestamp in timestampSet)
        {
            var readings = new List<double>(perSensor.Count);
            foreach (var series in perSensor)
            {
                var nearest = FindNearestValue(series, timestamp);
                if (nearest is double value)
                {
                    readings.Add(value);
                }
            }

            if (readings.Count == 0) continue;

            var aggregated = aggregation switch
            {
                TemperatureAggregationMode.Average => readings.Average(),
                TemperatureAggregationMode.Maximum => readings.Max(),
                TemperatureAggregationMode.Minimum => readings.Min(),
                TemperatureAggregationMode.Median => ComputeMedian(readings),
                _ => readings.Average(),
            };

            output.Add(new DateTimePoint(timestamp.LocalDateTime, _unitFormattingService.ConvertTemperature(aggregated)));
        }

        fan.DrivingTemperatureHistory = output.ToArray();
    }

    private static double? FindNearestValue(TelemetryPoint[] series, DateTimeOffset timestamp)
    {
        // Linear scan: telemetry history windows are short, so this is cheap and avoids needing a BCL bisect.
        double? best = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var point in series)
        {
            var delta = (point.ObservedAt - timestamp).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = point.NumericValue;
            }
        }
        return best;
    }

    private static double ComputeMedian(List<double> readings)
    {
        readings.Sort();
        var mid = readings.Count / 2;
        return readings.Count % 2 == 0 ? (readings[mid - 1] + readings[mid]) / 2d : readings[mid];
    }

    private void InsertSorted(FanCardModel fan)
    {
        var insertIndex = 0;
        while (insertIndex < _fans.Count && _fans[insertIndex].Snapshot.FanIndex < fan.Snapshot.FanIndex)
        {
            insertIndex++;
        }
        _fans.Insert(insertIndex, fan);
    }

    private void ReportStatus(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusVisible = true;
    }

    public void Dispose()
    {
        foreach (var subscription in _fanHistorySubscriptions.Values)
        {
            subscription.Dispose();
        }
        _fanHistorySubscriptions.Clear();

        foreach (var subscription in _temperatureHistorySubscriptions.Values)
        {
            subscription.Dispose();
        }
        _temperatureHistorySubscriptions.Clear();

        _curvePoints.CollectionChanged -= OnCurvePointsCollectionChanged;
        foreach (var point in _curvePoints)
        {
            point.PropertyChanged -= OnCurvePointPropertyChanged;
        }

        foreach (var chip in _sensorChipHandlers.Keys.ToArray())
        {
            DetachSensorChipHandler(chip);
        }

        _subscriptions.Dispose();
    }
}
