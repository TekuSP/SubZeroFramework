using System.Collections.Immutable;

namespace SubZeroFramework.Models;

/// <summary>
/// The tints a cooling profile may paint the shell with.
/// </summary>
/// <remarks>
/// Drawn from the app's own chart palette so a tinted shell still looks like this app rather than like a
/// theme someone bolted on. NO AMBERS: the rail's update icon turns amber when a release is available, and a
/// tint in that family would camouflage the one thing on the rail that is trying to get attention.
/// </remarks>
public static class CoolingAccentPalette
{
    /// <summary>Chart accent blue.</summary>
    public const uint AccentBlue = 0xFF8AB7E8u;

    /// <summary>Chart primary periwinkle.</summary>
    public const uint Periwinkle = 0xFFD7D8FFu;

    /// <summary>Status success green.</summary>
    public const uint Green = 0xFF6CCB5Fu;

    /// <summary>Chart error clay.</summary>
    public const uint Clay = 0xFF8A5C5Bu;

    /// <summary>Chart muted slate.</summary>
    public const uint Slate = 0xFF5D5E73u;

    /// <summary>Severity critical red.</summary>
    public const uint Red = 0xFFD9706Au;

    /// <summary>Violet, for profiles that are neither hot nor cold.</summary>
    public const uint Violet = 0xFF7E6BB0u;

    /// <summary>Teal, the cool end of the shelf.</summary>
    public const uint Teal = 0xFF4E9C97u;

    /// <summary>The curated tints, in the order they are offered.</summary>
    public static readonly ImmutableArray<uint> Tints =
    [
        AccentBlue,
        Teal,
        Green,
        Periwinkle,
        Violet,
        Slate,
        Clay,
        Red,
    ];
}
