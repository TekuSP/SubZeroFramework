using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Material.Icons;

using SubZeroFramework.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>One profile in the management list, editable in place.</summary>
/// <remarks>
/// Carries a reference back to the list it belongs to purely so the row's own buttons can bind to commands.
/// An ItemsRepeater template has no DataContext, so a command on the dialog's model is not otherwise
/// reachable from inside the row — and routing every button through a code-behind handler to get at it is a
/// lot of plumbing to avoid one back-reference.
/// </remarks>
public sealed partial class FanProfileRowModel : ObservableObject
{
    public FanProfileRowModel(FanProfileManageDialogModel owner, FanProfile profile, string description)
    {
        Owner = owner;
        Profile = profile;
        Name = profile.Name;
        Description = description;
    }

    public FanProfileManageDialogModel Owner { get; }

    public FanProfile Profile { get; }

    public string Id => Profile.Id;

    public MaterialIconKind IconKind => FanProfileCardModel.ResolveIcon(Profile);

    public string Description { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultStarBrushKey))]
    public partial bool IsDefault { get; set; }

    /// <summary>Lit when this is the default, dim otherwise — the star is the control AND the indicator.</summary>
    public string DefaultStarBrushKey => IsDefault ? "StatusWarningForegroundBrush" : "TextSecondaryBrush";
}

/// <summary>
/// Renaming, re-defaulting and deleting saved profiles.
/// </summary>
/// <remarks>
/// <para>
/// Editing happens against the store immediately rather than being gathered up and committed on close. The
/// dialog has no Save button for the same reason: there is nothing here a user would want to try out and
/// then abandon, and a Cancel that silently undid three renames would be worse than no Cancel at all.
/// </para>
/// <para>
/// Deleting is the exception and asks first, because it is the one action here that destroys something.
/// </para>
/// </remarks>
public sealed partial class FanProfileManageDialogModel : ObservableObject
{
    private readonly ILocalFanProfileStore _store;
    private readonly IUnitFormattingService _units;
    private readonly ObservableCollection<FanProfileRowModel> _rows = [];

    public FanProfileManageDialogModel(ILocalFanProfileStore store, IUnitFormattingService units)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(units);

        _store = store;
        _units = units;
        Rows = new ReadOnlyObservableCollection<FanProfileRowModel>(_rows);

        Refresh();
    }

    public ReadOnlyObservableCollection<FanProfileRowModel> Rows { get; }

    public bool IsEmpty => _rows.Count == 0;

    /// <summary>Set while a delete is waiting to be confirmed; the row it names is the one at risk.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    [NotifyPropertyChangedFor(nameof(DeletePrompt))]
    public partial FanProfileRowModel? PendingDelete { get; set; }

    public bool IsConfirmingDelete => PendingDelete is not null;

    public string DeletePrompt => PendingDelete is { } row
        ? $"Delete “{row.Name}”? This cannot be undone."
        : string.Empty;

    /// <summary>
    /// Writes back any names the user changed. Called once, when the list is closed.
    /// </summary>
    /// <remarks>
    /// Not on every keystroke: writing the file per character is pointless churn, and would briefly persist
    /// every half-typed name on the way to the intended one. Closing the dialog is the moment the user is
    /// done, so it is the moment the names are taken as final.
    /// </remarks>
    public void CommitRenames()
    {
        foreach (var row in _rows)
        {
            // An emptied field reverts rather than saving a nameless profile, which would be unclickable.
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                row.Name = row.Profile.Name;
                continue;
            }

            if (!string.Equals(row.Name.Trim(), row.Profile.Name, StringComparison.Ordinal))
            {
                _store.Rename(row.Id, row.Name);
            }
        }
    }

    [RelayCommand]
    private void MakeDefault(FanProfileRowModel? row)
    {
        if (row is null)
        {
            return;
        }

        // Clicking the current default clears it, so "no default" stays reachable without a separate control.
        _store.SetDefault(row.IsDefault ? null : row.Id);
        Refresh();
    }

    [RelayCommand]
    private void AskDelete(FanProfileRowModel? row) => PendingDelete = row;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (PendingDelete is { } row)
        {
            _store.Delete(row.Id);
            PendingDelete = null;
            Refresh();
        }
    }

    private void Refresh()
    {
        var defaultId = _store.DefaultProfileId;

        _rows.Clear();

        foreach (var profile in _store.Profiles)
        {
            // The same sentence the dashboard card shows, so the list and the card cannot describe the same
            // profile differently.
            var description = new FanProfileCardModel(profile, _units).Description;

            _rows.Add(new FanProfileRowModel(this, profile, description) { IsDefault = profile.Id == defaultId });
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
