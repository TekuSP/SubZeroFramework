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

    /// <summary>
    /// Opt-out for the once-a-day GitHub release check and the notice it raises. On by default.
    /// </summary>
    /// <remarks>
    /// Silences only the AUTOMATIC check. Pressing "Check for updates" is the user asking, and asking is
    /// always honoured — this is the setting for someone deliberately staying on an older release, not a
    /// master switch over their own button.
    /// </remarks>
    bool AutomaticUpdateChecksEnabled { get; set; }

    /// <summary>Returns every client-side setting to its shipped value, as on a fresh install.</summary>
    void ResetToDefaults();
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

    public bool AutomaticUpdateChecksEnabled
    {
        get => _current.AutomaticUpdateChecksEnabled;
        set => Update(_current with { AutomaticUpdateChecksEnabled = value });
    }

    /// <inheritdoc />
    /// <remarks>
    /// A fresh record rather than a field-by-field reset, so a setting added later cannot be forgotten here
    /// and quietly survive a factory reset.
    /// </remarks>
    public void ResetToDefaults() => Update(new StoredClientSettings());

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

        // Defaults ON: the check is cheap, silent when it fails, and the notice is the only way a user
        // learns a release exists. The initialiser is what makes the default hold for the settings files
        // that already exist without this property.
        public bool AutomaticUpdateChecksEnabled { get; init; } = true;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalClientSettingsStore.StoredClientSettings))]
internal sealed partial class LocalClientSettingsJsonContext : JsonSerializerContext;
