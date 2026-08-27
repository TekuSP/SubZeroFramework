using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

/// <summary>
/// Renaming, re-defaulting and deleting saved profiles.
/// </summary>
/// <remarks>
/// Every action binds straight to a command on the row's owner, so there is nothing to handle here. Renames
/// are the one thing not written as they are typed; the caller commits them once this closes.
/// </remarks>
public sealed partial class FanProfileManageDialog : ContentDialog
{
    public FanProfileManageDialog(FanProfileManageDialogModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        this.InitializeComponent();
    }

    public FanProfileManageDialogModel ViewModel { get; }
}
