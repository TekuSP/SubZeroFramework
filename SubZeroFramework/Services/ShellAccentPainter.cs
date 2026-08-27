using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using SubZeroFramework.Services.Cooling;

namespace SubZeroFramework.Services;

/// <summary>
/// Tints the shell — the title bar and the navigation pane — to the active cooling profile's colour.
/// </summary>
public interface IShellAccentPainter
{
    /// <summary>
    /// Fades the shell to the tint for <paramref name="accentArgb"/>, or back to bare surface when null.
    /// </summary>
    /// <param name="accentArgb">The profile's chosen tint, or null for none.</param>
    void Apply(uint? accentArgb);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// Works by ANIMATING one shared brush rather than by swapping brushes. The title bar and the pane both
/// paint from <c>ShellSurfaceBrush</c>, so moving its Color moves both together and no seam can appear
/// between them; and a colour can be animated between two values, where a replaced brush can only snap.
/// </para>
/// <para>
/// An explicit Storyboard rather than an implicit transition: <c>BrushTransition</c> is a WinUI implicit
/// animation Uno does not implement, so the fade it was supposed to provide simply would not happen on the
/// Skia head — the same trap ScrollHint's chevrons fell into. EnableDependentAnimation is required because
/// a brush's Color is not composition-animated.
/// </para>
/// </remarks>
public sealed class ShellAccentPainter : IShellAccentPainter
{
    /// <summary>
    /// How long the shell takes to change colour.
    /// </summary>
    /// <remarks>
    /// Long enough to read as a deliberate change of state rather than a glitch, short enough that switching
    /// profiles still feels immediate. The fan cards update instantly; this is the slower, ambient signal.
    /// </remarks>
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(420);

    private const string ShellSurfaceBrushKey = "ShellSurfaceBrush";

    private Storyboard? _running;

    public void Apply(uint? accentArgb)
    {
        if (Application.Current?.Resources[ShellSurfaceBrushKey] is not SolidColorBrush surface)
        {
            return;
        }

        var target = ToColor(accentArgb is { } accent
            ? AccentBlend.Blend(accent, AccentBlend.SidebarArgb)
            : AccentBlend.SidebarArgb);

        if (surface.Color == target)
        {
            return;
        }

        // Switching profiles quickly must not leave two fades fighting over the same brush; the last one
        // asked for is the one that should land.
        _running?.Stop();

        // No EasingFunction: ColorAnimation.EasingFunction is unimplemented in Uno (Uno0001), so it would
        // ease on the WinUI head and run linear on Skia — the same split-behaviour trap OpacityTransition set
        // for ScrollHint. A linear fade of this length reads fine, and reads the SAME on both.
        var animation = new ColorAnimation
        {
            To = target,
            Duration = new Duration(FadeDuration),
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, surface);
        Storyboard.SetTargetProperty(animation, "Color");

        _running = new Storyboard();
        _running.Children.Add(animation);
        _running.Begin();
    }

    private static Windows.UI.Color ToColor(uint argb) => Windows.UI.Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));
}
