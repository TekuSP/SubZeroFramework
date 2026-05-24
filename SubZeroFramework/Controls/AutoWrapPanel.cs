using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace SubZeroFramework.Controls;

/// <summary>
/// Arranges children in left-aligned wrapping rows while allowing each row to grow to the tallest measured child.
/// </summary>
public sealed partial class AutoWrapPanel : Panel
{
    /// <summary>
    /// Identifies the fixed child width used for measurement and arrangement.
    /// </summary>
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(AutoWrapPanel),
        new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the horizontal spacing between wrapped child elements.
    /// </summary>
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(AutoWrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the vertical spacing between wrapped rows.
    /// </summary>
    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(AutoWrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>
    /// Gets or sets the width assigned to each child; use <see cref="double.NaN" /> to measure children at natural width.
    /// </summary>
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between items in the same row.
    /// </summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between rows.
    /// </summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <summary>
    /// Measures children into wrapping rows and reports the combined height needed by all rows.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var availableWidth = ResolveAvailableWidth(availableSize.Width);
        var childConstraint = new Size(ResolveChildWidthConstraint(availableWidth), double.PositiveInfinity);
        var currentRowWidth = 0d;
        var currentRowHeight = 0d;
        var desiredWidth = 0d;
        var desiredHeight = 0d;

        foreach (var child in Children)
        {
            child.Measure(childConstraint);

            var childWidth = ResolveChildWidth(child.DesiredSize.Width);
            var childHeight = child.DesiredSize.Height;
            var spacing = currentRowWidth > 0d ? HorizontalSpacing : 0d;

            if (currentRowWidth > 0d && currentRowWidth + spacing + childWidth > availableWidth)
            {
                desiredWidth = Math.Max(desiredWidth, currentRowWidth);
                desiredHeight += currentRowHeight + VerticalSpacing;
                currentRowWidth = childWidth;
                currentRowHeight = childHeight;
                continue;
            }

            currentRowWidth += spacing + childWidth;
            currentRowHeight = Math.Max(currentRowHeight, childHeight);
        }

        if (currentRowWidth > 0d || currentRowHeight > 0d)
        {
            desiredWidth = Math.Max(desiredWidth, currentRowWidth);
            desiredHeight += currentRowHeight;
        }

        var finalWidth = double.IsInfinity(availableSize.Width) ? desiredWidth : availableSize.Width;
        return new Size(finalWidth, desiredHeight);
    }

    /// <summary>
    /// Places children from the left edge and wraps to a new row when the next child would exceed the available width.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var availableWidth = ResolveAvailableWidth(finalSize.Width);
        var x = 0d;
        var y = 0d;
        var currentRowHeight = 0d;

        foreach (var child in Children)
        {
            var childWidth = ResolveChildWidth(child.DesiredSize.Width);
            var childHeight = child.DesiredSize.Height;
            var spacing = x > 0d ? HorizontalSpacing : 0d;

            if (x > 0d && x + spacing + childWidth > availableWidth)
            {
                x = 0d;
                y += currentRowHeight + VerticalSpacing;
                currentRowHeight = 0d;
                spacing = 0d;
            }

            var arrangeWidth = childWidth;
            child.Arrange(new Rect(x + spacing, y, arrangeWidth, childHeight));
            x += spacing + arrangeWidth;
            currentRowHeight = Math.Max(currentRowHeight, childHeight);
        }

        return finalSize;
    }

    private static void OnLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is AutoWrapPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    private static double ResolveAvailableWidth(double width)
        => double.IsInfinity(width) || width <= 0d ? double.MaxValue : width;

    private double ResolveChildWidthConstraint(double availableWidth)
        => double.IsNaN(ItemWidth) || ItemWidth <= 0d ? availableWidth : ItemWidth;

    private double ResolveChildWidth(double desiredWidth)
        => double.IsNaN(ItemWidth) || ItemWidth <= 0d ? desiredWidth : ItemWidth;
}
