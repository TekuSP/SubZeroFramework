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
        AttachThermalSensorHandlers(model.ThermalSensors);
        RefreshThermalHistoryChart();
    }

    private void DetachViewModel(DashboardModel model)
    {
        model.PropertyChanged -= ViewModel_PropertyChanged;
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

        if (args.PropertyName is nameof(DashboardModel.ThermalSensors)
            or nameof(DashboardModel.VisibleThermalSensors)
            or nameof(DashboardModel.ShowInactiveThermalSensors))
        {
            RefreshThermalHistoryChart();
        }
    }

    private void AttachThermalSensorHandlers(IEnumerable<ThermalSensorModel> sensors)
    {
        foreach (var sensor in sensors)
        {
            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName is nameof(ThermalSensorModel.IsSelected)
                    or nameof(ThermalSensorModel.OverviewTemperatureHistory)
                    or nameof(ThermalSensorModel.ShouldShowByDefault))
                {
                    RefreshThermalHistoryChart();
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

        var selectedSensors = ViewModel.ThermalSensors
            .Where(sensor => ViewModel.VisibleThermalSensors.Contains(sensor))
            .Where(sensor => sensor.IsSelected && sensor.OverviewTemperatureHistory.Length > 0)
            .Select(CreateSeries)
            .ToArray();

        ThermalHistoryChart.Series = selectedSensors;
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
