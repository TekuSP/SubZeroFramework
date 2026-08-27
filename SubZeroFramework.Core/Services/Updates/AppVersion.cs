namespace SubZeroFramework.Services.Updates;

/// <summary>
/// Parses and compares the two version strings the update check has to reconcile: the app's own
/// <c>AssemblyInformationalVersion</c> and a GitHub release tag.
/// </summary>
/// <remarks>
/// In Core rather than beside the UI that shows the result, because this is where the tests can reach it:
/// the cross-platform test project references Core and the service, never the Uno app head.
/// </remarks>
public static class AppVersion
{
    /// <summary>The version in <paramref name="raw"/>, or null when it is not one this can compare safely.</summary>
    /// <param name="raw">A release tag ("v0.1.6") or an informational version ("0.1.5+abc1234").</param>
    /// <returns>The parsed version, or null.</returns>
    /// <remarks>
    /// Refusing is the safe answer, not a fallback: a string this cannot parse — a prerelease tag, a moving
    /// "nightly" tag, a hand-cut label — must produce silence rather than a guess, because the only thing the
    /// result drives is whether to tell the user their install is out of date.
    /// </remarks>
    public static Version? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        // SourceLink appends "+<commit-hash>" to the informational version.
        var plusIndex = text.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex > 0)
        {
            text = text[..plusIndex];
        }

        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        // Version.TryParse is culture-invariant by definition (digits and dots only), so there is no
        // IFormatProvider overload to pass one to.
        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>True when <paramref name="candidate"/> is strictly ahead of <paramref name="current"/>.</summary>
    /// <param name="candidate">The version offered by the release feed.</param>
    /// <param name="current">The running version.</param>
    /// <returns>True when an update is genuinely available.</returns>
    /// <remarks>
    /// Both sides are normalised to four fields first. A tag is cut as "v0.1.6" while a local build stamps
    /// "0.1.6.0"; comparing them raw makes the same release look newer than itself, and the tip would then
    /// appear on every launch of an install that is already current.
    /// </remarks>
    public static bool IsNewer(Version candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);

        return Normalize(candidate) > Normalize(current);
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        version.Build < 0 ? 0 : version.Build,
        version.Revision < 0 ? 0 : version.Revision);
}
