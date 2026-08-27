using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Updates;

/// <summary>One fetch of the release feed: what it found, its ETag, and whether it was unchanged.</summary>
/// <param name="Availability">What to offer the user, or <see cref="UpdateAvailability.None"/>.</param>
/// <param name="ETag">The response ETag, to send back next time. Null when there was none.</param>
/// <param name="NotModified">True when the server answered 304 and the caller should reuse its cache.</param>
public sealed record UpdateCheckResult(UpdateAvailability Availability, string? ETag, bool NotModified);

/// <summary>Asks somewhere whether a newer release exists.</summary>
public interface IUpdateCheckClient
{
    /// <summary>Fetches the latest release, sending <paramref name="etag"/> if one is known.</summary>
    /// <param name="etag">The ETag from the previous fetch, or null.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result; never throws.</returns>
    Task<UpdateCheckResult> FetchLatestAsync(string? etag, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the latest published release from the GitHub API.
/// </summary>
/// <remarks>
/// <para>
/// <c>/releases/latest</c> rather than <c>/releases</c>: it already excludes drafts and prereleases, which
/// matches what CI can produce — <c>build.yml</c> refuses a tag containing '-'.
/// </para>
/// <para>
/// NOTHING here throws. Every failure — no network, a 403 from the unauthenticated 60/hour limit shared
/// across a NAT, a repo with no releases, a body that changed shape — returns
/// <see cref="UpdateAvailability.None"/>. The check is unsolicited, so a failure the user did not ask for
/// must be invisible.
/// </para>
/// </remarks>
public sealed class GitHubUpdateCheckClient : IUpdateCheckClient
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/TekuSP/SubZeroFramework/releases/latest";

    /// <summary>The only host and path prefix a release URL may have.</summary>
    private const string ReleaseUrlPrefix = "https://github.com/TekuSP/SubZeroFramework/";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _client;
    private readonly ILogger<GitHubUpdateCheckClient> _logger;

    /// <summary>Creates the client.</summary>
    /// <param name="client">The HTTP client to send with.</param>
    /// <param name="logger">Where failures are recorded, since none of them reach the user.</param>
    public GitHubUpdateCheckClient(HttpClient client, ILogger<GitHubUpdateCheckClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> FetchLatestAsync(string? etag, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);

            // Mandatory: GitHub answers 403 to a request without one.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SubZeroFramework", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);

            // Unchanged since last time, and it cost no rate-limit quota.
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new UpdateCheckResult(UpdateAvailability.None, etag, NotModified: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("The update check returned {StatusCode}; leaving the cached result alone.", response.StatusCode);
                return new UpdateCheckResult(UpdateAvailability.None, etag, NotModified: false);
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(body, GitHubReleaseJsonContext.Default.GitHubRelease);
            var responseETag = response.Headers.ETag?.ToString() ?? etag;

            if (AppVersion.Parse(release?.TagName) is not { } version || !IsTrustedReleaseUrl(release?.HtmlUrl))
            {
                _logger.LogDebug("The update check found no usable release (tag '{Tag}').", release?.TagName);
                return new UpdateCheckResult(UpdateAvailability.None, responseETag, NotModified: false);
            }

            return new UpdateCheckResult(
                new UpdateAvailability { LatestVersion = version, ReleaseUrl = release!.HtmlUrl },
                responseETag,
                NotModified: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogDebug(exception, "The update check could not complete.");
            return new UpdateCheckResult(UpdateAvailability.None, etag, NotModified: false);
        }
    }

    /// <summary>Whether a URL from the response may be handed to the shell.</summary>
    /// <remarks>
    /// The URL arrives over the network, and the app will LAUNCH it. Anything that is not this repo's own
    /// HTTPS release area is refused rather than opened.
    /// </remarks>
    private static bool IsTrustedReleaseUrl(string? url)
        => !string.IsNullOrWhiteSpace(url)
            && url.StartsWith(ReleaseUrlPrefix, StringComparison.Ordinal);
}

/// <summary>The two fields this needs from the release payload.</summary>
internal sealed record GitHubRelease
{
    /// <summary>The release tag, e.g. "v0.1.6".</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }

    /// <summary>The release's page on github.com.</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubReleaseJsonContext : JsonSerializerContext;
