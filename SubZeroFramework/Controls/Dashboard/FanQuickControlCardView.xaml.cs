using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Controls.Dashboard;

/// <summary>
/// One dashboard fan card: ring gauge + "Now driving" + read-only Auto/Manual/Max/Curve mode indicator.
/// Display-only — fans are controlled from the Fan Curve Profiles page.
/// </summary>
public sealed partial class FanQuickControlCardView : UserControl
{
    public FanQuickControlCardView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(FanQuickControlModel),
        typeof(FanQuickControlCardView),
        new PropertyMetadata(null));

    /// <summary>The fan quick-view model rendered by this card.</summary>
    public FanQuickControlModel? Model
    {
        get => (FanQuickControlModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
