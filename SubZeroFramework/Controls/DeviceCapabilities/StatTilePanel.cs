using System;
using System.Collections.Generic;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace SubZeroFramework.Controls.DeviceCapabilities;

/// <summary>
/// Lays out stat tiles in equal-width columns that never shrink below <see cref="MinTileWidth"/>: at full width a
/// row holds <see cref="MaxColumns"/> tiles (the mockup layout), and on narrow panes tiles wrap onto extra rows
/// instead of clipping their text. Tiles in the same row share the row's height, like uniform Grid rows.
/// </summary>
public partial class StatTilePanel : Panel
{
    public static readonly DependencyProperty MinTileWidthProperty = DependencyProperty.Register(
        nameof(MinTileWidth),
        typeof(double),
        typeof(StatTilePanel),
        new PropertyMetadata(160d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns),
        typeof(int),
        typeof(StatTilePanel),
        new PropertyMetadata(4, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(StatTilePanel),
        new PropertyMetadata(10d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing),
        typeof(double),
        typeof(StatTilePanel),
        new PropertyMetadata(10d, OnLayoutPropertyChanged));

    public double MinTileWidth
    {
        get => (double)GetValue(MinTileWidthProperty);
        set => SetValue(MinTileWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((StatTilePanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = GetVisibleChildren();
        if (children.Count == 0)
        {
            return new Size(0, 0);
        }

        var width = ResolveWidth(availableSize.Width, children.Count);
        var columns = ComputeColumns(width, children.Count);
        var tileWidth = ComputeTileWidth(width, columns);

        double totalHeight = 0;
        double rowHeight = 0;
        var column = 0;
        foreach (var child in children)
        {
            child.Measure(new Size(tileWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++column == columns)
            {
                totalHeight += rowHeight + RowSpacing;
                rowHeight = 0;
                column = 0;
            }
        }

        totalHeight = column > 0 ? totalHeight + rowHeight : totalHeight - RowSpacing;
        return new Size(width, Math.Max(totalHeight, 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = GetVisibleChildren();
        if (children.Count == 0)
        {
            return finalSize;
        }

        var columns = ComputeColumns(finalSize.Width, children.Count);
        var tileWidth = ComputeTileWidth(finalSize.Width, columns);

        double y = 0;
        for (var rowStart = 0; rowStart < children.Count; rowStart += columns)
        {
            var rowCount = Math.Min(columns, children.Count - rowStart);

            double rowHeight = 0;
            for (var index = 0; index < rowCount; index++)
            {
                rowHeight = Math.Max(rowHeight, children[rowStart + index].DesiredSize.Height);
            }

            for (var index = 0; index < rowCount; index++)
            {
                children[rowStart + index].Arrange(new Rect(index * (tileWidth + ColumnSpacing), y, tileWidth, rowHeight));
            }

            y += rowHeight + RowSpacing;
        }

        return finalSize;
    }

    private List<UIElement> GetVisibleChildren()
    {
        List<UIElement> children = [];
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Visible)
            {
                children.Add(child);
            }
        }

        return children;
    }

    private double ResolveWidth(double availableWidth, int childCount)
    {
        if (!double.IsInfinity(availableWidth))
        {
            return availableWidth;
        }

        var columns = Math.Clamp(Math.Min(MaxColumns, childCount), 1, int.MaxValue);
        return columns * MinTileWidth + (columns - 1) * ColumnSpacing;
    }

    private int ComputeColumns(double width, int childCount)
    {
        var minWidth = Math.Max(MinTileWidth, 1d);
        var fit = (int)Math.Floor((width + ColumnSpacing) / (minWidth + ColumnSpacing));
        var cap = Math.Max(Math.Min(MaxColumns, childCount), 1);
        return Math.Clamp(fit, 1, cap);
    }

    private double ComputeTileWidth(double width, int columns)
        => Math.Max((width - (columns - 1) * ColumnSpacing) / columns, 0);
}
