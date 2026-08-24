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

    /// <summary>
    /// Reads the pointer position as CANONICAL Celsius and percent.
    /// </summary>
    /// <remarks>
    /// The chart plots in DISPLAY units, so <c>ScalePixelsToData</c> hands back the user's units — °F, or a
    /// 0–1 duty fraction. Everything downstream (FanCurveDomain.ClampTemperature, the draft, the EC) speaks
    /// canonical, so the inverse conversion happens HERE, once, at the single point where pointer input
    /// enters the model. Skipping it would store a point dragged to the tick reading 150 °F as 150 °C.
    /// </remarks>
    private bool TryGetChartData(PointerRoutedEventArgs e, out double temperature, out double duty)
        => TryScaleToCanonical(e.GetCurrentPoint(CurveChart).Position, out temperature, out duty);

    private bool TryScaleToCanonical(Windows.Foundation.Point position, out double temperature, out double duty)
    {
        temperature = 0d;
        duty = 0d;

        var chart = (ICartesianChartView)CurveChart;
        var scaled = chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));

        if (double.IsNaN(scaled.X) || double.IsNaN(scaled.Y) || ViewModel?.CurveChart is not { } curveChart)
        {
            return false;
        }

        temperature = curveChart.ToCanonicalTemperature(scaled.X);
        duty = curveChart.ToCanonicalDuty(scaled.Y);
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

        // A miss adds a point at the press position and picks it up in the same gesture, so press-drag places
        // a point in one motion. The pointer keeps producing data coordinates out in the axis margin, past the
        // plotted range — the draft snaps every add / move into its editable band, so a point can never land
        // where it is neither visible nor grabbable.
        var existing = ViewModel.FindNearestCurvePoint(temperature, duty, DragHitTemperatureRadius, DragHitDutyRadius);
        _draggingPoint = existing ?? ViewModel.AddCurvePointAt(temperature, duty);
        CurveChart.CapturePointer(e.Pointer);
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

        // Same canonical inverse as the drag path — a right-tap that skipped it would hit-test Fahrenheit
        // coordinates against Celsius points and delete whatever happened to be near the wrong place.
        if (!TryScaleToCanonical(e.GetPosition(CurveChart), out var temperature, out var duty)) return;

        var existing = ViewModel.FindNearestCurvePoint(temperature, duty, DragHitTemperatureRadius, DragHitDutyRadius);
        if (existing is not null && ViewModel.RemoveCurvePointCommand.CanExecute(existing))
        {
            ViewModel.RemoveCurvePointCommand.Execute(existing);
            e.Handled = true;
        }
    }
}
