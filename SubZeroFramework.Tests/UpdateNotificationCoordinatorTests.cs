using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Updates;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the rule the update notification actually runs on: rate-limit the FETCH, never the answer.
/// </summary>
[TestFixture]
public class UpdateNotificationCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private const string ReleaseUrl = "https://github.com/TekuSP/SubZeroFramework/releases/tag/v0.1.6";

    [Test]
    public async Task EvaluateAsync_OffersTheUpdate_WhenTheFeedIsAhead()
    {
        var client = new StubClient(Availability("0.1.6"));
        var coordinator = Coordinator(client, current: "0.1.5");

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(availability.IsUpdateAvailable, Is.True);
    }

    // The tip states both sides ("0.1.6 is available — you're on 0.1.5"), so the result has to carry both.
    [Test]
    public async Task EvaluateAsync_CarriesBothVersions_SoTheTipCanStateTheComparison()
    {
        var coordinator = Coordinator(new StubClient(Availability("0.1.6")), current: "0.1.5");

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(availability.LatestVersion, Is.EqualTo(Version.Parse("0.1.6")));
            Assert.That(availability.CurrentVersion, Is.EqualTo(Version.Parse("0.1.5")));
        });
    }

    [Test]
    public async Task EvaluateAsync_OffersNothing_WhenTheFeedMatchesTheRunningVersion()
    {
        var coordinator = Coordinator(new StubClient(Availability("0.1.5")), current: "0.1.5");

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(availability.IsUpdateAvailable, Is.False);
    }

    // Someone who pressed a check button is owed "0.1.5 is the newest release", not a bare "nothing found".
    [Test]
    public async Task EvaluateAsync_StillReportsTheRunningVersion_WhenThereIsNoUpdate()
    {
        var coordinator = Coordinator(new StubClient(Availability("0.1.5")), current: "0.1.5");

        var availability = await coordinator.EvaluateAsync(force: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(availability.IsUpdateAvailable, Is.False);
            Assert.That(availability.CurrentVersion, Is.EqualTo(Version.Parse("0.1.5")));
            Assert.That(availability.Status, Is.EqualTo(UpdateCheckStatus.UpToDate));
        });
    }

    // "Up to date" is a CLAIM. A check that never reached the feed has no evidence for it, and saying it
    // anyway tells a user on an old build that they are current.
    [Test]
    public async Task EvaluateAsync_ReportsUnknown_NotUpToDate_WhenTheFeedWasNeverRead()
    {
        var coordinator = Coordinator(new StubClient(UpdateAvailability.None), current: "0.1.5");

        var availability = await coordinator.EvaluateAsync(force: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(availability.IsUpdateAvailable, Is.False);
            Assert.That(availability.Status, Is.EqualTo(UpdateCheckStatus.Unknown), "a failed fetch must not read as 'up to date'");
        });
    }

    // A local Debug build stamps no version attributes at all, so there is nothing to compare against.
    [Test]
    public async Task EvaluateAsync_ReportsUnknown_WhenTheRunningVersionCannotBeDetermined()
    {
        var coordinator = Coordinator(new StubClient(Availability("0.1.6")), current: null);

        var availability = await coordinator.EvaluateAsync(force: true, CancellationToken.None);

        Assert.That(availability.Status, Is.EqualTo(UpdateCheckStatus.Unknown));
    }

    [Test]
    public async Task EvaluateAsync_ReportsUpdateAvailable_WhenTheFeedIsAhead()
    {
        var coordinator = Coordinator(new StubClient(Availability("0.1.6")), current: "0.1.5");

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(availability.Status, Is.EqualTo(UpdateCheckStatus.UpdateAvailable));
    }

    // The check is rate-limited; the TIP is not. A launch inside the window must still be able to offer the
    // update, from cache, without touching the network.
    [Test]
    public async Task EvaluateAsync_UsesTheCache_WithoutCallingTheFeed_InsideTheInterval()
    {
        var stateStore = new StubStateStore(new UpdateCheckState
        {
            LastCheckedUtc = Now.AddHours(-1),
            LatestVersion = "0.1.6",
            LatestReleaseUrl = ReleaseUrl,
        });
        var client = new StubClient(UpdateAvailability.None);
        var coordinator = Coordinator(client, current: "0.1.5", stateStore: stateStore);

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.Calls, Is.Zero, "the feed was contacted inside the 24 h interval");
            Assert.That(availability.IsUpdateAvailable, Is.True, "the cached result must still raise the tip");
        });
    }

    [Test]
    public async Task EvaluateAsync_ContactsTheFeed_OnceTheIntervalHasElapsed()
    {
        var stateStore = new StubStateStore(new UpdateCheckState { LastCheckedUtc = Now.AddHours(-25) });
        var client = new StubClient(Availability("0.1.6"));
        var coordinator = Coordinator(client, current: "0.1.5", stateStore: stateStore);

        await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(client.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task EvaluateAsync_ContactsTheFeed_WhenForced_EvenInsideTheInterval()
    {
        var stateStore = new StubStateStore(new UpdateCheckState { LastCheckedUtc = Now });
        var client = new StubClient(Availability("0.1.6"));
        var coordinator = Coordinator(client, current: "0.1.5", stateStore: stateStore);

        await coordinator.EvaluateAsync(force: true, CancellationToken.None);

        Assert.That(client.Calls, Is.EqualTo(1), "a user-initiated check must bypass the interval");
    }

    [Test]
    public async Task EvaluateAsync_DoesNothing_WhenTheUserTurnedChecksOff()
    {
        var client = new StubClient(Availability("0.1.6"));
        var coordinator = Coordinator(client, current: "0.1.5", checksEnabled: false);

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.Calls, Is.Zero);
            Assert.That(availability.IsUpdateAvailable, Is.False);
        });
    }

    // Pressing the button IS asking, so the opt-out does not apply to it — it silences the automatic
    // check, not the one the user just requested.
    [Test]
    public async Task EvaluateAsync_StillChecks_WhenForced_EvenWithChecksTurnedOff()
    {
        var client = new StubClient(Availability("0.1.6"));
        var coordinator = Coordinator(client, current: "0.1.5", checksEnabled: false);

        var availability = await coordinator.EvaluateAsync(force: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.Calls, Is.EqualTo(1));
            Assert.That(availability.IsUpdateAvailable, Is.True);
        });
    }

    // A local build stamps 0.1.5.0 from Directory.Build.props while CI stamps 0.1.<run-number>, so a
    // checkout can look older than a release it already contains.
    [Test]
    public async Task EvaluateAsync_OffersNothing_WhenTheRunningVersionIsUnknown()
    {
        var client = new StubClient(Availability("0.1.6"));
        var coordinator = Coordinator(client, current: null);

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(availability.IsUpdateAvailable, Is.False);
    }

    [Test]
    public async Task EvaluateAsync_KeepsTheCachedVerdict_WhenTheFeedAnswersNotModified()
    {
        var stateStore = new StubStateStore(new UpdateCheckState
        {
            LastCheckedUtc = Now.AddHours(-25),
            ETag = "\"abc\"",
            LatestVersion = "0.1.6",
            LatestReleaseUrl = ReleaseUrl,
        });
        var client = new StubClient(UpdateAvailability.None, notModified: true, etag: "\"abc\"");
        var coordinator = Coordinator(client, current: "0.1.5", stateStore: stateStore);

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(availability.IsUpdateAvailable, Is.True, "304 means unchanged, not 'no update'");
    }

    // Going offline must not hide an update the user has already been told about.
    [Test]
    public async Task EvaluateAsync_KeepsTheCachedVerdict_WhenTheFetchFails()
    {
        var stateStore = new StubStateStore(new UpdateCheckState
        {
            LastCheckedUtc = Now.AddHours(-25),
            LatestVersion = "0.1.6",
            LatestReleaseUrl = ReleaseUrl,
        });
        var coordinator = Coordinator(new StubClient(UpdateAvailability.None), current: "0.1.5", stateStore: stateStore);

        var availability = await coordinator.EvaluateAsync(force: false, CancellationToken.None);

        Assert.That(availability.IsUpdateAvailable, Is.True);
    }

    private static UpdateAvailability Availability(string version)
        => new() { LatestVersion = Version.Parse(version), ReleaseUrl = ReleaseUrl };

    private static UpdateNotificationCoordinator Coordinator(
        StubClient client,
        string? current,
        StubStateStore? stateStore = null,
        bool checksEnabled = true)
        => new(
            client,
            stateStore ?? new StubStateStore(new UpdateCheckState()),
            () => checksEnabled,
            current is null ? null : Version.Parse(current),
            () => Now,
            NullLogger<UpdateNotificationCoordinator>.Instance);

    private sealed class StubClient(UpdateAvailability availability, bool notModified = false, string? etag = null) : IUpdateCheckClient
    {
        public int Calls { get; private set; }

        public Task<UpdateCheckResult> FetchLatestAsync(string? requestETag, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new UpdateCheckResult(availability, etag, notModified));
        }
    }

    private sealed class StubStateStore(UpdateCheckState state) : IUpdateCheckStateStore
    {
        public UpdateCheckState Current { get; private set; } = state;

        public void Save(UpdateCheckState next) => Current = next;
    }
}
