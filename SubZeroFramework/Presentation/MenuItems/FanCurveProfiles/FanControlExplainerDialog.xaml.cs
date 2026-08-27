using SubZeroFramework.Controls.FanCurveProfiles.Models;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// The reference behind the Adaptive editor: what SubZero measured on this fan, what was decided once and
/// shipped, and why each choice was made.
/// </summary>
/// <remarks>
/// Read-only and stateless. It is opened from the middle of an edit, so it takes a snapshot of the fan when it
/// is constructed and never writes anything back — closing it leaves whatever the user had staged untouched.
/// </remarks>
public sealed partial class FanControlExplainerDialog : ContentDialog
{
    public FanControlExplainerDialog(FanControlExplainerModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        this.InitializeComponent();
    }

    public FanControlExplainerModel ViewModel { get; }
}
