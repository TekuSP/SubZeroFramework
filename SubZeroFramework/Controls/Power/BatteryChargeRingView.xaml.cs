using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.Foundation;

namespace SubZeroFramework.Controls.Power;

/// <summary>
/// Battery charge ring: a 270° arc (90° gap at the bottom). The grey track spans the full sweep; the live charge is
/// drawn over it in fixed severity zones (red 0–20%, amber 20–40%, green 40–100%), clipped to the current charge. A
/// dashed overlay flows along the charged arc while the pack is charging or discharging.
/// </summary>
public sealed partial class BatteryChargeRingView : UserControl
{
    private const double Diameter = 160d;
    private const double Thickness = 12d;
    private const double StartAngleDegrees = 135d;
    private const double SweepDegrees = 270d;

    // Matches StrokeDashArray "0.5,1.7" (period 2.2, in stroke-thickness units) for a seamless loop.
    private const double DashPeriod = 2.2d;

    private readonly Storyboard _dashStoryboard = new() { RepeatBehavior = RepeatBehavior.Forever };
    private readonly DoubleAnimation _dashAnimation = new()
    {
        From = 0d,
        Duration = new Duration(TimeSpan.FromSeconds(0.7)),
        EnableDependentAnimation = true,
    };

    public BatteryChargeRingView()
    {
        this.InitializeComponent();

        Storyboard.SetTargetProperty(_dashAnimation, "StrokeDashOffset");
        Storyboard.SetTarget(_dashAnimation, DashOverlay);
        _dashStoryboard.Children.Add(_dashAnimation);

        Render();
        Unloaded += (_, _) => _dashStoryboard.Stop();
    }

    public static readonly DependencyProperty ChargeFractionProperty = DependencyProperty.Register(
        nameof(ChargeFraction),
        typeof(double),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(0d, OnVisualChanged));

    /// <summary>Charge level as a fraction 0–1.</summary>
    public double ChargeFraction
    {
        get => (double)GetValue(ChargeFractionProperty);
        set => SetValue(ChargeFractionProperty, value);
    }

    public static readonly DependencyProperty ChargeTextProperty = DependencyProperty.Register(
        nameof(ChargeText),
        typeof(string),
        typeof(BatteryChargeRingView),
        new PropertyMetadata("--"));

    /// <summary>
    /// The bare charge figure shown large in the centre of the ring (e.g. "76"), WITHOUT its unit — the unit
    /// is drawn smaller beside it, from <see cref="ChargeUnitText"/>.
    /// </summary>
    public string ChargeText
    {
        get => (string)GetValue(ChargeTextProperty);
        set => SetValue(ChargeTextProperty, value);
    }

    public static readonly DependencyProperty ChargeUnitTextProperty = DependencyProperty.Register(
        nameof(ChargeUnitText),
        typeof(string),
        typeof(BatteryChargeRingView),
        new PropertyMetadata("%"));

    /// <summary>
    /// The unit drawn small beside the figure. Supplied by the caller rather than hardcoded, because the
    /// ratio unit is a user preference — "%" is only the default, not the only possibility.
    /// </summary>
    public string ChargeUnitText
    {
        get => (string)GetValue(ChargeUnitTextProperty);
        set => SetValue(ChargeUnitTextProperty, value);
    }

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(null, OnCentreContentChanged));

    /// <summary>
    /// Optional battery-state line ("Charging", "Discharging", …) shown with a status dot in place of the
    /// static "charge" caption. Null/empty keeps the plain caption, so existing callers are unchanged.
    /// </summary>
    public string? StatusText
    {
        get => (string?)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public static readonly DependencyProperty StatusDotBrushProperty = DependencyProperty.Register(
        nameof(StatusDotBrush),
        typeof(Brush),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(null, OnCentreContentChanged));

    /// <summary>Fill for the status dot (state colour). Null falls back to the secondary text brush.</summary>
    public Brush? StatusDotBrush
    {
        get => (Brush?)GetValue(StatusDotBrushProperty);
        set => SetValue(StatusDotBrushProperty, value);
    }

    public static readonly DependencyProperty DetailTextProperty = DependencyProperty.Register(
        nameof(DetailText),
        typeof(string),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(null, OnCentreContentChanged));

    /// <summary>Optional fine-print line under the status ("full in ~21 min"). Collapsed when empty.</summary>
    public string? DetailText
    {
        get => (string?)GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }

    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText),
        typeof(string),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(null, OnCentreContentChanged));

    /// <summary>Optional adapter-power figure ("240 W") shown as a pill in the ring's bottom mouth. Collapsed when empty.</summary>
    public string? BadgeText
    {
        get => (string?)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(
        nameof(IsAnimating),
        typeof(bool),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(false, OnVisualChanged));

    /// <summary>When true, the dashed overlay flows (charging/discharging); when false it is hidden and stopped.</summary>
    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    public static readonly DependencyProperty FlowReversedProperty = DependencyProperty.Register(
        nameof(FlowReversed),
        typeof(bool),
        typeof(BatteryChargeRingView),
        new PropertyMetadata(false, OnVisualChanged));

    /// <summary>Flow direction: forward (toward full) when charging, reversed when discharging.</summary>
    public bool FlowReversed
    {
        get => (bool)GetValue(FlowReversedProperty);
        set => SetValue(FlowReversedProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BatteryChargeRingView)d).Render();

    private static void OnCentreContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BatteryChargeRingView)d).RenderCentreContent();

    private void RenderCentreContent()
    {
        var hasStatus = !string.IsNullOrEmpty(StatusText);
        StatusRow.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        ChargeCaption.Visibility = hasStatus ? Visibility.Collapsed : Visibility.Visible;
        DetailLabel.Visibility = string.IsNullOrEmpty(DetailText) ? Visibility.Collapsed : Visibility.Visible;
        Badge.Visibility = string.IsNullOrEmpty(BadgeText) ? Visibility.Collapsed : Visibility.Visible;

        StatusDot.Fill = StatusDotBrush
            ?? Themes.AppThemeBrushes.Get("TextSecondaryBrush", Themes.AppThemeBrushes.TextSecondaryColor);
    }

    private void Render()
    {
        var radius = (Diameter - Thickness) / 2d - 2d;
        var charge = Math.Clamp(ChargeFraction, 0d, 1d);

        GreyTrack.Data = ArcGeometry(radius, 0d, 1d);
        RedZone.Data = charge > 0d ? ArcGeometry(radius, 0d, Math.Min(charge, 0.2d)) : null;
        AmberZone.Data = charge > 0.2d ? ArcGeometry(radius, 0.2d, Math.Min(charge, 0.4d)) : null;
        GreenZone.Data = charge > 0.4d ? ArcGeometry(radius, 0.4d, charge) : null;

        var animate = IsAnimating && charge > 0d;
        DashOverlay.Data = animate ? ArcGeometry(radius, 0d, charge) : null;
        DashOverlay.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;

        if (animate)
        {
            _dashStoryboard.Stop();
            // Charging flows toward full, discharging toward empty. Reverse via swapped From/To (both kept in
            // [0, DashPeriod]) rather than a negative StrokeDashOffset, which Uno Skia does not animate reliably.
            _dashAnimation.From = FlowReversed ? 0d : DashPeriod;
            _dashAnimation.To = FlowReversed ? DashPeriod : 0d;
            _dashStoryboard.Begin();
        }
        else
        {
            _dashStoryboard.Stop();
        }
    }

    private static Geometry ArcGeometry(double radius, double fromFraction, double toFraction)
    {
        const double center = Diameter / 2d;
        var startAngle = (StartAngleDegrees + (fromFraction * SweepDegrees)) * Math.PI / 180d;
        var endAngle = (StartAngleDegrees + (toFraction * SweepDegrees)) * Math.PI / 180d;

        var start = new Point(center + (radius * Math.Cos(startAngle)), center + (radius * Math.Sin(startAngle)));
        var end = new Point(center + (radius * Math.Cos(endAngle)), center + (radius * Math.Sin(endAngle)));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = (toFraction - fromFraction) * SweepDegrees > 180d,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
