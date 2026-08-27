using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>
/// One saved fan setup as a dashboard card: what it does, whether it is what the fans are doing now, and
/// whether it is the one marked default.
/// </summary>
/// <remarks>
/// The description is DERIVED from the profile rather than stored with it. A sentence written when the
/// profile was saved goes stale the moment it is re-saved against a different setup, and a card that
/// confidently describes something other than what it will do is worse than a card with no description.
/// </remarks>
public sealed partial class FanProfileCardModel : ObservableObject
{
    private readonly IUnitFormattingService _units;

    public FanProfileCardModel(FanProfile profile, IUnitFormattingService units)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(units);

        _units = units;
        Profile = profile;
        Description = Describe(profile);
    }

    public FanProfile Profile { get; }

    public string Id => Profile.Id;

    public string Name => Profile.Name;

    public MaterialIconKind IconKind => ResolveIcon(Profile);

    public string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(CheckVisibility))]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultBadgeVisibility))]
    public partial bool IsDefault { get; set; }

    public Visibility CheckVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DefaultBadgeVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;

    // Brushes are created at binding time (UI thread) — never cached in fields (see uno-vm-thread-affinity).
    public Brush CardBorderBrush => IsSelected
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.BrandDisabledColor);

    /// <summary>
    /// The icon a profile shows: the one it named, or one derived from what it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no icon picker. An icon is decoration, and a whole selection surface for decoration is not
    /// worth its weight — so a saved profile's icon follows what the profile actually does and stays truthful
    /// when it is re-saved against a different setup. The profiles written on first run name theirs, because
    /// those have identities no rule could infer.
    /// </para>
    /// <para>
    /// Ordered by what DOMINATES the setup rather than by what is merely present, so a mixed profile is named
    /// for its most consequential mode: everything-at-once outranks a single adaptive fan.
    /// </para>
    /// </remarks>
    public static MaterialIconKind ResolveIcon(FanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.IconName is not null && Enum.TryParse<MaterialIconKind>(profile.IconName, out var named))
        {
            return named;
        }

        if (profile.Fans.IsEmpty)
        {
            return MaterialIconKind.TuneVariant;
        }

        if (profile.Fans.All(static fan => fan.Mode == FanControlMode.Max))
        {
            return MaterialIconKind.Rocket;
        }

        if (profile.Fans.All(static fan => fan.Mode == FanControlMode.Auto))
        {
            return MaterialIconKind.VolumeLow;
        }

        if (profile.Fans.Any(static fan => fan.Mode == FanControlMode.Adaptive))
        {
            return MaterialIconKind.ScaleBalance;
        }

        return profile.Fans.Any(static fan => fan.Mode == FanControlMode.CustomCurve)
            ? MaterialIconKind.ChartBellCurveCumulative
            : MaterialIconKind.TuneVariant;
    }

    /// <summary>
    /// What the profile does, in one line.
    /// </summary>
    /// <remarks>
    /// Grouped by mode rather than listed per fan. "Adaptive on 3 fans · Manual 70% on 1" stays readable on a
    /// machine with four fans; naming each one does not, and the card has room for a line, not a table.
    /// </remarks>
    private string Describe(FanProfile profile)
    {
        if (profile.Fans.IsEmpty)
        {
            return "No fans saved in this profile";
        }

        var parts = profile.Fans
            .GroupBy(static fan => fan.Mode)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key)
            .Select(group => DescribeGroup(group.Key, [.. group], profile.Fans.Length));

        return string.Join(" · ", parts);
    }

    private string DescribeGroup(FanControlMode mode, IReadOnlyList<FanProfileEntry> entries, int total)
    {
        // "on every fan" reads better than "on 4 fans" and is the common case for a profile applied wholesale;
        // the count only earns its place when the profile actually splits the fans up.
        var scope = entries.Count == total ? "every fan" : $"{entries.Count} fan{(entries.Count == 1 ? string.Empty : "s")}";

        return mode switch
        {
            FanControlMode.Auto => $"Firmware control on {scope}",
            FanControlMode.Max => $"Full speed on {scope}",

            // Distinct settings are named; a spread is summarised, because listing five different targets is
            // the table this line exists to avoid.
            FanControlMode.Adaptive => DistinctValue(entries, static entry => entry.AdaptiveTargetCelsius) is double target
                ? $"Adaptive {_units.FormatTemperature(target, decimals: 0)} on {scope}"
                : $"Adaptive on {scope}",

            FanControlMode.Manual => DistinctValue(entries, static entry => entry.DutyPercent) is double duty
                ? $"Manual {duty:0}% on {scope}"
                : $"Manual on {scope}",

            _ => $"Curve on {scope}",
        };
    }

    /// <summary>The value every entry shares, or null if they differ.</summary>
    private static double? DistinctValue(IReadOnlyList<FanProfileEntry> entries, Func<FanProfileEntry, double> select)
    {
        var first = select(entries[0]);

        return entries.All(entry => Math.Abs(select(entry) - first) < 0.5d) ? first : null;
    }
}
