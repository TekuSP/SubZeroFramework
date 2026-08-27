using System.Text.Json;
using System.Text.Json.Serialization;

using SubZeroFramework.Services.Updates;

namespace SubZeroFramework.Services;

/// <summary>
/// Stores the update-check state next to the other client-only preferences.
/// </summary>
/// <remarks>
/// Separate from <see cref="LocalClientSettingsStore"/> deliberately: that file holds what the USER chose,
/// this one holds what the network last said. Mixing a cache into a settings file makes a corrupt cache look
/// like lost settings.
/// </remarks>
public sealed class UpdateCheckStateStore : IUpdateCheckStateStore
{
    private readonly object _gate = new();

    /// <summary>Creates the store and reads whatever a previous session left behind.</summary>
    public UpdateCheckStateStore()
    {
        StateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "SubZeroFramework",
            "update-check.json");
        Current = ReadFromDisk();
    }

    /// <summary>Where the state lives.</summary>
    public string StateFilePath { get; }

    /// <inheritdoc />
    public UpdateCheckState Current { get; private set; }

    /// <inheritdoc />
    public void Save(UpdateCheckState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            Current = state;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
                File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state, UpdateCheckStateJsonContext.Default.UpdateCheckState));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A cache that could not be written costs one extra request next launch. Nothing to report.
            }
        }
    }

    private UpdateCheckState ReadFromDisk()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(StateFilePath), UpdateCheckStateJsonContext.Default.UpdateCheckState)
                    ?? new UpdateCheckState();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt cache must never block startup; an empty state simply re-checks.
        }

        return new UpdateCheckState();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateCheckState))]
internal sealed partial class UpdateCheckStateJsonContext : JsonSerializerContext;
