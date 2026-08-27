using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Updates;

/// <summary>Decides whether there is an update worth telling the user about.</summary>
public interface IUpdateNotificationCoordinator
{
    /// <summary>Returns what to offer, contacting the feed only when it is due.</summary>
    /// <param name="force">True for a user-initiated check, which ignores the interval and the opt-out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What to offer, or <see cref="UpdateAvailability.None"/>.</returns>
    Task<UpdateAvailability> EvaluateAsync(bool force, CancellationToken cancellationToken);
}

/// <summary>
/// Rate-limits the network check, caches its verdict, and applies it to the running version.
/// </summary>
/// <remarks>
/// The interval governs the FETCH, never the answer: the tip is meant to appear on every launch while an
/// update is outstanding, so a launch inside the window still gets the cached verdict — which also means the
/// tip works offline once the release has been seen once.
/// </remarks>
public sealed class UpdateNotificationCoordinator : IUpdateNotificationCoordinator
{
    /// <summary>How long a fetched verdict is reused before the feed is contacted again.</summary>
    /// <remarks>
    /// The unauthenticated API allows 60 requests an hour per IP, shared across everyone behind a NAT.
    /// Once a day per install leaves that budget alone even on a busy network.
    /// </remarks>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IUpdateCheckClient _client;
    private readonly IUpdateCheckStateStore _stateStore;
    private readonly Func<bool> _areAutomaticChecksEnabled;
    private readonly Version? _currentVersion;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<UpdateNotificationCoordinator> _logger;

    /// <summary>Creates the coordinator.</summary>
    /// <param name="client">The release feed.</param>
    /// <param name="stateStore">Where the cached verdict lives.</param>
    /// <param name="areAutomaticChecksEnabled">
    /// Reads the user's opt-out. A delegate rather than the settings store itself, so Core does not have to
    /// know about the app's settings surface — and so a user toggling it mid-session is seen immediately.
    /// </param>
    /// <param name="currentVersion">The running version, or null when it could not be parsed.</param>
    /// <param name="clock">The clock, injected so the interval is testable without waiting a day.</param>
    /// <param name="logger">Where failures are recorded.</param>
    public UpdateNotificationCoordinator(
        IUpdateCheckClient client,
        IUpdateCheckStateStore stateStore,
        Func<bool> areAutomaticChecksEnabled,
        Version? currentVersion,
        Func<DateTimeOffset> clock,
        ILogger<UpdateNotificationCoordinator> logger)
    {
        _client = client;
        _stateStore = stateStore;
        _areAutomaticChecksEnabled = areAutomaticChecksEnabled;
        _currentVersion = currentVersion;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateAvailability> EvaluateAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && !_areAutomaticChecksEnabled())
        {
            return UpdateAvailability.None;
        }

        // No usable local version — a dev build, or something unparseable. Comparing against it would be
        // guessing, and the only thing the answer drives is telling the user their install is stale.
        if (_currentVersion is null)
        {
            return UpdateAvailability.None;
        }

        var state = _stateStore.Current;

        if (force || IsDue(state))
        {
            state = await FetchAsync(state, cancellationToken).ConfigureAwait(false);
        }

        // Nothing cached and nothing fetched: the feed was never successfully read, so "up to date" would be
        // a claim with no evidence behind it. Unknown is the honest answer.
        if (state.LastCheckedUtc is null || string.IsNullOrWhiteSpace(state.LatestVersion))
        {
            return new UpdateAvailability { CurrentVersion = _currentVersion, Status = UpdateCheckStatus.Unknown };
        }

        if (AppVersion.Parse(state.LatestVersion) is not { } latest
            || string.IsNullOrWhiteSpace(state.LatestReleaseUrl)
            || !AppVersion.IsNewer(latest, _currentVersion))
        {
            // Nothing to offer, but the running version is still worth carrying: a user who PRESSED a check
            // button is owed "SubZero 0.1.5 is the newest release", not a bare "nothing found".
            return new UpdateAvailability { CurrentVersion = _currentVersion, Status = UpdateCheckStatus.UpToDate };
        }

        // CurrentVersion is filled in HERE, not by the client: the client knows what GitHub published, the
        // coordinator is the only thing that also knows what is running.
        return new UpdateAvailability
        {
            LatestVersion = latest,
            CurrentVersion = _currentVersion,
            ReleaseUrl = state.LatestReleaseUrl,
            Status = UpdateCheckStatus.UpdateAvailable,
        };
    }

    private bool IsDue(UpdateCheckState state)
        => state.LastCheckedUtc is not { } lastChecked || _clock() - lastChecked >= CheckInterval;

    private async Task<UpdateCheckState> FetchAsync(UpdateCheckState state, CancellationToken cancellationToken)
    {
        var result = await _client.FetchLatestAsync(state.ETag, cancellationToken).ConfigureAwait(false);

        // 304: the feed is unchanged, so the cached version and URL stand. Only the timestamp moves.
        if (result.NotModified)
        {
            var revalidated = state with { LastCheckedUtc = _clock(), ETag = result.ETag ?? state.ETag };
            _stateStore.Save(revalidated);
            return revalidated;
        }

        // A failed fetch must not erase a good cached verdict — otherwise going offline hides an update the
        // user has already been told about.
        if (!result.Availability.IsUpdateAvailable)
        {
            _logger.LogDebug("The update check found nothing new; the cached verdict stands.");
            return state;
        }

        var updated = state with
        {
            LastCheckedUtc = _clock(),
            ETag = result.ETag,
            LatestVersion = result.Availability.LatestVersion?.ToString(),
            LatestReleaseUrl = result.Availability.ReleaseUrl,
        };

        _stateStore.Save(updated);
        return updated;
    }
}
