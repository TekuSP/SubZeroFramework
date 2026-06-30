using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Controls.Power;

/// <summary>Three chevrons with a looping bright pulse that travels left→right, used between the power-flow stats.</summary>
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

    private void OnLoaded(object sender, RoutedEventArgs e) => FlowStoryboard.Begin();

    private void OnUnloaded(object sender, RoutedEventArgs e) => FlowStoryboard.Stop();
}
