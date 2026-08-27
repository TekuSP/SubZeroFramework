using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>
/// The service's cooling profile library, and the commands that change it.
/// </summary>
/// <remarks>
/// The library is the service's, not this client's: every connected app sees the same profiles and the same
/// selection, and a profile saved here shows up there without either side polling.
/// </remarks>
public interface ICoolingProfileClient
{
    /// <summary>Every saved profile, as a change stream.</summary>
    IObservable<IChangeSet<CoolingProfile, string>> WatchCoolingProfiles();

    /// <summary>
    /// Which profile the service has selected, or null for none.
    /// </summary>
    /// <remarks>
    /// Selection, not effect. Whether the fans still match that profile is a separate question the client
    /// answers itself by comparing live fan state — see <see cref="CoolingProfile.Matches"/>.
    /// </remarks>
    IObservable<string?> WatchActiveProfileId();

    Task<CoolingProfileCommandResult> SaveAsync(CoolingProfile profile, CancellationToken cancellationToken = default);

    Task<CoolingProfileCommandResult> DeleteAsync(string profileId, CancellationToken cancellationToken = default);

    Task<CoolingProfileCommandResult> RenameAsync(string profileId, string name, CancellationToken cancellationToken = default);

    /// <summary>Applies a profile and records it as the selection. An empty id deselects without touching a fan.</summary>
    Task<CoolingProfileCommandResult> SetActiveAsync(string profileId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a cooling profile command.</summary>
/// <param name="Succeeded">Whether everything the command asked for happened.</param>
/// <param name="Message">A sentence for the user, or empty when there is nothing to say.</param>
/// <param name="FailedFanNames">
/// Fans that refused. Non-empty alongside <paramref name="Succeeded"/> false means a PARTIAL apply — the rest
/// of the machine did take the profile, so this is a warning rather than an error.
/// </param>
public sealed record CoolingProfileCommandResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> FailedFanNames)
{
    public static CoolingProfileCommandResult Ok { get; } = new(true, string.Empty, []);

    /// <summary>The result for a command that never reached the service.</summary>
    public static CoolingProfileCommandResult Unreachable { get; } =
        new(false, "Could not reach the SubZero service.", []);
}
