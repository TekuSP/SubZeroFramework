using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

/// <summary>
/// Client-local display-unit preferences store. Units are a presentation concern, so they are owned by
/// this app and persisted in the standard per-user application-data folder. The app runs unpackaged, so
/// <c>Windows.Storage.ApplicationData</c> is unavailable — a plain JSON file under
/// <c>%LOCALAPPDATA%\SubZeroFramework</c> is the standard save location instead.
/// </summary>
public sealed class LocalUserUnitPreferencesClient : IUserUnitPreferencesClient, IDisposable
{
    private readonly UnitPreferenceCatalog _catalog;
    private readonly BehaviorSubject<UserUnitPreferencesSnapshot> _preferencesSubject;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LocalUserUnitPreferencesClient(UnitPreferenceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        PreferencesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "SubZeroFramework",
            "display-unit-preferences.json");
        _preferencesSubject = new BehaviorSubject<UserUnitPreferencesSnapshot>(ReadSnapshotFromDisk());
    }

    public string PreferencesFilePath { get; }

    public UserUnitPreferencesSnapshot CurrentPreferences => _preferencesSubject.Value;

    public Task<UserPreferencesOperationResult> ApplyPreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return PersistAsync(_catalog.Normalize(snapshot), "Display units updated.", cancellationToken);
    }

    public Task<UserPreferencesOperationResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        => PersistAsync(_catalog.CreateDefaultSnapshot(), "Display units restored to defaults.", cancellationToken);

    public IObservable<UserUnitPreferencesSnapshot> WatchPreferences() => _preferencesSubject.AsObservable();

    public void Dispose()
    {
        _preferencesSubject.Dispose();
        _writeLock.Dispose();
    }

    private async Task<UserPreferencesOperationResult> PersistAsync(UserUnitPreferencesSnapshot snapshot, string successMessage, CancellationToken cancellationToken)
    {
        // Publish first so the UI always reflects the selection immediately; a disk failure only affects
        // whether the choice survives a restart, and is reported through the operation result.
        _preferencesSubject.OnNext(snapshot);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var stored = new StoredPreferences(
                snapshot.SchemaVersion,
                [.. snapshot.Entries.Select(entry => new StoredPreferenceEntry(entry.Kind.ToString(), entry.OptionKey))]);

            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesFilePath)!);
            var json = JsonSerializer.Serialize(stored, LocalUserUnitPreferencesJsonContext.Default.StoredPreferences);
            await File.WriteAllTextAsync(PreferencesFilePath, json, cancellationToken).ConfigureAwait(false);

            return new UserPreferencesOperationResult
            {
                Succeeded = true,
                Message = successMessage,
                Preferences = snapshot,
                PreferencesPath = PreferencesFilePath,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserPreferencesOperationResult
            {
                Succeeded = false,
                Message = $"Display units were applied for this session, but saving them failed: {exception.Message}",
                Preferences = snapshot,
                PreferencesPath = PreferencesFilePath,
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private UserUnitPreferencesSnapshot ReadSnapshotFromDisk()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath))
            {
                return _catalog.CreateDefaultSnapshot();
            }

            var stored = JsonSerializer.Deserialize(
                File.ReadAllText(PreferencesFilePath),
                LocalUserUnitPreferencesJsonContext.Default.StoredPreferences);

            if (stored is null)
            {
                return _catalog.CreateDefaultSnapshot();
            }

            return _catalog.Normalize(new UserUnitPreferencesSnapshot
            {
                SchemaVersion = stored.SchemaVersion > 0 ? stored.SchemaVersion : UserUnitPreferencesSnapshot.CurrentSchemaVersion,
                Entries =
                [
                    .. stored.Entries
                        .Where(entry => Enum.TryParse<UnitQuantityKind>(entry.Kind, ignoreCase: true, out _))
                        .Select(entry => new UserUnitPreferenceEntry(
                            Enum.Parse<UnitQuantityKind>(entry.Kind, ignoreCase: true),
                            entry.OptionKey ?? string.Empty))
                ],
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable preferences file must never block startup; defaults win.
            return _catalog.CreateDefaultSnapshot();
        }
    }

    internal sealed record StoredPreferences(int SchemaVersion, StoredPreferenceEntry[] Entries);

    internal sealed record StoredPreferenceEntry(string Kind, string OptionKey);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalUserUnitPreferencesClient.StoredPreferences))]
internal sealed partial class LocalUserUnitPreferencesJsonContext : JsonSerializerContext;
