using System.Collections.Immutable;

using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// What one profile wants of one fan.
/// </summary>
/// <remarks>
/// Only the fields the mode actually uses carry meaning, and the rest are kept rather than validated away: a
/// profile saved while a fan was Manual keeps its duty, and still keeps it after the profile is re-saved with
/// that fan on Auto. Discarding them would make re-saving a profile quietly destructive.
/// </remarks>
public sealed record CoolingProfileFanEntry
{
    public required int FanIndex { get; init; }

    public required FanControlMode Mode { get; init; }

    /// <summary>Duty for <see cref="FanControlMode.Manual"/>.</summary>
    public double DutyPercent { get; init; }

    /// <summary>Target temperature for <see cref="FanControlMode.Adaptive"/>, canonical Celsius.</summary>
    public double AdaptiveTargetCelsius { get; init; } = AdaptiveFanSettings.DefaultTargetCelsius;

    /// <summary>
    /// The curve for <see cref="FanControlMode.CustomCurve"/>, carried by the profile itself.
    /// </summary>
    /// <remarks>
    /// EMBEDDED rather than a slot reference, so the profile is self-contained: overwriting a fan's slot
    /// cannot silently change what this profile means, and a profile stays meaningful no matter what the
    /// user has done to their five slots since.
    /// </remarks>
    public ImmutableSortedDictionary<int, double> CurvePoints { get; init; } = ImmutableSortedDictionary<int, double>.Empty;

    /// <summary>How several driving sensors are reduced to one temperature, for the embedded curve.</summary>
    public TemperatureAggregationMode Aggregation { get; init; } = TemperatureAggregationMode.Maximum;

    /// <summary>
    /// Structural equality, because the compiler's is not.
    /// </summary>
    /// <remarks>
    /// A record's generated Equals defers to <see cref="EqualityComparer{T}.Default"/>, and
    /// ImmutableSortedDictionary compares by REFERENCE. Two entries describing exactly the same curve would
    /// therefore test unequal — which would make a round-tripped profile look modified, and every save
    /// republish as a change.
    /// </remarks>
    public bool Equals(CoolingProfileFanEntry? other) =>
        other is not null
        && FanIndex == other.FanIndex
        && Mode == other.Mode
        && DutyPercent.Equals(other.DutyPercent)
        && AdaptiveTargetCelsius.Equals(other.AdaptiveTargetCelsius)
        && Aggregation == other.Aggregation
        && CurvePoints.Count == other.CurvePoints.Count
        && CurvePoints.All(point => other.CurvePoints.TryGetValue(point.Key, out var value) && value.Equals(point.Value));

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(FanIndex, Mode, DutyPercent, AdaptiveTargetCelsius, Aggregation, CurvePoints.Count);
}

/// <summary>
/// A saved fan setup: every fan's mode and settings under one name, applied in one go.
/// </summary>
/// <remarks>
/// <para>
/// The LIBRARY lives in the service; this is the client's view of one entry in it. What the fans are actually
/// doing is still the service's live state alone, which is why <see cref="Matches"/> exists: "active" is not
/// a stored flag but a comparison, so it survives a restart and stops being true the moment the user changes
/// a fan by hand — which is exactly when the UI needs to stop claiming a profile is in effect.
/// </para>
/// <para>
/// Driving sensors are deliberately absent. The profile carries the TARGET and the fan keeps its own sensors:
/// which sensors drive a fan is a property of the hardware, not of the mood the user is in, and a profile
/// overwriting them would silently undo work done on the fan detail page.
/// </para>
/// </remarks>
public sealed partial record CoolingProfile
{
    /// <summary>Stable across renames, so a rename does not read as a delete plus an unrelated create.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The name of the icon to show, or null to let the presentation layer derive one from the setup.
    /// </summary>
    /// <remarks>
    /// A name rather than an icon: which icon set draws it is the UI's business, and a model that named a
    /// specific one would drag a presentation dependency into everything that touches a profile.
    /// </remarks>
    public string? IconName { get; init; }

    /// <summary>
    /// The tint this profile paints the shell with, or null for no tint.
    /// </summary>
    /// <remarks>
    /// ARGB rather than a Brush: a model naming a UI type would drag a presentation dependency into
    /// everything that touches a profile, and a Brush built off the UI thread fails silently besides. Null is
    /// also what "no profile selected" looks like, so the tint carries information rather than decoration.
    /// </remarks>
    public uint? AccentColorArgb { get; init; }

    /// <summary>Written on first run and marked as such; not otherwise special.</summary>
    public bool IsSeeded { get; init; }

    public ImmutableArray<CoolingProfileFanEntry> Fans { get; init; } = [];

    /// <summary>
    /// Structural equality, because the compiler's is not.
    /// </summary>
    /// <remarks>
    /// ImmutableArray compares by REFERENCE under the record's generated Equals, so two profiles listing the
    /// same fans would test unequal. See <see cref="CoolingProfileFanEntry.Equals(CoolingProfileFanEntry)"/>.
    /// </remarks>
    public bool Equals(CoolingProfile? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(IconName, other.IconName, StringComparison.Ordinal)
        && AccentColorArgb == other.AccentColorArgb
        && IsSeeded == other.IsSeeded
        && Fans.SequenceEqual(other.Fans);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Id, Name, IconName, AccentColorArgb, IsSeeded, Fans.Length);

    /// <summary>
    /// Whether the live fan states match what this profile asks for.
    /// </summary>
    /// <remarks>
    /// Every entry must match, and a fan the profile does not mention is not consulted — a profile written
    /// when a module was attached should still read as active once it is removed, rather than silently
    /// becoming un-selectable.
    /// </remarks>
    public bool Matches(IReadOnlyDictionary<int, FanControlStateSnapshot> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        if (Fans.IsEmpty)
        {
            return false;
        }

        var compared = 0;

        foreach (var entry in Fans)
        {
            if (!states.TryGetValue(entry.FanIndex, out var state))
            {
                continue;
            }

            compared++;

            if (!EntryMatches(entry, state))
            {
                return false;
            }
        }

        // Nothing compared means every fan this profile knows about has gone away, which is not a match — it
        // is an empty question, and answering "yes" would light up every stale profile at once.
        return compared > 0;
    }

    private static bool EntryMatches(CoolingProfileFanEntry entry, FanControlStateSnapshot state) => entry.Mode switch
    {
        // Tolerances, not equality. Duty comes back through the EC rounded to whole percent, and an adaptive
        // target set in Fahrenheit converts back a fraction of a degree off — exact comparison would leave a
        // profile reading as modified the instant after it was applied.
        FanControlMode.Manual =>
            state.Mode == FanControlMode.Manual
            && Math.Abs((state.LastDutyPercent ?? double.NaN) - entry.DutyPercent) < 1.5d,

        FanControlMode.Adaptive =>
            state.Mode == FanControlMode.Adaptive
            && Math.Abs(state.AdaptiveSettings.TargetTemperatureCelsius - entry.AdaptiveTargetCelsius) < 0.6d,

        // Against the EMBEDDED curve rather than a slot number, so a profile keeps meaning what it meant even
        // after the user has rebuilt the slot it was captured from.
        FanControlMode.CustomCurve =>
            state.Mode == FanControlMode.CustomCurve
            && entry.CurvePoints.Count == state.CustomCurvePoints.Count
            && entry.CurvePoints.All(point =>
                state.CustomCurvePoints.TryGetValue(point.Key, out var live)
                && Math.Abs(live - point.Value) < 1.5d),

        _ => state.Mode == entry.Mode,
    };
}
