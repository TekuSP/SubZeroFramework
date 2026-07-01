using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

namespace SubZeroFramework.Controls.Power;

/// <summary>Three chevrons with a looping bright pulse conveying power-flow direction. <see cref="IsActive"/>
/// toggles the pulse (static dim when off, e.g. no adapter or a full battery); <see cref="Reversed"/> mirrors the
/// arrows + pulse to point the other way (e.g. battery→system while discharging).</summary>
public sealed partial class FlowArrowsView : UserControl
{
    public FlowArrowsView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent),
        typeof(Brush),
        typeof(FlowArrowsView),
        new PropertyMetadata(new SolidColorBrush(ColorHelper.FromArgb(255, 160, 163, 186))));

    /// <summary>Chevron colour (e.g. secondary for the adapter→system gap, green for system→battery).</summary>
    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(FlowArrowsView),
        new PropertyMetadata(true, OnStateChanged));

    /// <summary>When true the chevrons pulse (power is flowing); when false they sit static and dim (no flow).</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty ReversedProperty = DependencyProperty.Register(
        nameof(Reversed),
        typeof(bool),
        typeof(FlowArrowsView),
        new PropertyMetadata(false, OnStateChanged));

    /// <summary>When true the arrows (and the travelling pulse) are mirrored to point the opposite way.</summary>
    public bool Reversed
    {
        get => (bool)GetValue(ReversedProperty);
        set => SetValue(ReversedProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FlowArrowsView)d).ApplyState();

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyState();

    private void OnUnloaded(object sender, RoutedEventArgs e) => FlowStoryboard.Stop();

    private void ApplyState()
    {
        // Mirror the whole strip to reverse the arrow direction and the pulse's travel.
        Root.RenderTransformOrigin = new Point(0.5, 0.5);
        Root.RenderTransform = Reversed ? new ScaleTransform { ScaleX = -1 } : null;

        if (IsActive)
        {
            FlowStoryboard.Begin();
        }
        else
        {
            // Stop reverts each chevron to its static base opacity (0.3), leaving them dim + still.
            FlowStoryboard.Stop();
        }
    }
}
