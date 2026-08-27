using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>What a profile card's buttons ask for.</summary>
public enum ProfileCardAction
{
    /// <summary>Save what the fans are doing now as a new profile. Carries no profile.</summary>
    Add,

    /// <summary>Change only the name.</summary>
    Rename,

    /// <summary>Change its appearance, and optionally re-capture what the fans are doing into it.</summary>
    Edit,

    /// <summary>Remove it from the library.</summary>
    Delete,
}

/// <summary>
/// Where a card's buttons send their requests.
/// </summary>
/// <remarks>
/// The card raises intent; it does not act. Every one of these opens a ContentDialog, and a dialog needs a
/// XamlRoot — which lives on the page, not on a view model. So the page model forwards the request and the
/// page answers it.
/// </remarks>
public interface IProfileCardActions
{
    /// <param name="profile">The profile the button belonged to, or null for <see cref="ProfileCardAction.Add"/>.</param>
    /// <param name="action">What was asked for.</param>
    void RequestProfileAction(CoolingProfile? profile, ProfileCardAction action);
}
