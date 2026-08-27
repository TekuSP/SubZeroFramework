using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Cooling;
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

    public FanProfileCardModel(CoolingProfile profile, IUnitFormattingService units)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(units);

        _units = units;
        Profile = profile;
        Description = Describe(profile);
    }

    public CoolingProfile Profile { get; }

    /// <summary>
    /// Where the card's action buttons send their requests, or null on a card built only to be described.
    /// </summary>
    /// <remarks>
    /// A back-reference for the same reason <c>FanProfileRowModel</c> has one: the buttons live inside an item
    /// template, and a command on the page model is not otherwise reachable from in there.
    /// </remarks>
    public IProfileCardActions? Owner { get; init; }

    /// <summary>
    /// True for the one card that is not a profile: the plus that makes a new one.
    /// </summary>
    /// <remarks>
    /// A card rather than a button beneath the shelf, because "add" belongs in the same row as the things it
    /// adds to — the shelf then explains itself with no heading and no link to read.
    /// </remarks>
    public bool IsAddCard { get; init; }

    public Visibility ProfileContentVisibility => IsAddCard ? Visibility.Collapsed : Visibility.Visible;

    public Visibility AddContentVisibility => IsAddCard ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The plus card, which stands in for a profile without being one.</summary>
    public static FanProfileCardModel CreateAddCard(IProfileCardActions owner, IUnitFormattingService units)
        => new(new CoolingProfile { Id = string.Empty, Name = "New profile" }, units)
        {
            Owner = owner,
            IsAddCard = true,
        };

    /// <summary>
    /// The card's background: its own tint, or the ordinary card surface when it has none.
    /// </summary>
    /// <remarks>
    /// Blended over the CARD colour rather than the sidebar's, so a tint reads the same strength here as it
    /// does on the rail despite sitting on a lighter surface. Built on the UI thread and fresh per card —
    /// never taken from AppThemeBrushes, whose cache hands out the single instance App.xaml shares with every
    /// StaticResource consumer.
    /// </remarks>
    public Brush CardBackgroundBrush => !IsAddCard && Profile.AccentColorArgb is { } accent
        ? new SolidColorBrush(ToColor(AccentBlend.Blend(accent, CardSurfaceArgb)))
        : AppThemeBrushes.Get("CardBackgroundBrush", AppThemeBrushes.CardBackgroundColor);

    /// <summary>
    /// The bar along the top of the card, and the shelf's answer to "which one am I on?".
    /// </summary>
    /// <remarks>
    /// FULL STRENGTH on the selected profile and dimmed on the rest. With every bar at full strength the
    /// shelf was a row of equally loud colours saying nothing about which was in effect; dimming the others
    /// makes the selected one the only thing on the row that is fully lit, without adding a badge or a label
    /// to carry the same information twice.
    /// </remarks>
    public Brush AccentBarBrush
    {
        get
        {
            var brush = Profile.AccentColorArgb is { } accent
                ? new SolidColorBrush(ToColor(accent))
                : new SolidColorBrush(ToColor(AccentBlend.SidebarArgb));

            brush.Opacity = IsSelected ? 1d : DimmedAccentBarOpacity;
            return brush;
        }
    }

    /// <summary>
    /// How far an unselected profile's bar is faded.
    /// </summary>
    /// <remarks>
    /// Deliberately far down. At a third of full strength the row still read as several lit bars competing;
    /// the selected one only becomes obvious once the others are barely more than a hint of their colour.
    /// </remarks>
    private const double DimmedAccentBarOpacity = 0.12d;

    /// <summary>The dashboard's card surface, #2E2E2E, as ARGB.</summary>
    private const uint CardSurfaceArgb = 0xFF2E2E2Eu;

    [RelayCommand]
    private void Rename() => Owner?.RequestProfileAction(Profile, ProfileCardAction.Rename);

    [RelayCommand]
    private void Edit() => Owner?.RequestProfileAction(Profile, ProfileCardAction.Edit);

    [RelayCommand]
    private void Delete() => Owner?.RequestProfileAction(Profile, ProfileCardAction.Delete);

    private static Windows.UI.Color ToColor(uint argb) => Windows.UI.Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    public string Id => Profile.Id;

    public string Name => Profile.Name;

    public Symbol IconSymbol => ResolveIcon(Profile);

    public string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(CheckVisibility))]
    [NotifyPropertyChangedFor(nameof(AccentBarBrush))]
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
    public static Symbol ResolveIcon(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.IconName is not null && Enum.TryParse<Symbol>(profile.IconName, out var named))
        {
            return named;
        }

        if (profile.Fans.IsEmpty)
        {
            return Symbol.Setting;
        }

        if (profile.Fans.All(static fan => fan.Mode == FanControlMode.Max))
        {
            return Symbol.Rotate;
        }

        if (profile.Fans.All(static fan => fan.Mode == FanControlMode.Auto))
        {
            return Symbol.Mute;
        }

        // Refresh, not Target: Target is in the Symbol enum but has no glyph in the shipped icon font, so it
        // rendered as an empty box on any adaptively-driven profile.
        if (profile.Fans.Any(static fan => fan.Mode == FanControlMode.Adaptive))
        {
            return Symbol.Refresh;
        }

        return profile.Fans.Any(static fan => fan.Mode == FanControlMode.CustomCurve)
            ? Symbol.Shuffle
            : Symbol.Setting;
    }

    /// <summary>
    /// What the profile does, in one line.
    /// </summary>
    /// <remarks>
    /// Grouped by mode rather than listed per fan. "Adaptive on 3 fans · Manual 70% on 1" stays readable on a
    /// machine with four fans; naming each one does not, and the card has room for a line, not a table.
    /// </remarks>
    private string Describe(CoolingProfile profile)
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

    private string DescribeGroup(FanControlMode mode, IReadOnlyList<CoolingProfileFanEntry> entries, int total)
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
    private static double? DistinctValue(IReadOnlyList<CoolingProfileFanEntry> entries, Func<CoolingProfileFanEntry, double> select)
    {
        var first = select(entries[0]);

        return entries.All(entry => Math.Abs(select(entry) - first) < 0.5d) ? first : null;
    }
}
