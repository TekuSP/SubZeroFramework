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

    bool ThermalAlertsEnabled { get; set; }

    /// <summary>The warning temperature (canonical Celsius) the hottest sensor must reach to raise a thermal alert.</summary>
    double ThermalAlertThresholdCelsius { get; set; }

    /// <summary>Opt-in for service/fan-control status notifications (restart, install, curve applied, connection lost, …).</summary>
    bool StatusNotificationsEnabled { get; set; }
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

    public bool ThermalAlertsEnabled
    {
        get => _current.ThermalAlertsEnabled;
        set => Update(_current with { ThermalAlertsEnabled = value });
    }

    public double ThermalAlertThresholdCelsius
    {
        get => _current.ThermalAlertThresholdCelsius;
        set => Update(_current with { ThermalAlertThresholdCelsius = value });
    }

    public bool StatusNotificationsEnabled
    {
        get => _current.StatusNotificationsEnabled;
        set => Update(_current with { StatusNotificationsEnabled = value });
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

    // "Start minimized" was removed 2026-07-18 (no tray icon, so a hidden launch had no way back);
    // an old startMinimized JSON property is simply ignored on read.
    internal sealed record StoredClientSettings
    {
        public bool ThermalAlertsEnabled { get; init; }

        public double ThermalAlertThresholdCelsius { get; init; } = ThermalAlertMonitor.DefaultThresholdCelsius;

        public bool StatusNotificationsEnabled { get; init; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalClientSettingsStore.StoredClientSettings))]
internal sealed partial class LocalClientSettingsJsonContext : JsonSerializerContext;
