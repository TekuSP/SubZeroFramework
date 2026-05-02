using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Extensions;

/// <summary>
/// Creates a <see cref="FontIcon"/> from a glyph string.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(FontIcon))]
public sealed class FontIconStringExt : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FontIconStringExt"/> class.
    /// </summary>
    public FontIconStringExt() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FontIconStringExt"/> class.
    /// </summary>
    /// <param name="glyph">The glyph string to render.</param>
    public FontIconStringExt(string glyph)
    {
        Glyph = glyph;
    }

    /// <summary>
    /// Gets or sets the glyph string to render.
    /// </summary>
    public string? Glyph { get; set; }

    /// <summary>
    /// Gets or sets the optional horizontal alignment.
    /// </summary>
    public HorizontalAlignment? HorizontalAlignment { get; set; }

    /// <summary>
    /// Gets or sets the optional font family.
    /// </summary>
    public FontFamily? IconFontFamily { get; set; }

    /// <summary>
    /// Gets or sets the optional foreground brush.
    /// </summary>
    public Brush? IconForeground { get; set; }

    /// <summary>
    /// Gets or sets the optional font size.
    /// </summary>
    public double? IconSize { get; set; }

    /// <summary>
    /// Gets or sets the optional vertical alignment.
    /// </summary>
    public VerticalAlignment? VerticalAlignment { get; set; }

    /// <inheritdoc />
    protected override object ProvideValue(IXamlServiceProvider serviceProvider)
    {
        var result = new FontIcon
        {
            Glyph = string.IsNullOrWhiteSpace(Glyph) ? throw new ArgumentException("The glyph string must not be null, empty, or whitespace.", nameof(Glyph)) : Glyph
        };

        result.FontSize = IconSize ?? result.FontSize;
        result.Foreground = IconForeground ?? result.Foreground;
        result.FontFamily = IconFontFamily ?? result.FontFamily;
        result.VerticalAlignment = VerticalAlignment ?? result.VerticalAlignment;
        result.HorizontalAlignment = HorizontalAlignment ?? result.HorizontalAlignment;

        return result;
    }
}
