using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SubZeroFramework.Controls;

/// <summary>
/// The shared instance-picker side panel (PACKAGES / DETECTED DRIVES / INSTALLED MODULES…): an uppercase title
/// over a single-select list, on the full-height card chrome. Selection members delegate to the inner
/// <see cref="ListView"/> so page code-behind can drive data navigation from <see cref="SelectionChanged"/>.
/// </summary>
public sealed partial class InstancePickerView : UserControl
{
    public InstancePickerView()
    {
        this.InitializeComponent();
        List.Items.VectorChanged += (_, _) => UpdateEmptyTextVisibility();
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(InstancePickerView),
        new PropertyMetadata(string.Empty));

    /// <summary>Panel title, shown uppercase-styled (e.g. "PACKAGES").</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(InstancePickerView),
        new PropertyMetadata(null, static (sender, _) => ((InstancePickerView)sender).UpdateEmptyTextVisibility()));

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText),
        typeof(string),
        typeof(InstancePickerView),
        new PropertyMetadata(string.Empty, static (sender, _) => ((InstancePickerView)sender).UpdateEmptyTextVisibility()));

    /// <summary>Muted placeholder shown centered in the panel while the list has no items (e.g. "None connected");
    /// empty (the default) disables the placeholder.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(InstancePickerView),
        new PropertyMetadata(null));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Raised when the list selection changes; mirrors <see cref="ListView.SelectionChanged"/>.</summary>
    public event SelectionChangedEventHandler? SelectionChanged;

    /// <summary>Currently selected instance (the bound item model), or null.</summary>
    public object? SelectedItem
    {
        get => List.SelectedItem;
        set => List.SelectedItem = value;
    }

    /// <summary>Selected index; -1 when nothing is selected.</summary>
    public int SelectedIndex
    {
        get => List.SelectedIndex;
        set => List.SelectedIndex = value;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, e);

    private void UpdateEmptyTextVisibility() =>
        EmptyTextBlock.Visibility = !string.IsNullOrEmpty(EmptyText) && List.Items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
}
