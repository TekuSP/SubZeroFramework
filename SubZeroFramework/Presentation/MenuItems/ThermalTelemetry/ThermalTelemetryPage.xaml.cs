using System.Collections.Specialized;
using System.ComponentModel;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

using SubZeroFramework.Presentation.MenuItems.Dashboard;

namespace SubZeroFramework.Presentation.MenuItems.ThermalTelemetry;

public sealed partial class ThermalTelemetryPage : Page, INotifyPropertyChanged
{
    private readonly Dictionary<ThermalSensorModel, PropertyChangedEventHandler> _sensorHandlers = [];
    private INotifyCollectionChanged? _sensorCollection;
    private double[] _thermalHistorySeparators = [];
    private double? _thermalHistoryMinLimit;
    private double? _thermalHistoryMaxLimit;

    public ThermalTelemetryPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThermalTelemetryModel? ViewModel
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

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

        if (args.NewValue is ThermalTelemetryModel model)
        {
            ViewModel = model;
            AttachViewModel(model);
            return;
        }

        ViewModel = null;
        ThermalHistoryChart.Series = [];
    }

    private void AttachViewModel(ThermalTelemetryModel model)
    {
        model.PropertyChanged += ViewModel_PropertyChanged;
        _sensorCollection = model.Sensors;
        _sensorCollection.CollectionChanged += SensorCollection_CollectionChanged;
        AttachSensorHandlers(model.Sensors);
        RefreshThermalHistoryChart();
    }

    private void DetachViewModel(ThermalTelemetryModel model)
    {
        model.PropertyChanged -= ViewModel_PropertyChanged;

        if (_sensorCollection is not null)
        {
            _sensorCollection.CollectionChanged -= SensorCollection_CollectionChanged;
            _sensorCollection = null;
        }

        DetachSensorHandlers();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ThermalTelemetryModel.SelectedHistoryWindowIndex)
            || args.PropertyName == nameof(ThermalTelemetryModel.SelectedHistoryWindow))
        {
            UpdateThermalHistoryAxis();
        }
    }

    private void SensorCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            DetachSensorHandlers();
            if (ViewModel is not null)
            {
                AttachSensorHandlers(ViewModel.Sensors);
            }

            RefreshThermalHistoryChart();
            return;
        }

        if (e.OldItems is not null)
        {
            DetachSensorHandlers(e.OldItems.OfType<ThermalSensorModel>());
        }

        if (e.NewItems is not null)
        {
            AttachSensorHandlers(e.NewItems.OfType<ThermalSensorModel>());
        }

        RefreshThermalHistoryChart();
    }

    private void AttachSensorHandlers(IEnumerable<ThermalSensorModel> sensors)
    {
        foreach (var sensor in sensors)
        {
            if (_sensorHandlers.ContainsKey(sensor))
            {
                continue;
            }

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
            _sensorHandlers[sensor] = handler;
        }
    }

    private void DetachSensorHandlers(IEnumerable<ThermalSensorModel> sensors)
    {
        foreach (var sensor in sensors)
        {
            if (_sensorHandlers.Remove(sensor, out var handler))
            {
                sensor.PropertyChanged -= handler;
            }
        }
    }

    private void DetachSensorHandlers()
    {
        DetachSensorHandlers(_sensorHandlers.Keys.ToArray());
    }

    private void RefreshThermalHistoryChart()
    {
        if (ViewModel is null)
        {
            ThermalHistoryChart.Series = [];
            return;
        }

        var selectedSensors = GetSelectedSensors();

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

        UpdateThermalHistoryAxis(GetSelectedSensors());
    }

    private ThermalSensorModel[] GetSelectedSensors()
    {
        if (ViewModel is null)
        {
            return [];
        }

        return [.. ViewModel.Sensors.Where(sensor => sensor.IsSelected)];
    }

    private void UpdateThermalHistoryAxis(IEnumerable<ThermalSensorModel> selectedSensors)
    {
        var historyWindow = ViewModel?.SelectedHistoryWindow ?? TimeSpan.FromHours(1);
        var historyPoints = selectedSensors
            .SelectMany(sensor => sensor.OverviewTemperatureHistory)
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var axisEnd = historyPoints.Length == 0
            ? DateTime.Now
            : historyPoints[^1] > DateTime.Now ? historyPoints[^1] : DateTime.Now;

        var earliestPoint = historyPoints.Length == 0
            ? axisEnd - historyWindow
            : historyPoints[0];

        var axisStart = earliestPoint < axisEnd - historyWindow
            ? axisEnd - historyWindow
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
