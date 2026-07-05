using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubZeroFramework.Services;

/// <summary>
/// Client-only app settings that never involve the background service (launch behavior, alert opt-ins).
/// Persisted as JSON in the standard per-user application-data folder, next to the display-unit
/// preferences (the app runs unpackaged, so <c>Windows.Storage.ApplicationData</c> is unavailable).
/// </summary>
public interface ILocalClientSettingsStore
{
    string SettingsFilePath { get; }

    bool StartMinimized { get; set; }

    bool ThermalAlertsEnabled { get; set; }
}

public sealed class LocalClientSettingsStore : ILocalClientSettingsStore
{
    private readonly object _gate = new();
    private StoredClientSettings _current;

    public LocalClientSettingsStore()
    {
        SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "SubZeroFramework",
            "client-settings.json");
        _current = ReadFromDisk();
    }

    public string SettingsFilePath { get; }

    public bool StartMinimized
    {
        get => _current.StartMinimized;
        set => Update(_current with { StartMinimized = value });
    }

    public bool ThermalAlertsEnabled
    {
        get => _current.ThermalAlertsEnabled;
        set => Update(_current with { ThermalAlertsEnabled = value });
    }

    private void Update(StoredClientSettings settings)
    {
        lock (_gate)
        {
            _current = settings;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, LocalClientSettingsJsonContext.Default.StoredClientSettings));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The in-memory value stays applied for this session; only persistence failed.
            }
        }
    }

    private StoredClientSettings ReadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(SettingsFilePath), LocalClientSettingsJsonContext.Default.StoredClientSettings)
                    ?? new StoredClientSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt settings file must never block startup; defaults win.
        }

        return new StoredClientSettings();
    }

    internal sealed record StoredClientSettings
    {
        public bool StartMinimized { get; init; }

        public bool ThermalAlertsEnabled { get; init; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalClientSettingsStore.StoredClientSettings))]
internal sealed partial class LocalClientSettingsJsonContext : JsonSerializerContext;
