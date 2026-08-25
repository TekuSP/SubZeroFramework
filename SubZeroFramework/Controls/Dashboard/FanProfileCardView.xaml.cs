using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Controls.Dashboard;

/// <summary>
/// One saved fan setup as a card (icon + name + what it does; active = accent outline + check).
/// Click handling lives in the consumer (the dashboard wraps each card in a Button).
/// </summary>
public sealed partial class FanProfileCardView : UserControl
{
    public FanProfileCardView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(FanProfileCardModel),
        typeof(FanProfileCardView),
        new PropertyMetadata(null));

    /// <summary>The profile rendered by this card.</summary>
    public FanProfileCardModel? Model
    {
        get => (FanProfileCardModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
