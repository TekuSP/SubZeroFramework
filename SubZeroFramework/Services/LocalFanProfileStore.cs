using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

using FrameworkDotnet.Enums;

using Material.Icons;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>
/// The user's saved fan setups, persisted as JSON beside the other client-only settings.
/// </summary>
/// <remarks>
/// Its own file rather than a section of the client settings: a profile list is the one piece of client state
/// a user would miss if it were lost, and keeping it separate means a corrupt or half-written settings file
/// cannot take it with it — nor the reverse.
/// </remarks>
public interface ILocalFanProfileStore
{
    string ProfilesFilePath { get; }

    /// <summary>Every saved profile, in the order they are shown.</summary>
    ImmutableArray<FanProfile> Profiles { get; }

    /// <summary>The profile applied when nothing else has been chosen, or null if none is marked.</summary>
    string? DefaultProfileId { get; }

    /// <summary>Raised after any change, so a view showing the list can follow it.</summary>
    event EventHandler? Changed;

    /// <summary>Adds a profile, or replaces the one with the same id.</summary>
    void Save(FanProfile profile);

    void Delete(string profileId);

    /// <summary>Renames a profile, keeping its id and therefore its identity.</summary>
    void Rename(string profileId, string name);

    void SetDefault(string? profileId);

    /// <summary>
    /// Writes the starting set of profiles, once, if the user has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seeded rather than shipped empty because an empty Profiles section teaches nothing: the feature is only
    /// legible once there is something on the shelf to apply and compare against.
    /// </para>
    /// <para>
    /// <b>Only ever when the list is empty</b>, so deleting a seeded profile is permanent. Re-seeding what the
    /// user threw away is the kind of helpfulness that reads as a bug.
    /// </para>
    /// </remarks>
    void SeedIfEmpty(IReadOnlyCollection<int> fanIndices);
}

/// <inheritdoc />
public sealed partial class LocalFanProfileStore : ILocalFanProfileStore
{
    /// <summary>What the shipped "quick" profile holds fans to, canonical Celsius.</summary>
    private const double GamingTargetCelsius = 72d;

    private readonly object _gate = new();
    private StoredFanProfiles _current;

    public LocalFanProfileStore()
    {
        ProfilesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "SubZeroFramework",
            "fan-profiles.json");

        _current = ReadFromDisk();
    }

    public string ProfilesFilePath { get; }

    public event EventHandler? Changed;

    public ImmutableArray<FanProfile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _current.Profiles.Select(ToProfile)];
            }
        }
    }

    public string? DefaultProfileId
    {
        get
        {
            lock (_gate)
            {
                return _current.DefaultProfileId;
            }
        }
    }

    public void Save(FanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_gate)
        {
            var stored = ToStored(profile);
            var existing = _current.Profiles.FindIndex(candidate => candidate.Id == profile.Id);

            // Replaced in place rather than removed and appended: re-saving a profile should not move it to
            // the end of a row the user has learned the shape of.
            var profiles = existing >= 0
                ? _current.Profiles.SetItem(existing, stored)
                : _current.Profiles.Add(stored);

            Update(_current with { Profiles = profiles });
        }
    }

    public void Delete(string profileId)
    {
        lock (_gate)
        {
            var profiles = _current.Profiles.RemoveAll(candidate => candidate.Id == profileId);
            if (profiles.Count == _current.Profiles.Count)
            {
                return;
            }

            Update(_current with
            {
                Profiles = profiles,

                // A default pointing at a deleted profile would leave the badge on nothing at all.
                DefaultProfileId = _current.DefaultProfileId == profileId ? null : _current.DefaultProfileId,
            });
        }
    }

    public void Rename(string profileId, string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        lock (_gate)
        {
            var index = _current.Profiles.FindIndex(candidate => candidate.Id == profileId);
            if (index < 0)
            {
                return;
            }

            Update(_current with
            {
                Profiles = _current.Profiles.SetItem(index, _current.Profiles[index] with { Name = trimmed }),
            });
        }
    }

    public void SetDefault(string? profileId)
    {
        lock (_gate)
        {
            if (profileId is not null && !_current.Profiles.Any(candidate => candidate.Id == profileId))
            {
                return;
            }

            Update(_current with { DefaultProfileId = profileId });
        }
    }

    public void SeedIfEmpty(IReadOnlyCollection<int> fanIndices)
    {
        ArgumentNullException.ThrowIfNull(fanIndices);

        lock (_gate)
        {
            if (!_current.Profiles.IsEmpty || fanIndices.Count == 0)
            {
                return;
            }

            // Every seeded profile uses only modes that work on a machine nothing has been configured on
            // yet. Adaptive arms without a calibration and runs on its bootstrap model, and Auto and Max ask
            // nothing of the app at all — whereas a curve would name a saved slot that does not exist here,
            // and applying one on a fresh install would fail on every fan.
            var balanced = Seed("Balanced", MaterialIconKind.ScaleBalance, fanIndices, static index => new FanProfileEntry
            {
                FanIndex = index,
                Mode = FanControlMode.Adaptive,
                AdaptiveTargetCelsius = AdaptiveFanSettings.DefaultTargetCelsius,
            });

            var profiles = ImmutableList.Create(
                Seed("Silent", MaterialIconKind.VolumeLow, fanIndices, static index => new FanProfileEntry
                {
                    FanIndex = index,
                    Mode = FanControlMode.Auto,
                }),
                balanced,
                Seed("Gaming", MaterialIconKind.ControllerClassicOutline, fanIndices, static index => new FanProfileEntry
                {
                    FanIndex = index,
                    Mode = FanControlMode.Adaptive,
                    AdaptiveTargetCelsius = GamingTargetCelsius,
                }),
                Seed("Render", MaterialIconKind.Rocket, fanIndices, static index => new FanProfileEntry
                {
                    FanIndex = index,
                    Mode = FanControlMode.Max,
                }));

            Update(_current with { Profiles = profiles, DefaultProfileId = balanced.Id });
        }
    }

    private static StoredFanProfile Seed(
        string name,
        MaterialIconKind icon,
        IReadOnlyCollection<int> fanIndices,
        Func<int, FanProfileEntry> entry)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            IconName = icon.ToString(),
            IsSeeded = true,
            Fans = [.. fanIndices.Order().Select(index => ToStoredEntry(entry(index)))],
        };

    private static FanProfile ToProfile(StoredFanProfile stored) => new()
    {
        Id = stored.Id,
        Name = stored.Name,
        IconName = stored.IconName,
        IsSeeded = stored.IsSeeded,
        Fans = [.. stored.Fans.Select(static fan => new FanProfileEntry
        {
            FanIndex = fan.FanIndex,
            Mode = Enum.TryParse<FanControlMode>(fan.Mode, out var mode) ? mode : FanControlMode.Auto,
            DutyPercent = fan.DutyPercent,
            CurveSlot = fan.CurveSlot,
            AdaptiveTargetCelsius = fan.AdaptiveTargetCelsius,
        })],
    };

    private static StoredFanProfile ToStored(FanProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        IconName = profile.IconName,
        IsSeeded = profile.IsSeeded,
        Fans = [.. profile.Fans.Select(ToStoredEntry)],
    };

    // Modes are stored by NAME, not by their numeric value. The enum comes from a dependency, and a release
    // that inserts a member would otherwise silently reinterpret every saved profile — a stored "3" becoming
    // a different mode is the kind of corruption that looks like the fans misbehaving.
    private static StoredFanProfileEntry ToStoredEntry(FanProfileEntry entry) => new()
    {
        FanIndex = entry.FanIndex,
        Mode = entry.Mode.ToString(),
        DutyPercent = entry.DutyPercent,
        CurveSlot = entry.CurveSlot,
        AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
    };

    private void Update(StoredFanProfiles profiles)
    {
        _current = profiles;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilesFilePath)!);
            File.WriteAllText(ProfilesFilePath, JsonSerializer.Serialize(profiles, LocalFanProfileJsonContext.Default.StoredFanProfiles));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The change stays applied for this session; only persistence failed.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private StoredFanProfiles ReadFromDisk()
    {
        try
        {
            if (File.Exists(ProfilesFilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(ProfilesFilePath), LocalFanProfileJsonContext.Default.StoredFanProfiles)
                    ?? new StoredFanProfiles();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt profile file must never block startup. The user loses their profiles rather than the
            // app, and the next save writes a clean one.
        }

        return new StoredFanProfiles();
    }

    internal sealed record StoredFanProfiles
    {
        public ImmutableList<StoredFanProfile> Profiles { get; init; } = [];

        public string? DefaultProfileId { get; init; }
    }

    internal sealed partial record StoredFanProfile
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? IconName { get; init; }

        public bool IsSeeded { get; init; }

        public ImmutableList<StoredFanProfileEntry> Fans { get; init; } = [];
    }

    internal sealed record StoredFanProfileEntry
    {
        public int FanIndex { get; init; }

        public string Mode { get; init; } = nameof(FanControlMode.Auto);

        public double DutyPercent { get; init; }

        public int CurveSlot { get; init; }

        public double AdaptiveTargetCelsius { get; init; } = AdaptiveFanSettings.DefaultTargetCelsius;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalFanProfileStore.StoredFanProfiles))]
internal sealed partial class LocalFanProfileJsonContext : JsonSerializerContext;
