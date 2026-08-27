#if DEBUG
using SubZeroFramework.Models;
using SubZeroFramework.Services.Updates;

namespace SubZeroFramework.Services;

/// <summary>
/// Debug-build-only overrides for the update check, so its states can be reached without cutting a release.
/// </summary>
/// <remarks>
/// <para>
/// The update notice is otherwise unreachable on a developer machine: it needs a published GitHub release
/// that is strictly newer than the build in front of you, which is a chicken-and-egg problem exactly when
/// you are trying to look at the UI. Editing <c>Directory.Build.props</c> works but rebuilds the world and
/// is easy to forget to revert — and forgetting it ships a wrong version number.
/// </para>
/// <para>
/// Two flags, because they test different halves:
/// <list type="bullet">
/// <item><c>--fake-version 0.0.1</c> — pretend the RUNNING app is that old. The real GitHub call still
/// happens, so this exercises the whole path end to end. Needs network and a published release.</item>
/// <item><c>--fake-latest 9.9.9</c> — pretend GitHub published that version, skipping the network entirely.
/// Needs neither a release nor a connection, so it is the one that always works.</item>
/// </list>
/// Both may be combined. The whole type is compiled out of RELEASE: a stray argument must never be able to
/// tell a user their install is out of date when it is not.
/// </para>
/// </remarks>
internal static class DebugUpdateOverrides
{
    /// <summary>The release page a faked update points at — real, so the button goes somewhere sensible.</summary>
    private const string ReleasePageUrl = "https://github.com/TekuSP/SubZeroFramework/releases/latest";

    private static readonly Lazy<(Version? Current, Version? Latest)> Parsed = new(ParseCommandLine);

    /// <summary>The version the app should claim to be, or null to use the real one.</summary>
    public static Version? FakeCurrentVersion => Parsed.Value.Current;

    /// <summary>The version GitHub should appear to have published, or null to actually ask it.</summary>
    public static Version? FakeLatestVersion => Parsed.Value.Latest;

    /// <summary>True when the network client should be replaced by <see cref="FakeClient"/>.</summary>
    public static bool HasFakeLatest => FakeLatestVersion is not null;

    private static (Version? Current, Version? Latest) ParseCommandLine()
    {
        var arguments = Environment.GetCommandLineArgs();
        return (ReadVersionAfter(arguments, "--fake-version"), ReadVersionAfter(arguments, "--fake-latest"));
    }

    private static Version? ReadVersionAfter(string[] arguments, string flag)
    {
        for (var i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return AppVersion.Parse(arguments[i + 1]);
            }
        }

        return null;
    }

    /// <summary>Answers with the faked release instead of asking GitHub.</summary>
    internal sealed class FakeClient : IUpdateCheckClient
    {
        /// <inheritdoc />
        public Task<UpdateCheckResult> FetchLatestAsync(string? etag, CancellationToken cancellationToken)
            => Task.FromResult(new UpdateCheckResult(
                new UpdateAvailability { LatestVersion = FakeLatestVersion, ReleaseUrl = ReleasePageUrl },

                // A changing ETag on purpose: a cached one would let the 24 h interval skip the fetch and
                // make the flag look broken on the second launch.
                ETag: null,
                NotModified: false));
    }
}
#endif
