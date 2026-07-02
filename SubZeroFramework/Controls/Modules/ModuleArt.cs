using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SubZeroFramework.Controls.Modules;

/// <summary>
/// Resolves module presentation art: the real chassis/module PNGs in <c>Assets/Png</c> (used INSTEAD of the
/// design PDFs' placeholder shapes — user rule) and the catalog's MaterialIconKind names. Images are cached.
/// </summary>
public static class ModuleArt
{
    private static readonly Dictionary<string, BitmapImage> ImageCache = [];

    /// <summary>The FW16 top-down chassis art.</summary>
    public const string Framework16TopPath = "ms-appx:///Assets/Png/framework-16-top.png";

    /// <summary>The FW16 top-row spacer module art (spacers are inferred, not enumerated).</summary>
    public static ImageSource SpacerImage => Load("ms-appx:///Assets/Png/framework-16-spacer-module.png");

    /// <summary>The FW16 touchpad spacer art (flanking the standard touchpad).</summary>
    public static ImageSource TouchpadSpacerImage => Load("ms-appx:///Assets/Png/framework-16-touchpad-spacer.png");

    /// <summary>Parses a catalog icon name into a glyph, falling back to a help icon so nothing renders blank.</summary>
    public static MaterialIconKind ResolveIcon(string iconName)
        => Enum.TryParse<MaterialIconKind>(iconName, out var kind) ? kind : MaterialIconKind.HelpCircleOutline;

    /// <summary>The input-deck module art for an identity, or null when no PNG exists (icon card instead).</summary>
    // FD0001 intentionally suppressed: art is picked from what the device itself reported.
#pragma warning disable FD0001
    public static ImageSource? DeckImageFor(FrameworkModuleIdentity identity) => identity switch
    {
        FrameworkModuleIdentity.Framework16KeyboardModule => Load("ms-appx:///Assets/Png/framework-16-keyboard.png"),
        FrameworkModuleIdentity.Framework16LedMatrix => Load("ms-appx:///Assets/Png/framework-16-ledmatrix-module.png"),
        FrameworkModuleIdentity.Framework16TouchpadModule => Load("ms-appx:///Assets/Png/framework-16-touchpad.png"),
        _ => null,
    };
#pragma warning restore FD0001

    private static BitmapImage Load(string path)
    {
        if (!ImageCache.TryGetValue(path, out var image))
        {
            image = new BitmapImage(new Uri(path));
            ImageCache[path] = image;
        }

        return image;
    }
}
