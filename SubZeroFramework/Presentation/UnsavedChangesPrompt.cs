using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SubZeroFramework.Presentation;

/// <summary>
/// Shared confirm-before-discard dialog shown when the user tries to leave a page or Settings section that
/// has unsaved staged changes. Data loss is a blocking decision, so a modal <see cref="ContentDialog"/> is
/// the right surface (per the app-error-feedback conventions).
/// </summary>
internal static class UnsavedChangesPrompt
{
    /// <summary>Returns true when the user chose to discard the changes and leave; false to stay.</summary>
    public static async Task<bool> ConfirmDiscardAsync(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var dialog = new ContentDialog
        {
            Title = "Discard unsaved changes?",
            Content = "You have changes here that haven't been saved. Leaving now discards them.",
            PrimaryButtonText = "Discard changes",
            CloseButtonText = "Stay",
            // Enter must not confirm a destructive discard — default to staying.
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
