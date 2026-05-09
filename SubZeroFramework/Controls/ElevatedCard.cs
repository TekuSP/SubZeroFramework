namespace SubZeroFramework.Controls;

public sealed partial class ElevatedCard : ContentControl
{
    public ElevatedCard()
    {
        DefaultStyleKey = typeof(ElevatedCard);
    }

    public static new readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(ElevatedCard),
            new PropertyMetadata(new CornerRadius(8)));

    public new CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty ElevationProperty =
        DependencyProperty.Register(
            nameof(Elevation),
            typeof(double),
            typeof(ElevatedCard),
            new PropertyMetadata(30.0, OnElevationChanged));

    public double Elevation
    {
        get => (double)GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    private Border? _shadowBorder;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        
        _shadowBorder = GetTemplateChild("ShadowBorder") as Border;
        UpdateElevation();
    }

    private static void OnElevationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ElevatedCard card)
        {
            card.UpdateElevation();
        }
    }

    private void UpdateElevation()
    {
        if (_shadowBorder != null)
        {
            _shadowBorder.Translation = new System.Numerics.Vector3(0, 0, (float)Elevation);
        }
    }
}
