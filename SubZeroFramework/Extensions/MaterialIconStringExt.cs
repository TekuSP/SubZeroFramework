using Material.Icons;
using Material.Icons.UNO;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Extensions;

/// <summary>
/// Creates a <see cref="MaterialIcon"/> from a material icon name string.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(MaterialIcon))]
public sealed class MaterialIconStringExt : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialIconStringExt"/> class.
    /// </summary>
    public MaterialIconStringExt() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialIconStringExt"/> class.
    /// </summary>
    /// <param name="kind">The material icon name to parse.</param>
    public MaterialIconStringExt(string kind)
    {
        StringKind = kind;
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialIconStringExt"/> class.
    /// </summary>
    /// <param name="kind">The material icon name to parse.</param>
    public MaterialIconStringExt(MaterialIconKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets or sets the icon animation to play.
    /// </summary>
    public MaterialIconAnimation Animation { get; set; }

    /// <summary>
    /// Gets or sets the optional horizontal alignment.
    /// </summary>
    public HorizontalAlignment? HorizontalAlignment { get; set; }

    /// <summary>
    /// Gets or sets the optional foreground brush.
    /// </summary>
    public Brush? IconForeground { get; set; }

    /// <summary>
    /// Gets or sets the optional icon size.
    /// </summary>
    public double? IconSize { get; set; }

    /// <summary>
    /// Gets or sets the material icon name to parse.
    /// </summary>
    public string? StringKind { get; set; }

    /// <summary>
    /// Gets or sets the material icon name to parse.
    /// </summary>
    public MaterialIconKind? Kind { get; set; }

    /// <summary>
    /// Gets or sets the optional vertical alignment.
    /// </summary>
    public VerticalAlignment? VerticalAlignment { get; set; }

    /// <inheritdoc />
    protected override object ProvideValue(IXamlServiceProvider serviceProvider)
    {
        if (Kind is null)
            Kind = Enum.TryParse<MaterialIconKind>(string.IsNullOrWhiteSpace(StringKind) ? throw new ArgumentException("The material icon kind string must not be null, empty, or whitespace.", nameof(StringKind)) : StringKind, ignoreCase: true, out var parsedKind) ? parsedKind : throw new ArgumentException($"Unknown material icon kind '{StringKind}'.", nameof(StringKind));

        var result = new MaterialIcon
        {
            Kind = Kind.Value,
            Animation = Animation,
        };

        result.Height = IconSize ?? result.Height;
        result.Width = IconSize ?? result.Width;
        result.Foreground = IconForeground ?? result.Foreground;
        result.VerticalAlignment = VerticalAlignment ?? result.VerticalAlignment;
        result.HorizontalAlignment = HorizontalAlignment ?? result.HorizontalAlignment;

        return result;
    }
}
