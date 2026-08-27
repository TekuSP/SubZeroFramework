using System.Collections.Immutable;
using System.Reactive.Subjects;

using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Where the cooling profile library is written.
/// </summary>
/// <remarks>
/// A seam rather than a direct dependency on the configuration store, so the profile store can be tested
/// without a settings file, a options monitor, or a disk.
/// </remarks>
public interface ICoolingProfilePersistence
{
    CoolingProfileLibrary Load();

    void Save(CoolingProfileLibrary library);
}

/// <summary>The whole persisted state of the profile feature.</summary>
/// <param name="Profiles">Every saved profile, in order.</param>
/// <param name="ActiveProfileId">The selected profile, or null for none.</param>
/// <param name="HasSeeded">
/// Whether the starting set has ever been written.
/// </param>
/// <remarks>
/// <paramref name="HasSeeded"/> is stored rather than inferred from an empty library, because "empty" is
/// also what a user who deleted every seeded profile leaves behind — and re-seeding what they threw away,
/// on the very next launch, is the kind of helpfulness that reads as a bug.
/// </remarks>
public sealed record CoolingProfileLibrary(
    IReadOnlyList<CoolingProfile> Profiles,
    string? ActiveProfileId,
    bool HasSeeded);

/// <summary>The user's saved cooling profiles and which one they last selected.</summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="FrameworkFanControlStateStore"/>, including its lock: SourceCache.AddOrUpdate is
/// individually thread-safe, but two concurrent read-modify-writes can interleave so the later publish
/// resurrects the earlier lookup's stale fields — and then persists the reverted value.
/// </para>
/// <para>
/// It stores a LIBRARY and a LABEL, never a command. What the fans are actually doing remains the fan
/// control state store's answer alone, which is what keeps two places from holding fan intent and
/// disagreeing about it.
/// </para>
/// </remarks>
public sealed class FrameworkCoolingProfileStore : IDisposable
{
    private readonly SourceCache<CoolingProfile, string> _profiles = new(static profile => profile.Id);
    private readonly BehaviorSubject<string?> _activeProfileId = new(null);
    private readonly ICoolingProfilePersistence _persistence;
    private readonly Lock _gate = new();
    private bool _hasSeeded;
    private bool _disposed;

    /// <summary>
    /// The fans the machine last reported, remembered so the baseline can be rebuilt without them.
    /// </summary>
    /// <remarks>
    /// Deleting the last profile has to re-seed, and a delete carries no fan list of its own. Recovered from
    /// the loaded library at startup so a restart does not lose it.
    /// </remarks>
    private ImmutableArray<int> _knownFanIndices = [];

    public FrameworkCoolingProfileStore(ICoolingProfilePersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        _persistence = persistence;

        var library = persistence.Load();
        _profiles.AddOrUpdate(library.Profiles);
        _activeProfileId.OnNext(library.ActiveProfileId);
        _hasSeeded = library.HasSeeded;

        _knownFanIndices =
        [
            .. library.Profiles
                .SelectMany(static profile => profile.Fans)
                .Select(static fan => fan.FanIndex)
                .Distinct()
                .Order(),
        ];
    }

    /// <summary>The library, as a change stream.</summary>
    public IObservable<IChangeSet<CoolingProfile, string>> Connect() => _profiles.Connect();

    /// <summary>The selection, as a stream. Replays the current value to every new subscriber.</summary>
    public IObservable<string?> ConnectActiveProfileId() => _activeProfileId;

    public string? ActiveProfileId => _activeProfileId.Value;

    public IReadOnlyList<CoolingProfile> Profiles => [.. _profiles.Items];

    /// <summary>The profile with that id, or null if the library has no such profile.</summary>
    public CoolingProfile? Find(string profileId)
    {
        var found = _profiles.Lookup(profileId);
        return found.HasValue ? found.Value : null;
    }

    /// <summary>Adds a profile, or replaces the one with the same id.</summary>
    public void Save(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfDisposed();

        lock (_gate)
        {
            _profiles.AddOrUpdate(profile);
            Persist();
        }
    }

    public void Delete(string profileId)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            var removed = _profiles.Lookup(profileId);
            _profiles.RemoveKey(profileId);

            // A selection pointing at nothing would leave the shell naming a profile the user just deleted,
            // and tinted by it.
            if (string.Equals(_activeProfileId.Value, profileId, StringComparison.Ordinal))
            {
                _activeProfileId.OnNext(null);
            }

            // THE LIBRARY IS NEVER EMPTY. Deleting the last profile puts the baseline back and selects it,
            // rather than leaving a shelf with nothing on it and no profile in effect. This deliberately
            // overrides the "never re-seed" rule, which exists to stop deleted profiles reappearing one at a
            // time; an empty shelf is a different problem, and a worse one.
            if (_profiles.Count == 0)
            {
                var fanIndices = _knownFanIndices.Length > 0
                    ? _knownFanIndices
                    : [.. removed.HasValue ? removed.Value.Fans.Select(static fan => fan.FanIndex) : []];

                if (fanIndices.Length > 0)
                {
                    var seeds = CoolingProfileSeeds.Build(fanIndices);
                    _profiles.AddOrUpdate(seeds);
                    _activeProfileId.OnNext(seeds[0].Id);
                }
            }

            Persist();
        }
    }

    /// <summary>Renames a profile, keeping its id and therefore its identity.</summary>
    /// <returns>False when no profile has that id.</returns>
    public bool Rename(string profileId, string name)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            var existing = _profiles.Lookup(profileId);
            if (!existing.HasValue)
            {
                return false;
            }

            _profiles.AddOrUpdate(existing.Value with { Name = name });
            Persist();
            return true;
        }
    }

    public void SetActive(string? profileId)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            _activeProfileId.OnNext(profileId);
            Persist();
        }
    }

    /// <summary>
    /// Writes the starting set of profiles, once, if the user has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seeded rather than shipped empty because an empty Profiles section teaches nothing: the feature is
    /// only legible once there is something on the shelf to apply and compare against.
    /// </para>
    /// <para>
    /// <b>Only ever when the library is empty</b>, so deleting a seeded profile is permanent. Re-seeding what
    /// the user threw away is the kind of helpfulness that reads as a bug.
    /// </para>
    /// </remarks>
    public void SeedIfEmpty(IReadOnlyCollection<int> fanIndices)
    {
        ArgumentNullException.ThrowIfNull(fanIndices);
        ThrowIfDisposed();

        if (fanIndices.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            // Remembered even when this call goes on to seed nothing: this is the only place the store is
            // told which fans exist, and a later delete-to-empty needs that list to rebuild the baseline.
            _knownFanIndices = [.. fanIndices.Distinct().Order()];

            // The MARKER, not the count. An empty library is also what a user who deleted every seeded
            // profile leaves behind, and re-seeding those on the next launch would undo a deliberate choice.
            if (_hasSeeded || _profiles.Count > 0)
            {
                return;
            }

            _hasSeeded = true;

            var seeds = CoolingProfileSeeds.Build(fanIndices);
            _profiles.AddOrUpdate(seeds);

            // SELECTED, not merely present. "Nothing selected" and "on the baseline" look identical — both
            // leave the shell untinted — so leaving the seed unselected would make the very first run a state
            // the user cannot tell apart from a broken one, and cannot leave except by picking something.
            if (_activeProfileId.Value is null && seeds.Length > 0)
            {
                _activeProfileId.OnNext(seeds[0].Id);
            }

            Persist();
        }
    }

    /// <summary>
    /// Throws the whole library away and writes the starting set back, as on a fresh install.
    /// </summary>
    /// <param name="fanIndices">The fans the seed should describe.</param>
    /// <remarks>
    /// Re-seeds IMMEDIATELY rather than clearing the marker and waiting: the seed worker has already run and
    /// completed by this point, so leaving it to re-seed would mean an empty shelf until the next service
    /// start. A factory reset should leave the machine looking like a first run, not like a wipe.
    /// </remarks>
    public void ResetToFactoryDefaults(IReadOnlyCollection<int> fanIndices)
    {
        ArgumentNullException.ThrowIfNull(fanIndices);
        ThrowIfDisposed();

        lock (_gate)
        {
            _profiles.Clear();
            _activeProfileId.OnNext(null);
            _hasSeeded = false;
        }

        SeedIfEmpty(fanIndices);
    }

    private void Persist() => _persistence.Save(new CoolingProfileLibrary([.. _profiles.Items], _activeProfileId.Value, _hasSeeded));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        _disposed = true;
        _profiles.Dispose();
        _activeProfileId.Dispose();
    }
}
