using System.ComponentModel;

using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

using SubZeroFramework.Controls.FanCurveProfiles.Models;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Custom-curve editor section: the draggable curve chart (add / move / remove points by pointer),
/// the driving-temperature sensor selector with aggregation, and the driving-temperature history chart
/// plus its legend. Owns all of the page's direct-manipulation pointer logic; bound to the shared
/// <see cref="FanCurveProfilesModel"/> via <see cref="ViewModel"/>.
/// </summary>
public sealed partial class FanCurveEditorView : UserControl, INotifyPropertyChanged
{
    private const double DragHitTemperatureRadius = 4.5d;
    private const double DragHitDutyRadius = 7.5d;

    private CurvePointModel? _draggingPoint;

    public FanCurveEditorView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "UserControl exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation pushes the host-supplied ViewModel into the bindings.")]
    public FanCurveProfilesModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    private bool TryGetChartData(PointerRoutedEventArgs e, out double temperature, out double duty)
    {
        temperature = 0d;
        duty = 0d;

        var chart = (ICartesianChartView)CurveChart;
        var position = e.GetCurrentPoint(CurveChart).Position;
        var scaled = chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));

        if (double.IsNaN(scaled.X) || double.IsNaN(scaled.Y))
        {
            return false;
        }

        temperature = scaled.X;
        duty = scaled.Y;
        return true;
    }

    private void CurveChart_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.Pointer.PointerDeviceType is PointerDeviceType.Mouse or PointerDeviceType.Pen)
        {
            var properties = e.GetCurrentPoint(CurveChart).Properties;
            if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
            {
                return;
            }
        }

        if (!TryGetChartData(e, out var temperature, out var duty))
        {
            return;
        }

        var existing = ViewModel.FindNearestCurvePoint(temperature, duty, DragHitTemperatureRadius, DragHitDutyRadius);
        if (existing is not null)
        {
            _draggingPoint = existing;
            CurveChart.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        ViewModel.AddCurvePointAt(temperature, duty);
        e.Handled = true;
    }

    private void CurveChart_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingPoint is null || ViewModel is null) return;
        if (!TryGetChartData(e, out var temperature, out var duty)) return;

        ViewModel.UpdateCurvePoint(_draggingPoint, temperature, duty);
        e.Handled = true;
    }

    private void CurveChart_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingPoint is null) return;
        CurveChart.ReleasePointerCapture(e.Pointer);
        _draggingPoint = null;
        e.Handled = true;
    }

    private void CurveChart_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel is null) return;

        var chart = (ICartesianChartView)CurveChart;
        var position = e.GetPosition(CurveChart);
        var scaled = chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));
        if (double.IsNaN(scaled.X) || double.IsNaN(scaled.Y)) return;

        var existing = ViewModel.FindNearestCurvePoint(scaled.X, scaled.Y, DragHitTemperatureRadius, DragHitDutyRadius);
        if (existing is not null && ViewModel.RemoveCurvePointCommand.CanExecute(existing))
        {
            ViewModel.RemoveCurvePointCommand.Execute(existing);
            e.Handled = true;
        }
    }
}
