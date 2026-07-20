namespace SubZeroFramework.Services.Navigation;

/// <summary>
/// A view model whose in-progress edits are STAGED and would be lost on navigation. The shell and the
/// Settings page query <see cref="HasUnsavedChanges"/> before leaving a page/section and call
/// <see cref="DiscardUnsavedChangesAsync"/> when the user confirms discarding them.
/// </summary>
public interface IUnsavedChangesGuard
{
    /// <summary>True while staged edits differ from the applied state (a leave would lose them).</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Reverts the staged edits back to the applied state (the user chose "Discard").</summary>
    Task DiscardUnsavedChangesAsync();
}
