using System.Collections.Specialized;
using System.ComponentModel;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class DashboardPage : Page, INotifyPropertyChanged
{
    private readonly Dictionary<ThermalSensorModel, PropertyChangedEventHandler> _thermalSensorHandlers = [];
    private INotifyCollectionChanged? _visibleThermalSensorsCollection;
    private double[] _thermalHistorySeparators = [];
    private double? _thermalHistoryMinLimit;
    private double? _thermalHistoryMaxLimit;

    public DashboardPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardModel? ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    }

    public Func<double, string> ThermalLabelFormatter { get; } = static value => $"{value:N0}°C";

    public Func<DateTime, string> ThermalHistoryDateFormatter { get; } = ThermalSensorModel.Formatter;

    public double[] ThermalHistorySeparators
    {
        get => _thermalHistorySeparators;
        private set
        {
            _thermalHistorySeparators = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThermalHistorySeparators)));
        }
    }

    public double? ThermalHistoryMinLimit
    {
        get => _thermalHistoryMinLimit;
        private set
        {
            _thermalHistoryMinLimit = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThermalHistoryMinLimit)));
        }
    }

    public double? ThermalHistoryMaxLimit
    {
        get => _thermalHistoryMaxLimit;
        private set
        {
            _thermalHistoryMaxLimit = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThermalHistoryMaxLimit)));
        }
    }

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (ViewModel is not null)
        {
            DetachViewModel(ViewModel);
        }

        if (args.NewValue is DashboardModel model)
        {
            ViewModel = model;
            AttachViewModel(model);
            return;
        }

        ViewModel = null;
        ThermalHistoryChart.Series = [];
    }

    private void AttachViewModel(DashboardModel model)
    {
        model.PropertyChanged += ViewModel_PropertyChanged;
        _visibleThermalSensorsCollection = model.VisibleThermalSensors;
        _visibleThermalSensorsCollection.CollectionChanged += VisibleThermalSensors_CollectionChanged;
        AttachThermalSensorHandlers(model.ThermalSensors);
        RefreshThermalHistoryChart();
    }

    private void DetachViewModel(DashboardModel model)
    {
        model.PropertyChanged -= ViewModel_PropertyChanged;
        if (_visibleThermalSensorsCollection is not null)
        {
            _visibleThermalSensorsCollection.CollectionChanged -= VisibleThermalSensors_CollectionChanged;
            _visibleThermalSensorsCollection = null;
        }

        DetachThermalSensorHandlers();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (args.PropertyName == nameof(DashboardModel.ThermalSensors))
        {
            DetachThermalSensorHandlers();
            AttachThermalSensorHandlers(ViewModel.ThermalSensors);
        }
    }

    private void VisibleThermalSensors_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshThermalHistoryChart();
    }

    private void AttachThermalSensorHandlers(IEnumerable<ThermalSensorModel> sensors)
    {
        foreach (var sensor in sensors)
        {
            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName == nameof(ThermalSensorModel.IsSelected))
                {
                    RefreshThermalHistoryChart();
                    return;
                }

                if (args.PropertyName == nameof(ThermalSensorModel.OverviewTemperatureHistory))
                {
                    UpdateThermalHistoryAxis();
                }
            };

            sensor.PropertyChanged += handler;
            _thermalSensorHandlers[sensor] = handler;
        }
    }

    private void DetachThermalSensorHandlers()
    {
        foreach (var (sensor, handler) in _thermalSensorHandlers)
        {
            sensor.PropertyChanged -= handler;
        }

        _thermalSensorHandlers.Clear();
    }

    private void RefreshThermalHistoryChart()
    {
        if (ViewModel is null)
        {
            ThermalHistoryChart.Series = [];
            return;
        }

        var selectedSensors = GetSelectedThermalSensors();

        UpdateThermalHistoryAxis(selectedSensors);

        ThermalHistoryChart.Series = selectedSensors.Select(CreateSeries).ToArray();
    }

    private void UpdateThermalHistoryAxis()
    {
        if (ViewModel is null)
        {
            ThermalHistoryMinLimit = null;
            ThermalHistoryMaxLimit = null;
            ThermalHistorySeparators = [];
            return;
        }

        UpdateThermalHistoryAxis(GetSelectedThermalSensors());
    }

    private ThermalSensorModel[] GetSelectedThermalSensors()
    {
        if (ViewModel is null)
        {
            return [];
        }

        return [.. ViewModel.ThermalSensors
            .Where(sensor => ViewModel.VisibleThermalSensors.Contains(sensor) && sensor.IsSelected)];
    }

    private void UpdateThermalHistoryAxis(IEnumerable<ThermalSensorModel> selectedSensors)
    {
        var historyPoints = selectedSensors
            .SelectMany(sensor => sensor.OverviewTemperatureHistory)
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
        var separatorStep = GetThermalHistorySeparatorStep(visibleSpan);

        ThermalHistoryMinLimit = axisStart.Ticks;
        ThermalHistoryMaxLimit = axisEnd.Ticks;

        List<double> separators = [axisStart.Ticks];
        for (var tick = axisStart + separatorStep; tick < axisEnd; tick += separatorStep)
        {
            separators.Add(tick.Ticks);
        }

        if (separators.Count == 0 || separators[^1] != axisEnd.Ticks)
        {
            separators.Add(axisEnd.Ticks);
        }

        ThermalHistorySeparators = [.. separators];
    }

    private static TimeSpan GetThermalHistorySeparatorStep(TimeSpan visibleSpan)
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

    private static ISeries CreateSeries(ThermalSensorModel sensor)
    {
        var strokeColor = SKColor.Parse(sensor.HistoryStrokeHex);

        return new LineSeries<DateTimePoint>
        {
            Values = sensor.OverviewTemperatureHistory,
            Name = sensor.DisplayName,
            Fill = null,
            GeometrySize = 6,
            GeometryFill = new SolidColorPaint(strokeColor),
            GeometryStroke = new SolidColorPaint(strokeColor, 2),
            LineSmoothness = 0.6,
            Stroke = new SolidColorPaint(strokeColor, 3),
        };
    }
}
