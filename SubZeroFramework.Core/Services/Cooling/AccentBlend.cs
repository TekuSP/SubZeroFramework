namespace SubZeroFramework.Services.Cooling;

/// <summary>
/// Composites an accent tint over a surface colour and keeps the result readable.
/// </summary>
/// <remarks>
/// <para>
/// BLENDED rather than layered. Producing one opaque colour keeps the visual tree free of an overlay
/// element, avoids hit-testing and NavigationView pane-layering questions entirely, and makes the result
/// something a test can assert on directly rather than something only an eye can check.
/// </para>
/// <para>
/// Deliberately free of <c>Windows.UI.Color</c> so it lives in Core and needs no UI thread: a Brush built off
/// the UI thread fails silently in this app, and colour arithmetic has no business being anywhere near that
/// hazard.
/// </para>
/// </remarks>
public static class AccentBlend
{
    /// <summary>The shell surface every tint is laid over: App.xaml's SidebarBackgroundBrush.</summary>
    public const uint SidebarArgb = 0xFF000000u;

    /// <summary>The rail's icon colour, which every blend has to stay readable against.</summary>
    public const uint RailIconArgb = 0xFFD7D8FFu;

    /// <summary>
    /// How much of the tint reaches the surface.
    /// </summary>
    /// <remarks>
    /// FIXED BY THE APP, never by the user. A raw colour choice would let someone pick a tint that erases the
    /// rail's own contrast; taking only the hue and keeping the strength here means every choice stays
    /// legible without having to police the choices themselves.
    /// </remarks>
    public const double AccentAlpha = 0.18d;

    /// <summary>The readability floor a blended surface must clear against the rail's icon colour.</summary>
    public const double MinimumContrastRatio = 4.5d;

    /// <summary>
    /// The opaque colour produced by laying <paramref name="accentArgb"/> over <paramref name="surfaceArgb"/>.
    /// </summary>
    /// <param name="accentArgb">The tint. Only its hue matters; its alpha is ignored.</param>
    /// <param name="surfaceArgb">The surface being tinted.</param>
    /// <returns>An opaque ARGB colour.</returns>
    /// <remarks>
    /// Steps the alpha back toward the surface until the rail's icons stay readable, so a user who picks
    /// something pale gets a barely-tinted rail rather than an unusable one.
    /// </remarks>
    public static uint Blend(uint accentArgb, uint surfaceArgb)
    {
        var alpha = AccentAlpha;

        while (true)
        {
            var candidate = Mix(accentArgb, surfaceArgb, alpha);

            if (alpha <= 0d || ContrastRatio(RailIconArgb, candidate) >= MinimumContrastRatio)
            {
                return candidate;
            }

            alpha -= 0.02d;
        }
    }

    /// <summary>The WCAG contrast ratio between two opaque colours, from 1.0 to 21.0.</summary>
    /// <param name="foregroundArgb">The colour being read.</param>
    /// <param name="backgroundArgb">The colour it is read against.</param>
    public static double ContrastRatio(uint foregroundArgb, uint backgroundArgb)
    {
        var first = RelativeLuminance(foregroundArgb);
        var second = RelativeLuminance(backgroundArgb);

        return (Math.Max(first, second) + 0.05d) / (Math.Min(first, second) + 0.05d);
    }

    private static uint Mix(uint accentArgb, uint surfaceArgb, double alpha) =>
        0xFF000000u
        | ((uint)Math.Round((((accentArgb >> 16) & 0xFF) * alpha) + (((surfaceArgb >> 16) & 0xFF) * (1d - alpha))) << 16)
        | ((uint)Math.Round((((accentArgb >> 8) & 0xFF) * alpha) + (((surfaceArgb >> 8) & 0xFF) * (1d - alpha))) << 8)
        | (uint)Math.Round(((accentArgb & 0xFF) * alpha) + ((surfaceArgb & 0xFF) * (1d - alpha)));

    private static double RelativeLuminance(uint argb)
    {
        static double Channel(double raw)
        {
            var value = raw / 255d;
            return value <= 0.03928d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Channel((argb >> 16) & 0xFF))
            + (0.7152d * Channel((argb >> 8) & 0xFF))
            + (0.0722d * Channel(argb & 0xFF));
    }
}
