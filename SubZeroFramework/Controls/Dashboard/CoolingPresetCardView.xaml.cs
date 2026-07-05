using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Controls.Dashboard;

/// <summary>
/// One cooling-profile preset card (icon + name + description; selected = accent outline + check).
/// Click handling lives in the consumer (the dashboard wraps each card in a Button).
/// </summary>
public sealed partial class CoolingPresetCardView : UserControl
{
    public CoolingPresetCardView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(CoolingPresetCardModel),
        typeof(CoolingPresetCardView),
        new PropertyMetadata(null));

    /// <summary>The preset rendered by this card.</summary>
    public CoolingPresetCardModel? Model
    {
        get => (CoolingPresetCardModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
