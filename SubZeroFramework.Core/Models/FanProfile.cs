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
public sealed record FanProfileEntry
{
    public required int FanIndex { get; init; }

    public required FanControlMode Mode { get; init; }

    /// <summary>Duty for <see cref="FanControlMode.Manual"/>.</summary>
    public double DutyPercent { get; init; }

    /// <summary>Which saved curve for <see cref="FanControlMode.CustomCurve"/>.</summary>
    public int CurveSlot { get; init; }

    /// <summary>Target temperature for <see cref="FanControlMode.Adaptive"/>, canonical Celsius.</summary>
    public double AdaptiveTargetCelsius { get; init; } = AdaptiveFanSettings.DefaultTargetCelsius;
}

/// <summary>
/// A saved fan setup: every fan's mode and settings under one name, applied in one go.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately client-side.</b> A profile is a named batch of commands the service already accepts one at
/// a time, so teaching the service about profiles would add a second place for fan intent to live and a way
/// for the two to disagree. What the fans are actually doing stays the service's answer alone; this only
/// remembers combinations worth returning to.
/// </para>
/// <para>
/// Which also settles what "active" means: it is not a stored flag but a comparison against the live fan
/// states, so it survives a restart, and stops being true the moment the user changes a fan by hand — which
/// is exactly when the UI needs to stop claiming a profile is in effect.
/// </para>
/// </remarks>
public sealed partial record FanProfile
{
    /// <summary>Stable across renames, so a rename does not read as a delete plus an unrelated create.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The name of the icon to show, or null to let the presentation layer derive one from the setup.
    /// </summary>
    /// <remarks>
    /// A name rather than an icon: which icon set draws it is the UI's business, and a model that named a
    /// specific one would drag a presentation dependency into everything that touches a profile. Null for
    /// anything the user saves — there is no icon picker, so a saved profile's icon follows what it does.
    /// </remarks>
    public string? IconName { get; init; }

    /// <summary>Written on first run and marked as such; not otherwise special.</summary>
    public bool IsSeeded { get; init; }

    public ImmutableArray<FanProfileEntry> Fans { get; init; } = [];

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

    private static bool EntryMatches(FanProfileEntry entry, FanControlStateSnapshot state) => entry.Mode switch
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

        FanControlMode.CustomCurve =>
            state.Mode == FanControlMode.CustomCurve && state.ActiveCurveSlot == entry.CurveSlot,

        _ => state.Mode == entry.Mode,
    };
}
