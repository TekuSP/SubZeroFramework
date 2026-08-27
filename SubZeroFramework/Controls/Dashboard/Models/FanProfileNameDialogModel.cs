using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Cooling;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>Which of the three jobs the profile dialog is doing.</summary>
public enum ProfileDialogMode
{
    /// <summary>Saving what the fans are doing now as a new profile.</summary>
    Create,

    /// <summary>Changing only the name of an existing profile.</summary>
    Rename,

    /// <summary>Changing an existing profile's appearance, and optionally what it does.</summary>
    Edit,
}

// The curated swatch strip is gone. Every swatch showed its colour BLENDED over the sidebar at the tint's
// real 18% strength, which is correct and useless: nine circles of near-black. The colour picker alone is
// both honest and enough.

/// <summary>One icon in the picker.</summary>
/// <remarks>
/// WinUI's built-in Fluent set (<see cref="Symbol"/>) rather than the Material icons the rail uses for its
/// own few glyphs. It is enumerable, every member is guaranteed to render, and it is the same family the
/// WinUI 3 Gallery's icon page draws from — no glyph table to hand-maintain and no risk of a profile showing
/// an empty box because a codepoint was mistyped.
/// </remarks>
public sealed partial class ProfileIconModel : ObservableObject
{
    public ProfileIconModel(Symbol symbol)
    {
        Symbol = symbol;
        Name = symbol.ToString();
    }

    public Symbol Symbol { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionBorderThickness))]
    public partial bool IsSelected { get; set; }

    /// <summary>A ring around the chosen icon. A converter would be a lot of ceremony for one number.</summary>
    public Thickness SelectionBorderThickness => new(IsSelected ? 2d : 0d);
}

/// <summary>
/// Naming a new profile, with the setup it is about to capture stated alongside.
/// </summary>
/// <remarks>
/// The summary is not decoration. A profile is saved from whatever the fans happen to be doing at that
/// moment, and that is not visible from a dialog covering the page — so the one thing this must show is what
/// is actually being saved, before it is saved under a name that will outlive the memory of it.
/// </remarks>
public sealed partial class FanProfileNameDialogModel : ObservableObject
{
    private readonly IReadOnlyCollection<string> _existingNames;
    private readonly ObservableCollection<ProfileIconModel> _icons = [];

    /// <param name="existingNames">Names already taken. The profile's own name must NOT be in here when editing.</param>
    /// <param name="mode">Which of the three jobs this dialog is doing.</param>
    /// <param name="existing">The profile being changed, or null when creating one.</param>
    public FanProfileNameDialogModel(
        IReadOnlyCollection<string> existingNames,
        ProfileDialogMode mode = ProfileDialogMode.Create,
        CoolingProfile? existing = null)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        Mode = mode;
        _existingNames = existingNames;

        Icons = new ReadOnlyObservableCollection<ProfileIconModel>(_icons);

        if (existing is not null)
        {
            Name = existing.Name;
            SelectedIconName = existing.IconName;
            SelectedAccentArgb = existing.AccentColorArgb;

            if (existing.AccentColorArgb is { } accent)
            {
                // Seeds the picker with the profile's own colour, so opening Edit shows what it already is
                // rather than an unrelated default.
                CustomAccentColor = Windows.UI.Color.FromArgb(
                    0xFF,
                    (byte)((accent >> 16) & 0xFF),
                    (byte)((accent >> 8) & 0xFF),
                    (byte)(accent & 0xFF));
            }
        }

        RefreshIcons();
    }

    public ProfileDialogMode Mode { get; }

    public string DialogTitle => Mode switch
    {
        ProfileDialogMode.Rename => "Rename profile",
        ProfileDialogMode.Edit => "Edit profile",
        _ => "Save as profile",
    };

    public string PrimaryButtonText => Mode switch
    {
        ProfileDialogMode.Rename => "Rename",
        ProfileDialogMode.Edit => "Save changes",
        _ => "Save",
    };

    /// <summary>The label above the name box. Renaming says what it is for; the others need only a field name.</summary>
    public string NameHeader => Mode == ProfileDialogMode.Rename ? "Rename your profile:" : "Name";

    /// <summary>Renaming is only about the name, so the icon and colour pickers stay out of the way.</summary>
    public Visibility AppearanceVisibility => Mode == ProfileDialogMode.Rename ? Visibility.Collapsed : Visibility.Visible;

    // Editing changes a profile's NAME, COLOUR and ICON — nothing else. What a profile does to the fans is
    // fixed when it is created; there is no re-capture here, so editing can never surprise anyone by
    // rewriting the setup they saved.

    public ReadOnlyObservableCollection<ProfileIconModel> Icons { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>What the user typed into the icon search box.</summary>
    [ObservableProperty]
    public partial string IconSearch { get; set; } = string.Empty;

    /// <summary>The chosen icon's enum name, or null to let the card derive one from the setup.</summary>
    [ObservableProperty]
    public partial string? SelectedIconName { get; private set; }

    /// <summary>The chosen tint, or null for none.</summary>
    [ObservableProperty]
    public partial uint? SelectedAccentArgb { get; private set; }

    /// <summary>
    /// Bound to the Community Toolkit's colour picker button, for a tint outside the curated strip.
    /// </summary>
    /// <remarks>
    /// Only the HUE that arrives here is used: <see cref="AccentBlend"/> owns the strength and clamps
    /// anything that would leave the rail's icons unreadable, so no colour chosen here can break the shell.
    /// </remarks>
    [ObservableProperty]
    public partial Windows.UI.Color CustomAccentColor { get; set; }

    partial void OnCustomAccentColorChanged(Windows.UI.Color value)
    {
        // A fully transparent value is the property's own default, not a choice — applying it on load would
        // silently tint a profile the user has not touched.
        if (value.A == 0)
        {
            return;
        }

        SelectCustomAccent(value);
    }

    public string TrimmedName => Name.Trim();

    public bool IsValid => TrimmedName.Length > 0 && !Collides;

    /// <summary>
    /// Why the name will not do, or empty while it will.
    /// </summary>
    /// <remarks>
    /// Blank while the field is still empty: a dialog that opens already complaining reads as an error the
    /// user caused, when they have simply not typed yet.
    /// </remarks>
    public string ValidationMessage => TrimmedName.Length > 0 && Collides
        ? $"There is already a profile called “{TrimmedName}”."
        : string.Empty;

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>Picks an icon, or unpicks it if it was already chosen.</summary>
    public void SelectIcon(ProfileIconModel? icon)
    {
        var clearing = icon is null || string.Equals(icon.Name, SelectedIconName, StringComparison.Ordinal);
        SelectedIconName = clearing ? null : icon!.Name;

        foreach (var candidate in _icons)
        {
            candidate.IsSelected = !clearing && ReferenceEquals(candidate, icon);
        }
    }

    /// <summary>Applies a colour chosen from the picker.</summary>
    /// <remarks>
    /// Only the HUE is taken; <see cref="AccentBlend"/> owns the strength, and clamps anything that would
    /// leave the rail's icons unreadable. That is why a custom colour cannot break the shell however dark or
    /// pale the user picks.
    /// </remarks>
    public void SelectCustomAccent(Windows.UI.Color color)
        => SelectedAccentArgb = (uint)((0xFF << 24) | (color.R << 16) | (color.G << 8) | color.B);

    partial void OnIconSearchChanged(string value) => RefreshIcons();

    private void RefreshIcons()
    {
        var selected = SelectedIconName;
        _icons.Clear();

        foreach (var symbol in MatchingIcons())
        {
            _icons.Add(new ProfileIconModel(symbol)
            {
                IsSelected = string.Equals(symbol.ToString(), selected, StringComparison.Ordinal),
            });
        }
    }

    /// <summary>
    /// Every Fluent symbol, filtered by the search box.
    /// </summary>
    /// <remarks>
    /// THE WHOLE SET, alphabetically, when nothing is typed. A curated shortlist hid most of what WinUI
    /// offers behind a search box the user had no reason to believe would find anything more.
    /// </remarks>
    private IEnumerable<Symbol> MatchingIcons()
    {
        var query = IconSearch.Trim();

        var all = Enum.GetValues<Symbol>().Distinct();

        if (query.Length == 0)
        {
            return all.OrderBy(static symbol => symbol.ToString(), StringComparer.CurrentCultureIgnoreCase);
        }

        return all
            .Where(symbol => symbol.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase))

            // Names that START with the query first: typing "play" should surface Play before StopSlideShow.
            .OrderBy(symbol => symbol.ToString().StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(static symbol => symbol.ToString(), StringComparer.CurrentCultureIgnoreCase);
    }

    // Case-insensitive, because two profiles differing only in capitals are indistinguishable on a card.
    private bool Collides => _existingNames.Any(existing => string.Equals(existing, TrimmedName, StringComparison.CurrentCultureIgnoreCase));
}
