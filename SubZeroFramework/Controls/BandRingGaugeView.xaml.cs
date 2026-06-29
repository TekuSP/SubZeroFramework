using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Controls.Fans.Models;

namespace SubZeroFramework.Controls;

/// <summary>
/// Reusable segmented multi-band speed ring gauge driven by a <see cref="FanCardModel"/>. Renders only the
/// arc (nominal → caution → critical → track); hosts overlay the centre value text and any ghost/target arc.
/// Used by the master fan rows, the detail header, and the mode body at different sizes via
/// <see cref="RingThickness"/>.
/// </summary>
public sealed partial class BandRingGaugeView : UserControl
{
    public BandRingGaugeView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty FanProperty = DependencyProperty.Register(
        nameof(Fan),
        typeof(FanCardModel),
        typeof(BandRingGaugeView),
        new PropertyMetadata(null));

    /// <summary>The fan whose speed bands drive the gauge. May be <c>null</c> when nothing is selected.</summary>
    public FanCardModel? Fan
    {
        get => (FanCardModel?)GetValue(FanProperty);
        set => SetValue(FanProperty, value);
    }

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness),
        typeof(double),
        typeof(BandRingGaugeView),
        new PropertyMetadata(10d));

    /// <summary>Radial width of the ring (<c>MaxRadialColumnWidth</c>); scale per host (row ≈ 5, header ≈ 9, mode ≈ 14).</summary>
    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }
}
