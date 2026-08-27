using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.Dashboard.Models;

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

    public FanProfileNameDialogModel(string summary, IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        Summary = summary;
        _existingNames = existingNames;
    }

    /// <summary>What the new profile will capture, in the same words the card will use.</summary>
    public string Summary { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string Name { get; set; } = string.Empty;

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

    // Case-insensitive, because two profiles differing only in capitals are indistinguishable on a card.
    private bool Collides => _existingNames.Any(existing => string.Equals(existing, TrimmedName, StringComparison.CurrentCultureIgnoreCase));
}
