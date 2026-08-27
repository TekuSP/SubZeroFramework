using System.Net;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Updates;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the release-feed client, whose whole contract is "answer something useful or answer nothing,
/// but never throw and never surface an error".
/// </summary>
[TestFixture]
public class GitHubUpdateCheckClientTests
{
    private const string LatestReleaseJson = """
        {
          "tag_name": "v0.1.6",
          "html_url": "https://github.com/TekuSP/SubZeroFramework/releases/tag/v0.1.6"
        }
        """;

    [Test]
    public async Task FetchLatestAsync_ReturnsTheTagAndUrl_OnSuccess()
    {
        var client = Client(new StubHandler(HttpStatusCode.OK, LatestReleaseJson, etag: "\"abc\""));

        var result = await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Availability.LatestVersion, Is.EqualTo(Version.Parse("0.1.6")));
            Assert.That(result.Availability.ReleaseUrl, Is.EqualTo("https://github.com/TekuSP/SubZeroFramework/releases/tag/v0.1.6"));
            Assert.That(result.ETag, Is.EqualTo("\"abc\""));
            Assert.That(result.NotModified, Is.False);
        });
    }

    [Test]
    public async Task FetchLatestAsync_SendsAUserAgent_BecauseGitHubRefusesWithoutOne()
    {
        var handler = new StubHandler(HttpStatusCode.OK, LatestReleaseJson);
        var client = Client(handler);

        await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.That(handler.LastRequest!.Headers.UserAgent, Is.Not.Empty);
    }

    [Test]
    public async Task FetchLatestAsync_SendsIfNoneMatch_WhenAnETagIsKnown()
    {
        var handler = new StubHandler(HttpStatusCode.NotModified, body: null);
        var client = Client(handler);

        var result = await client.FetchLatestAsync("\"abc\"", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastRequest!.Headers.IfNoneMatch.ToString(), Is.EqualTo("\"abc\""));
            Assert.That(result.NotModified, Is.True, "304 must be reported so the cached verdict is reused");
        });
    }

    // Every one of these is a normal condition, not an error: no network, a shared IP that burned the
    // 60/hour limit, a repo with no releases yet, a body that is not what we expected.
    [TestCase(HttpStatusCode.Forbidden, null)]
    [TestCase(HttpStatusCode.NotFound, null)]
    [TestCase(HttpStatusCode.OK, "{ \"tag_name\": \"nightly\" }")]
    [TestCase(HttpStatusCode.OK, "not json at all")]
    public async Task FetchLatestAsync_ReturnsNone_AndNeverThrows(HttpStatusCode status, string? body)
    {
        var client = Client(new StubHandler(status, body));

        var result = await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.That(result.Availability.IsUpdateAvailable, Is.False);
    }

    [Test]
    public async Task FetchLatestAsync_ReturnsNone_WhenTheTransportFails()
    {
        var client = Client(new ThrowingHandler());

        var result = await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.That(result.Availability.IsUpdateAvailable, Is.False);
    }

    // A release URL is a string from the network, and the app will LAUNCH it. Anything not on this repo
    // over HTTPS is refused rather than handed to the shell.
    [TestCase("https://evil.example.com/releases/tag/v0.1.6")]
    [TestCase("http://github.com/TekuSP/SubZeroFramework/releases/tag/v0.1.6")]
    [TestCase("https://github.com/someone-else/SubZeroFramework/releases/tag/v0.1.6")]
    public async Task FetchLatestAsync_RefusesAReleaseUrl_ThatIsNotOnThisRepoOverHttps(string url)
    {
        var json = $$"""{ "tag_name": "v0.1.6", "html_url": "{{url}}" }""";
        var client = Client(new StubHandler(HttpStatusCode.OK, json));

        var result = await client.FetchLatestAsync(etag: null, CancellationToken.None);

        Assert.That(result.Availability.IsUpdateAvailable, Is.False);
    }

    private HttpClient? _http;

    /// <summary>The client under test owns no disposable state; the HttpClient behind it does.</summary>
    [TearDown]
    public void DisposeHttpClient()
    {
        _http?.Dispose();
        _http = null;
    }

    private GitHubUpdateCheckClient Client(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler);
        return new GitHubUpdateCheckClient(_http, NullLogger<GitHubUpdateCheckClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string? body, string? etag = null) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(status);

            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            if (etag is not null)
            {
                response.Headers.TryAddWithoutValidation("ETag", etag);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("no network");
    }
}
