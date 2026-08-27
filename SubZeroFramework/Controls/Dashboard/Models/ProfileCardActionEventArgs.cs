using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>A card asking the page to open a dialog.</summary>
/// <param name="profile">The profile the button belonged to.</param>
/// <param name="action">What was asked for.</param>
public sealed class ProfileCardActionEventArgs(CoolingProfile? profile, ProfileCardAction action) : EventArgs
{
    /// <summary>The profile acted on, or null for <see cref="ProfileCardAction.Add"/>.</summary>
    public CoolingProfile? Profile { get; } = profile;

    public ProfileCardAction Action { get; } = action;
}
