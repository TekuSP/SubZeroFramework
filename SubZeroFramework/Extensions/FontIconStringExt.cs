using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Extensions;

/// <summary>
/// Creates a <see cref="FontIcon"/> from a glyph string.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(FontIcon))]
public sealed class FontIconStringExt : MarkupExtension {
    /// <summary>
    /// Initializes a new instance of the <see cref="FontIconStringExt"/> class.
    /// </summary>
    public FontIconStringExt() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FontIconStringExt"/> class.
    /// </summary>
    /// <param name="glyph">The glyph string to render.</param>
    public FontIconStringExt(string glyph) {
        Glyph = glyph;
    }

    /// <summary>
    /// Gets or sets the glyph string to render.
    /// </summary>
    public string? Glyph { get; set; }

    /// <summary>
    /// Gets or sets the optional font size.
    /// </summary>
    public double? IconSize { get; set; }

    /// <summary>
    /// Gets or sets the optional foreground brush.
    /// </summary>
    public Brush? IconForeground { get; set; }

    /// <summary>
    /// Gets or sets the optional font family.
    /// </summary>
    public FontFamily? IconFontFamily { get; set; }

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
        if (string.IsNullOrWhiteSpace(Glyph)) {
            throw new ArgumentException("The glyph string must not be null, empty, or whitespace.", nameof(Glyph));
        }

        var result = new FontIcon
        {
            Glyph = Glyph
        };

        if (IconSize is not null) {
            result.FontSize = IconSize.Value;
        }

        if (IconForeground is not null) {
            result.Foreground = IconForeground;
        }

        if (IconFontFamily is not null) {
            result.FontFamily = IconFontFamily;
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
