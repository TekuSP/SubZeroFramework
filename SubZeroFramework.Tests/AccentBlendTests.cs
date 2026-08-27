using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Cooling;

namespace SubZeroFramework.Tests;

/// <summary>
/// The shell tint has one job it must never fail at: staying out of the way of the rail's own icons.
/// </summary>
/// <remarks>
/// The user picks a hue and the app picks the strength, so these tests are what stop a pale choice from
/// quietly making the navigation unreadable — the failure mode nobody reports as a bug because it looks
/// deliberate.
/// </remarks>
[TestFixture]
public class AccentBlendTests
{
    [Test]
    public void Blend_IsOpaque_SoItCanBePaintedDirectly()
        => Assert.That(AccentBlend.Blend(CoolingAccentPalette.AccentBlue, AccentBlend.SidebarArgb) >> 24, Is.EqualTo(0xFFu));

    [Test]
    public void Blend_StaysNearTheSurface_BecauseTheAlphaIsFixedLow()
    {
        var blended = AccentBlend.Blend(CoolingAccentPalette.AccentBlue, AccentBlend.SidebarArgb);

        // 18% of the way from black toward a mid blue is still unmistakably a dark rail.
        Assert.That(AccentBlend.ContrastRatio(AccentBlend.RailIconArgb, blended), Is.GreaterThanOrEqualTo(AccentBlend.MinimumContrastRatio));
    }

    [Test]
    public void Blend_KeepsEvenAWhiteTintReadable_RatherThanErasingTheIcons()
    {
        var blended = AccentBlend.Blend(0xFFFFFFFFu, AccentBlend.SidebarArgb);

        Assert.That(AccentBlend.ContrastRatio(AccentBlend.RailIconArgb, blended), Is.GreaterThanOrEqualTo(AccentBlend.MinimumContrastRatio));
    }

    [Test]
    public void EveryCuratedTint_StaysReadable()
    {
        Assert.Multiple(() =>
        {
            foreach (var tint in CoolingAccentPalette.Tints)
            {
                var blended = AccentBlend.Blend(tint, AccentBlend.SidebarArgb);

                Assert.That(
                    AccentBlend.ContrastRatio(AccentBlend.RailIconArgb, blended),
                    Is.GreaterThanOrEqualTo(AccentBlend.MinimumContrastRatio),
                    $"Tint {tint:X8} leaves the rail icons unreadable.");
            }
        });
    }

    /// <summary>
    /// No tint at all leaves the surface exactly as it was.
    /// </summary>
    /// <remarks>
    /// Black has to keep meaning "no profile selected". A blend that drifted even slightly would make the
    /// unselected state look like a very dark choice of tint instead of like an absence.
    /// </remarks>
    [Test]
    public void BlendingTheSurfaceWithItself_ChangesNothing()
        => Assert.That(
            AccentBlend.Blend(AccentBlend.SidebarArgb, AccentBlend.SidebarArgb),
            Is.EqualTo(AccentBlend.SidebarArgb));

    /// <summary>The palette is a shelf, not a paint mixer.</summary>
    [Test]
    public void TheCuratedPaletteHasNoAmbers_SoATintCannotCamouflageTheUpdateIcon()
    {
        Assert.Multiple(() =>
        {
            foreach (var tint in CoolingAccentPalette.Tints)
            {
                var red = (tint >> 16) & 0xFF;
                var green = (tint >> 8) & 0xFF;
                var blue = tint & 0xFF;

                // Amber is a warm colour whose blue channel falls well below both others. The rail's update
                // notice owns that corner of the wheel.
                var isAmber = red > 150 && green > 110 && blue < 100;

                Assert.That(isAmber, Is.False, $"Tint {tint:X8} sits in the amber family the update icon uses.");
            }
        });
    }
}
