using Material.Icons;
using Material.Icons.UNO;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Controls;

/// <summary>
/// A <see cref="NavigationViewItem"/> that renders its content using <see cref="MaterialIconText"/>.
/// </summary>
public sealed class NavigationMaterialViewItem : NavigationViewItem {
    /// <summary>
    /// Identifies the <see cref="MaterialIconKindName"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty MaterialIconKindNameProperty = DependencyProperty.Register(
        nameof(MaterialIconKindName),
        typeof(string),
        typeof(NavigationMaterialViewItem),
        new PropertyMetadata(null, OnMaterialIconPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="MaterialIconForeground"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty MaterialIconForegroundProperty = DependencyProperty.Register(
        nameof(MaterialIconForeground),
        typeof(Brush),
        typeof(NavigationMaterialViewItem),
        new PropertyMetadata(null, OnMaterialIconPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="MaterialIconSize"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty MaterialIconSizeProperty = DependencyProperty.Register(
        nameof(MaterialIconSize),
        typeof(double),
        typeof(NavigationMaterialViewItem),
        new PropertyMetadata(double.NaN, OnMaterialIconPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="Text"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(NavigationMaterialViewItem),
        new PropertyMetadata(string.Empty, OnMaterialIconPropertyChanged));

    /// <summary>
    /// Gets or sets the material icon kind name.
    /// </summary>
    public string? MaterialIconKindName {
        get => (string?)GetValue(MaterialIconKindNameProperty);
        set => SetValue(MaterialIconKindNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush applied to the generated content.
    /// </summary>
    public Brush? MaterialIconForeground {
        get => (Brush?)GetValue(MaterialIconForegroundProperty);
        set => SetValue(MaterialIconForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the size applied to the generated icon.
    /// </summary>
    public double MaterialIconSize {
        get => (double)GetValue(MaterialIconSizeProperty);
        set => SetValue(MaterialIconSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the text displayed beside the material icon.
    /// </summary>
    public string Text {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate() {
        base.OnApplyTemplate();
        UpdateIcon();
    }

    private static void OnMaterialIconPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) {
        ((NavigationMaterialViewItem)dependencyObject).UpdateIcon();
    }

    private void UpdateIcon() {
        Icon = null;

        if (string.IsNullOrWhiteSpace(MaterialIconKindName)) {
            Content = Text;
            return;
        }

        if (!Enum.TryParse<MaterialIconKind>(MaterialIconKindName, ignoreCase: true, out var parsedKind)) {
            throw new ArgumentException($"Unknown material icon kind '{MaterialIconKindName}'.", nameof(MaterialIconKindName));
        }

        var content = new MaterialIconText {
            Kind = parsedKind,
            Text = Text
        };

        if (MaterialIconForeground is not null) {
            content.Foreground = MaterialIconForeground;
        }

        if (!double.IsNaN(MaterialIconSize)) {
            content.IconSize = MaterialIconSize;
        }

        Content = content;
    }
}
