using SubZeroFramework.Presentation.Units;

namespace SubZeroFramework.Services;

public interface IUserUnitPreferencesClient
{
    string PreferencesFilePath { get; }

    UserUnitPreferencesSnapshot CurrentPreferences { get; }

    Task<UserUnitPreferencesSnapshot> GetPreferencesAsync(CancellationToken cancellationToken = default);

    Task<UserUnitPreferencesSnapshot> UpdatePreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<UserUnitPreferencesSnapshot> ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    IObservable<UserUnitPreferencesSnapshot> WatchPreferences();
}
