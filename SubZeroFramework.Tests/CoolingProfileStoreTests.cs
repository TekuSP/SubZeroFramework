using System.Collections.Immutable;

using DynamicData;

using FrameworkDotnet.Enums;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// The cooling profile library: what it publishes, what it forgets, and what it refuses to do twice.
/// </summary>
[TestFixture]
public class CoolingProfileStoreTests
{
    /// <summary>Persistence that keeps what it was given, so a save can be inspected without a disk.</summary>
    private sealed class RecordingPersistence : ICoolingProfilePersistence
    {
        public IReadOnlyList<CoolingProfile> Seeded { get; init; } = [];

        public string? SeededActiveProfileId { get; init; }

        public bool SeededHasSeeded { get; init; }

        public IReadOnlyList<CoolingProfile> LastSavedProfiles { get; private set; } = [];

        public string? LastSavedActiveProfileId { get; private set; }

        public bool LastSavedHasSeeded { get; private set; }

        public int SaveCount { get; private set; }

        public CoolingProfileLibrary Load() => new(Seeded, SeededActiveProfileId, SeededHasSeeded);

        public void Save(CoolingProfileLibrary library)
        {
            LastSavedProfiles = library.Profiles;
            LastSavedActiveProfileId = library.ActiveProfileId;
            LastSavedHasSeeded = library.HasSeeded;
            SaveCount++;
        }
    }

    private static CoolingProfile Profile(string id, string name) => new() { Id = id, Name = name };

    [Test]
    public void Save_PublishesTheProfileToSubscribers()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();

        store.Save(Profile("p1", "Gaming"));

        Assert.That(observed.Lookup("p1").Value.Name, Is.EqualTo("Gaming"));
    }

    [Test]
    public void Rename_KeepsTheId_SoItDoesNotReadAsADeletePlusAnUnrelatedCreate()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();
        store.Save(Profile("p1", "Gaming"));

        Assert.Multiple(() =>
        {
            Assert.That(store.Rename("p1", "Loud"), Is.True);
            Assert.That(observed.Lookup("p1").Value.Name, Is.EqualTo("Loud"));
            Assert.That(observed.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Rename_ReportsFailure_ForAProfileThatIsNotThere()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());

        Assert.That(store.Rename("missing", "Loud"), Is.False);
    }

    /// <summary>
    /// Deleting the selected profile clears the selection.
    /// </summary>
    /// <remarks>
    /// A selection pointing at nothing would leave the shell tinted by, and naming, a profile the user just
    /// threw away — with no way to deselect it because the card is gone.
    /// </remarks>
    [Test]
    public void Delete_AlsoClearsTheSelection_WhenTheDeletedProfileWasTheActiveOne()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        store.Save(Profile("p1", "Gaming"));
        store.SetActive("p1");

        store.Delete("p1");

        Assert.That(store.ActiveProfileId, Is.Null);
    }

    [Test]
    public void Delete_LeavesTheSelectionAlone_WhenSomeOtherProfileWasDeleted()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        store.Save(Profile("p1", "Gaming"));
        store.Save(Profile("p2", "Quiet"));
        store.SetActive("p1");

        store.Delete("p2");

        Assert.That(store.ActiveProfileId, Is.EqualTo("p1"));
    }

    /// <summary>
    /// The library is never empty: deleting the last profile puts the baseline back, and selects it.
    /// </summary>
    /// <remarks>
    /// This overrides the earlier "never re-seed" rule, which existed to stop deleted profiles reappearing
    /// one at a time. An empty shelf with no profile in effect is a different and worse problem.
    /// </remarks>
    [Test]
    public void DeletingTheLastProfile_PutsTheBaselineBackAndSelectsIt()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();

        store.SeedIfEmpty([0, 1]);
        store.Save(Profile("mine", "Mine"));

        foreach (var existing in observed.Items.ToList())
        {
            store.Delete(existing.Id);
        }

        Assert.Multiple(() =>
        {
            Assert.That(observed.Count, Is.EqualTo(1));
            Assert.That(observed.Items.Single().Name, Is.EqualTo("Default"));
            Assert.That(store.ActiveProfileId, Is.EqualTo(observed.Items.Single().Id));
        });
    }

    [Test]
    public void DeletingOneOfSeveralProfiles_DoesNotResurrectAnything()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();

        store.SeedIfEmpty([0]);
        store.Save(Profile("mine", "Mine"));

        store.Delete("mine");

        Assert.That(observed.Count, Is.EqualTo(1));
    }

    [Test]
    public void SeedIfEmpty_LeavesAnExistingLibraryUntouched()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();
        store.Save(Profile("mine", "Mine"));

        store.SeedIfEmpty([0, 1]);

        Assert.That(observed.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// The refusal to re-seed survives a restart.
    /// </summary>
    /// <remarks>
    /// Deleting every seeded profile leaves an empty library, which looks exactly like a fresh install. Only
    /// the persisted marker tells the two apart — without it the user finds all three back on next launch.
    /// </remarks>
    [Test]
    public void SeedIfEmpty_DoesNothingOnARestart_WhenTheUserHadAlreadyDeletedTheSeeds()
    {
        var persistence = new RecordingPersistence { Seeded = [], SeededHasSeeded = true };
        using var store = new FrameworkCoolingProfileStore(persistence);
        using var observed = store.Connect().AsObservableCache();

        store.SeedIfEmpty([0, 1]);

        Assert.That(observed.Count, Is.Zero);
    }

    [Test]
    public void SeedingRecordsThatItHappened_SoTheNextLaunchKnows()
    {
        var persistence = new RecordingPersistence();
        using var store = new FrameworkCoolingProfileStore(persistence);

        store.SeedIfEmpty([0]);

        Assert.That(persistence.LastSavedHasSeeded, Is.True);
    }

    /// <summary>
    /// Exactly one seeded profile, describing the machine at rest.
    /// </summary>
    /// <remarks>
    /// A shelf that arrives pre-stocked with somebody else's idea of Quiet and Gaming asks the user to curate
    /// a list they did not write. One baseline gives the feature something to be, and makes the plus card the
    /// obvious way to add the rest.
    /// </remarks>
    [Test]
    public void SeedingWritesOneBaselineProfile_WithEveryFanOnAuto()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();

        store.SeedIfEmpty([0, 1]);

        Assert.That(observed.Count, Is.EqualTo(1));

        var seeded = observed.Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(seeded.Name, Is.EqualTo("Default"));
            Assert.That(seeded.IsSeeded, Is.True);
            Assert.That(seeded.Fans.Select(static fan => fan.FanIndex), Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(seeded.Fans.Select(static fan => fan.Mode), Is.All.EqualTo(FanControlMode.Auto));

            // Black. The baseline should look like the shell at rest, not like a colour someone chose.
            Assert.That(seeded.AccentColorArgb, Is.Null);
        });
    }

    /// <summary>
    /// The seeded baseline is also SELECTED.
    /// </summary>
    /// <remarks>
    /// It has no tint, so "nothing selected" and "on the baseline" look identical on screen. Leaving the seed
    /// unselected would make a fresh install indistinguishable from a broken one.
    /// </remarks>
    [Test]
    public void SeedingAlsoSelectsTheBaseline_SoAFreshInstallIsNeverOnNothing()
    {
        using var store = new FrameworkCoolingProfileStore(new RecordingPersistence());
        using var observed = store.Connect().AsObservableCache();

        store.SeedIfEmpty([0]);

        Assert.That(store.ActiveProfileId, Is.EqualTo(observed.Items.Single().Id));
    }

    [Test]
    public void SeedingLeavesAnExistingSelectionAlone()
    {
        var persistence = new RecordingPersistence
        {
            Seeded = [],
            SeededActiveProfileId = "chosen-by-the-user",
        };

        using var store = new FrameworkCoolingProfileStore(persistence);

        store.SeedIfEmpty([0]);

        Assert.That(store.ActiveProfileId, Is.EqualTo("chosen-by-the-user"));
    }

    [Test]
    public void AStoreStartsFromWhatWasPersisted()
    {
        var persistence = new RecordingPersistence
        {
            Seeded = [Profile("p1", "Gaming")],
            SeededActiveProfileId = "p1",
        };

        using var store = new FrameworkCoolingProfileStore(persistence);
        using var observed = store.Connect().AsObservableCache();

        Assert.Multiple(() =>
        {
            Assert.That(observed.Count, Is.EqualTo(1));
            Assert.That(store.ActiveProfileId, Is.EqualTo("p1"));
        });
    }

    [Test]
    public void EveryMutationIsPersisted()
    {
        var persistence = new RecordingPersistence();
        using var store = new FrameworkCoolingProfileStore(persistence);

        store.Save(Profile("p1", "Gaming"));
        store.SetActive("p1");

        Assert.Multiple(() =>
        {
            Assert.That(persistence.SaveCount, Is.EqualTo(2));
            Assert.That(persistence.LastSavedActiveProfileId, Is.EqualTo("p1"));
            Assert.That(persistence.LastSavedProfiles.Select(static profile => profile.Id), Is.EquivalentTo(new[] { "p1" }));
        });
    }

    /// <summary>A profile survives the trip to disk and back without losing anything.</summary>
    [Test]
    public void OptionsRoundTrip_PreservesEverythingAProfileMeans()
    {
        var original = new CoolingProfile
        {
            Id = "p1",
            Name = "Gaming",
            IconName = "Rocket",
            AccentColorArgb = CoolingAccentPalette.AccentBlue,
            IsSeeded = true,
            Fans =
            [
                new CoolingProfileFanEntry
                {
                    FanIndex = 1,
                    Mode = FanControlMode.CustomCurve,
                    DutyPercent = 42d,
                    AdaptiveTargetCelsius = 71d,
                    Aggregation = TemperatureAggregationMode.Average,
                    CurvePoints = ImmutableSortedDictionary.CreateRange([new KeyValuePair<int, double>(60, 55d)]),
                },
            ],
        };

        var restored = CoolingProfileOptions.From(original).ToProfile();

        Assert.That(restored, Is.EqualTo(original));
    }

    [Test]
    public void OptionsToProfile_ToleratesABlobWithNoFans()
        => Assert.That(new CoolingProfileOptions { Id = "p1", Name = "Empty" }.ToProfile().Fans, Is.Empty);
}
