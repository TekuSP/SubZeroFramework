using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

/// <summary>
/// Owner of the user's display-unit selections. Display units are a presentation concern, so they live
/// entirely on the client (see <see cref="LocalUserUnitPreferencesClient"/>) — the background service is
/// not involved. Changes apply immediately and persist across sessions.
/// </summary>
public interface IUserUnitPreferencesClient
{
    string PreferencesFilePath { get; }

    UserUnitPreferencesSnapshot CurrentPreferences { get; }

    Task<UserPreferencesOperationResult> ApplyPreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<UserPreferencesOperationResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    IObservable<UserUnitPreferencesSnapshot> WatchPreferences();
}
