using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Converters;

/// <summary>
/// Resolves a theme brush KEY to the brush itself, for collections whose items carry their own colour.
/// </summary>
/// <remarks>
/// <para>
/// Needed because a <c>DataTemplate</c> inside an <c>ItemsRepeater</c> cannot pick a <c>StaticResource</c>
/// per item — the resource is chosen when the template is parsed, not per row. The alternative is putting a
/// <see cref="Brush"/> on the item model, which drags a UI type into a view model and, worse, creates it on
/// whatever thread the model was built on: brushes made off the UI thread fail silently in Uno and take the
/// whole DataContext down with them.
/// </para>
/// <para>
/// Passing a key keeps the model free of UI types and keeps the swatch colours defined in exactly one place —
/// the theme dictionary — so a legend can never drift from the bar it describes.
/// </para>
/// </remarks>
public sealed partial class ThemeBrushKeyConverter : IValueConverter
{
    /// <summary>Resolves the key, falling back to transparent rather than throwing.</summary>
    /// <param name="value">The resource key.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="language">Unused.</param>
    /// <returns>The brush, or a transparent brush when the key is unknown.</returns>
    /// <remarks>
    /// A missing key yields an invisible swatch rather than an exception: a legend row losing its colour is a
    /// cosmetic defect, while throwing inside a template tears down the list.
    /// </remarks>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string key
            && Application.Current?.Resources.TryGetValue(key, out var resource) == true
            && resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    /// <summary>Not supported — a brush cannot be turned back into the key that produced it.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException("Theme brush keys are one-way.");
}
