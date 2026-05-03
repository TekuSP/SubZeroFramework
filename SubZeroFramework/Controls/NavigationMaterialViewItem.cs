using Material.Icons;

namespace SubZeroFramework.Controls;

/// <summary>
/// A <see cref="NavigationViewItem"/> that renders its content using <see cref="NavigationMaterialViewItemPresenter"/>.
/// </summary>
public class NavigationMaterialViewItem : NavigationViewItem
{
    private const double CompactWidthThreshold = 56;

    private readonly NavigationMaterialViewItemPresenter _content = new()
    {
        Spacing = 14.5,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        FontSize = 14
    };

    public NavigationMaterialViewItem()
    {
        SizeChanged += NavigationMaterialViewItem_SizeChanged;
        Content = _content;
    }

    private void NavigationMaterialViewItem_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateLayoutMode(args.NewSize.Width);
    }

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
    public string? MaterialIconKindName
    {
        get => (string?)GetValue(MaterialIconKindNameProperty);
        set => SetValue(MaterialIconKindNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush applied to the generated content.
    /// </summary>
    public Brush? MaterialIconForeground
    {
        get => (Brush?)GetValue(MaterialIconForegroundProperty);
        set => SetValue(MaterialIconForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the size applied to the generated icon.
    /// </summary>
    public double MaterialIconSize
    {
        get => (double)GetValue(MaterialIconSizeProperty);
        set => SetValue(MaterialIconSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the text displayed beside the material icon.
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateIcon();
        UpdateLayoutMode(ActualWidth);
    }

    private static void OnMaterialIconPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((NavigationMaterialViewItem)dependencyObject).UpdateIcon();
    }

    private void UpdateIcon()
    {
        if (string.IsNullOrWhiteSpace(MaterialIconKindName))
        {
            _content.Kind = null;
            _content.Text = Text;
            return;
        }

        if (!Enum.TryParse<MaterialIconKind>(MaterialIconKindName, ignoreCase: true, out var parsedKind))
        {
            throw new ArgumentException($"Unknown material icon kind '{MaterialIconKindName}'.", nameof(MaterialIconKindName));
        }

        _content.Kind = parsedKind;
        _content.Text = Text;
        _content.FontFamily = FontFamily;
        _content.FontWeight = FontWeight;

        if (MaterialIconForeground is not null)
        {
            _content.Foreground = MaterialIconForeground;
        }
        else
        {
            _content.ClearValue(ForegroundProperty);
        }

        _content.IconSize = double.IsNaN(MaterialIconSize) ? 20 : MaterialIconSize;
    }

    private void UpdateLayoutMode(double width)
    {
        _content.IsCompact = width <= CompactWidthThreshold;
    }
}
