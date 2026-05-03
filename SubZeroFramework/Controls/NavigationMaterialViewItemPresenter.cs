using Material.Icons;

namespace SubZeroFramework.Controls;

/// <summary>
/// Presents compact and expanded material icon navigation item layouts without transform offsets.
/// </summary>
public sealed class NavigationMaterialViewItemPresenter : Control
{
    /// <summary>
    /// Identifies the <see cref="Kind"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(MaterialIconKind?),
        typeof(NavigationMaterialViewItemPresenter),
        new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="IconSize"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(NavigationMaterialViewItemPresenter),
        new PropertyMetadata(20d));

    /// <summary>
    /// Identifies the <see cref="IsCompact"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(NavigationMaterialViewItemPresenter),
        new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="Spacing"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(NavigationMaterialViewItemPresenter),
        new PropertyMetadata(14.5d));

    /// <summary>
    /// Identifies the <see cref="Text"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(NavigationMaterialViewItemPresenter),
        new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationMaterialViewItemPresenter"/> class.
    /// </summary>
    public NavigationMaterialViewItemPresenter()
    {
        DefaultStyleKey = typeof(NavigationMaterialViewItemPresenter);
    }

    /// <summary>
    /// Gets or sets the material icon kind.
    /// </summary>
    public MaterialIconKind? Kind
    {
        get => (MaterialIconKind?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon size.
    /// </summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the compact layout is active.
    /// </summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between the icon and text in expanded mode.
    /// </summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
