using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Settings.Models;

namespace SubZeroFramework.Controls.Settings;

/// <summary>
/// One Display-units row (icon + name + live sample subtitle + segmented unit pills). The selected pill
/// applies immediately through the row model's owner; there is no separate save step.
/// </summary>
public sealed partial class UnitSegmentPickerView : UserControl
{
    public UnitSegmentPickerView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item),
        typeof(UnitPreferenceRowModel),
        typeof(UnitSegmentPickerView),
        new PropertyMetadata(null));

    /// <summary>The row model rendered by this picker.</summary>
    public UnitPreferenceRowModel? Item
    {
        get => (UnitPreferenceRowModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UnitPreferenceSegmentModel segment)
        {
            segment.Select();
        }
    }
}
