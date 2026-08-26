using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Presentation;

/// <summary>
/// Paints a <see cref="ContentDialog"/>'s primary button in the app's blue instead of the Windows accent.
/// </summary>
/// <remarks>
/// <para>
/// A dialog's primary button is styled with <c>AccentButtonStyle</c>, which reads its brushes from the
/// SYSTEM accent — so on a machine whose Windows accent is red, every confirm button in the app was red,
/// with a foreground chosen to contrast with that rather than with ours.
/// </para>
/// <para>
/// <b>Applied per dialog rather than once at application level.</b> Those brushes are read through
/// <c>ThemeResource</c>, and overriding them in <c>Application.Resources</c> — including in a
/// <c>ThemeDictionaries</c> entry — does not reach the control here: the same keys placed on the dialog
/// itself take effect while the application-level ones do not. Putting them in the element's own lookup
/// chain is the form that actually works, and it is what the coloured buttons elsewhere in this app already
/// do with <c>Button.Resources</c>.
/// </para>
/// </remarks>
internal static class AccentDialogPalette
{
    private static readonly Windows.UI.Color Accent = ColorHelper.FromArgb(0xFF, 0x00, 0x78, 0xD7);
    private static readonly Windows.UI.Color AccentHover = ColorHelper.FromArgb(0xFF, 0x1F, 0x8A, 0xE6);
    private static readonly Windows.UI.Color AccentPressed = ColorHelper.FromArgb(0xFF, 0x00, 0x63, 0xB1);
    private static readonly Windows.UI.Color AccentDisabled = ColorHelper.FromArgb(0xFF, 0x2A, 0x2D, 0x2D);
    private static readonly Windows.UI.Color OnAccent = Colors.White;
    private static readonly Windows.UI.Color OnAccentDisabled = ColorHelper.FromArgb(0x59, 0xFF, 0xFF, 0xFF);

    /// <summary>Applies the palette to one dialog. Safe to call before or after the dialog is shown.</summary>
    public static void Apply(ContentDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        Set(dialog, "AccentButtonBackground", Accent);
        Set(dialog, "AccentButtonBackgroundPointerOver", AccentHover);
        Set(dialog, "AccentButtonBackgroundPressed", AccentPressed);
        Set(dialog, "AccentButtonBackgroundDisabled", AccentDisabled);
        Set(dialog, "AccentButtonBorderBrush", Accent);
        Set(dialog, "AccentButtonBorderBrushPointerOver", AccentHover);
        Set(dialog, "AccentButtonBorderBrushPressed", AccentPressed);
        Set(dialog, "AccentButtonForeground", OnAccent);
        Set(dialog, "AccentButtonForegroundPointerOver", OnAccent);
        Set(dialog, "AccentButtonForegroundPressed", OnAccent);
        Set(dialog, "AccentButtonForegroundDisabled", OnAccentDisabled);
    }

    private static void Set(ContentDialog dialog, string key, Windows.UI.Color color)
        => dialog.Resources[key] = new SolidColorBrush(color);
}
