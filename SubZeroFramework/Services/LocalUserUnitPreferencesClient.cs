using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

public sealed class LocalUserUnitPreferencesClient : IUserUnitPreferencesClient, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Lock _gate = new();
    private readonly UnitPreferenceCatalog _catalog;
    private readonly BehaviorSubject<UserUnitPreferencesSnapshot> _preferencesSubject;

    public LocalUserUnitPreferencesClient(UnitPreferenceCatalog catalog)
    {
        _catalog = catalog;
        PreferencesFilePath = BuildPreferencesFilePath();
        _preferencesSubject = new BehaviorSubject<UserUnitPreferencesSnapshot>(LoadPreferences());
    }

    public string PreferencesFilePath { get; }

    public UserUnitPreferencesSnapshot CurrentPreferences => _preferencesSubject.Value;

    public Task<UserUnitPreferencesSnapshot> GetPreferencesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentPreferences);

    public async Task<UserUnitPreferencesSnapshot> UpdatePreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var normalizedSnapshot = _catalog.Normalize(snapshot);
        await PersistPreferencesAsync(normalizedSnapshot, cancellationToken).ConfigureAwait(false);
        _preferencesSubject.OnNext(normalizedSnapshot);
        return normalizedSnapshot;
    }

    public Task<UserUnitPreferencesSnapshot> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        => UpdatePreferencesAsync(_catalog.CreateDefaultSnapshot(), cancellationToken);

    public IObservable<UserUnitPreferencesSnapshot> WatchPreferences()
        => _preferencesSubject.AsObservable();

    public void Dispose()
    {
        _preferencesSubject.Dispose();
    }

    private UserUnitPreferencesSnapshot LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath))
            {
                return PersistDefaults();
            }

            var json = File.ReadAllText(PreferencesFilePath);
            var snapshot = JsonSerializer.Deserialize<UserUnitPreferencesSnapshot>(json, SerializerOptions);
            var normalizedSnapshot = _catalog.Normalize(snapshot);

            if (!SnapshotsEquivalent(snapshot, normalizedSnapshot))
            {
                PersistPreferences(normalizedSnapshot);
            }

            return normalizedSnapshot;
        }
        catch
        {
            return PersistDefaults();
        }
    }

    private UserUnitPreferencesSnapshot PersistDefaults()
    {
        var defaults = _catalog.CreateDefaultSnapshot();
        PersistPreferences(defaults);
        return defaults;
    }

    private void PersistPreferences(UserUnitPreferencesSnapshot snapshot)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesFilePath)!);
            File.WriteAllText(PreferencesFilePath, JsonSerializer.Serialize(snapshot, SerializerOptions));
        }
    }

    private Task PersistPreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string serializedSnapshot = JsonSerializer.Serialize(snapshot, SerializerOptions);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesFilePath)!);
            File.WriteAllText(PreferencesFilePath, serializedSnapshot);
        }

        return Task.CompletedTask;
    }

    private static string BuildPreferencesFilePath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "SubZeroFramework", "user-preferences.json");
    }

    private static bool SnapshotsEquivalent(UserUnitPreferencesSnapshot? left, UserUnitPreferencesSnapshot right)
    {
        if (left is null || left.SchemaVersion != right.SchemaVersion || left.Entries.Length != right.Entries.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Entries.Length; index++)
        {
            if (left.Entries[index] != right.Entries[index])
            {
                return false;
            }
        }

        return true;
    }
}
