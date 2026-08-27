namespace SubZeroFramework.Services.Updates;

/// <summary>What the last update check found, so a launch can act without going to the network.</summary>
public sealed record UpdateCheckState
{
    /// <summary>When the feed was last successfully contacted.</summary>
    public DateTimeOffset? LastCheckedUtc { get; init; }

    /// <summary>The ETag to revalidate with, so an unchanged feed costs no rate-limit quota.</summary>
    public string? ETag { get; init; }

    /// <summary>The newest version seen, as a string so the file stays readable.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>That release's page.</summary>
    public string? LatestReleaseUrl { get; init; }
}

/// <summary>Persistence for <see cref="UpdateCheckState"/>.</summary>
/// <remarks>
/// The interface lives in Core so the coordinator can be tested against a fake; the implementation that
/// writes to the per-user application-data folder lives in the app, beside the other client-only stores.
/// </remarks>
public interface IUpdateCheckStateStore
{
    /// <summary>The state as last read or written.</summary>
    UpdateCheckState Current { get; }

    /// <summary>Replaces the state and writes it to disk.</summary>
    /// <param name="state">The new state.</param>
    void Save(UpdateCheckState state);
}
