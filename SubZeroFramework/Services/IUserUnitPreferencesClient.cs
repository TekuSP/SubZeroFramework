using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

public interface IUserUnitPreferencesClient
{
    string PreferencesFilePath { get; }

    UserUnitPreferencesSnapshot CurrentPreferences { get; }

    Task<UserUnitPreferencesSnapshot> GetPreferencesAsync(CancellationToken cancellationToken = default);

    Task<UserPreferencesOperationResult> ApplyPreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<UserPreferencesOperationResult> SavePreferencesAsync(CancellationToken cancellationToken = default);

    Task<UserPreferencesOperationResult> LoadPreferencesAsync(CancellationToken cancellationToken = default);

    Task<UserPreferencesOperationResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    Task<UserPreferencesOperationResult> RelocatePreferencesStoreAsync(string targetDirectory, CancellationToken cancellationToken = default);

    IObservable<UserUnitPreferencesSnapshot> WatchPreferences();
}
