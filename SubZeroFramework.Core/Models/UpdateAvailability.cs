namespace SubZeroFramework.Models;

/// <summary>How an update check ended.</summary>
public enum UpdateCheckStatus
{
    /// <summary>
    /// The question could not be answered — offline, rate-limited, no releases published, or the running
    /// version could not be determined (a local dev build stamps none).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="UpToDate"/> on purpose. Collapsing the two told a user whose check had
    /// FAILED that they were on the newest release, which is a claim the app had no evidence for.
    /// </remarks>
    Unknown,

    /// <summary>The feed was read and nothing newer than the running version exists.</summary>
    UpToDate,

    /// <summary>A strictly newer release exists.</summary>
    UpdateAvailable,
}

/// <summary>
/// The outcome of one update check.
/// </summary>
/// <remarks>
/// <see cref="None"/> is the answer to every failure — offline, rate-limited, no releases published, a tag
/// that could not be parsed. The caller cannot tell those apart on purpose: none of them is something to
/// show a user who did not ask, and collapsing them here keeps that decision out of the UI.
/// </remarks>
public sealed record UpdateAvailability
{
    /// <summary>Nothing to offer, for any reason.</summary>
    public static UpdateAvailability None { get; } = new();

    /// <summary>The newest published release, or null when none could be resolved.</summary>
    public Version? LatestVersion { get; init; }

    /// <summary>
    /// The version currently running, so the UI can state both sides of the comparison.
    /// </summary>
    /// <remarks>
    /// Carried here rather than re-derived in the view: "0.1.6 is available" is a fact the user cannot act
    /// on without knowing what they are on, and having the UI parse the assembly a second time would put a
    /// second copy of the version rules where the first one could drift away from it.
    /// </remarks>
    public Version? CurrentVersion { get; init; }

    /// <summary>The release's own page on GitHub, or null. Already validated as a github.com URL.</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>How the check ended, so the UI can tell "nothing newer" from "could not ask".</summary>
    public UpdateCheckStatus Status { get; init; } = UpdateCheckStatus.Unknown;

    /// <summary>True only when a strictly newer version was resolved AND has somewhere to send the user.</summary>
    public bool IsUpdateAvailable => LatestVersion is not null && !string.IsNullOrWhiteSpace(ReleaseUrl);
}
