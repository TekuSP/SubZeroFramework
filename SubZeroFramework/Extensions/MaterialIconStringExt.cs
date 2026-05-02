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
public sealed class MaterialIconStringExt : MarkupExtension {
    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialIconStringExt"/> class.
    /// </summary>
    public MaterialIconStringExt() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialIconStringExt"/> class.
    /// </summary>
    /// <param name="kind">The material icon name to parse.</param>
    public MaterialIconStringExt(string kind) {
        Kind = kind;
    }

    /// <summary>
    /// Gets or sets the material icon name to parse.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Gets or sets the icon animation to play.
    /// </summary>
    public MaterialIconAnimation Animation { get; set; }

    /// <summary>
    /// Gets or sets the optional icon size.
    /// </summary>
    public double? IconSize { get; set; }

    /// <summary>
    /// Gets or sets the optional foreground brush.
    /// </summary>
    public Brush? IconForeground { get; set; }

    /// <summary>
    /// Gets or sets the optional vertical alignment.
    /// </summary>
    public VerticalAlignment? VerticalAlignment { get; set; }

    /// <summary>
    /// Gets or sets the optional horizontal alignment.
    /// </summary>
    public HorizontalAlignment? HorizontalAlignment { get; set; }

    /// <inheritdoc />
    protected override object ProvideValue(IXamlServiceProvider serviceProvider) {
        if (string.IsNullOrWhiteSpace(Kind)) {
            throw new ArgumentException("The material icon name must not be null, empty, or whitespace.", nameof(Kind));
        }

        if (!Enum.TryParse<MaterialIconKind>(Kind, ignoreCase: true, out var parsedKind)) {
            throw new ArgumentException($"Unknown material icon kind '{Kind}'.", nameof(Kind));
        }

        var result = new MaterialIcon
        {
            Kind = parsedKind,
            Animation = Animation
        };

        if (IconSize is not null) {
            result.Height = IconSize.Value;
            result.Width = IconSize.Value;
        }

        if (IconForeground is not null) {
            result.Foreground = IconForeground;
        }

        if (VerticalAlignment is not null) {
            result.VerticalAlignment = VerticalAlignment.Value;
        }

        if (HorizontalAlignment is not null) {
            result.HorizontalAlignment = HorizontalAlignment.Value;
        }

        return result;
    }
}
