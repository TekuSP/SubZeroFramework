using Material.Icons;
using Material.Icons.UNO;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls;

/// <summary>
/// Shows a small chevron at each end of a scrollable area while there is more content that way.
/// </summary>
/// <remarks>
/// <para>
/// Opt in with one attribute — <c>controls:ScrollHint.IsEnabled="True"</c> on the ScrollViewer — because the
/// alternative shapes all cost more. Retemplating ScrollViewer would mean copying its whole default template
/// and pinning this app to one Uno version of it; a sibling overlay control would need every page to already
/// have a Grid in the right place, which they do not (the ScrollViewers here sit inside Grids, Borders and
/// deeper nestings in roughly equal measure).
/// </para>
/// <para>
/// So it WRAPS instead: at load it puts a Grid where the element was and drops the element plus the chevron
/// into it. The layout properties that positioned the element move to the wrapper, so the page lays out
/// exactly as before. Anything it cannot place safely is left alone — a hint that never appears is a far
/// better failure than a page whose layout silently shifts.
/// </para>
/// <para>
/// Works on a <see cref="ScrollViewer"/> and equally on a control that scrolls INSIDE its own template —
/// ListView and GridView. For those the wrapper goes around the control (a normal element in the page tree)
/// while the scroll state is read from the ScrollViewer inside the template; reparenting a template part
/// would be asking for trouble. Because the chevron is driven by live scrollable height, applying this to a
/// list that turns out not to scroll — a GridView already inside a page ScrollViewer, say — costs nothing:
/// the hint simply never shows.
/// </para>
/// </remarks>
public static class ScrollHint
{
    /// <summary>
    /// How close to an end counts as "already there". Scroll offsets are doubles and rarely land exactly on
    /// zero or on ScrollableHeight, so an equality test would leave a chevron showing at the very end.
    /// </summary>
    private const double EdgeEpsilon = 1.5d;

    private const string WrapperName = "ScrollHintWrapper";

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollHint),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (e.NewValue is true)
        {
            element.Loaded += OnElementLoaded;

            // An element already in the tree when the property is set never raises Loaded again.
            if (element.IsLoaded)
            {
                Attach(element);
            }
        }
        else
        {
            element.Loaded -= OnElementLoaded;
        }
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Attach(element);
        }
    }

    private static void Attach(FrameworkElement element)
    {
        // Loaded fires again on every re-parent (navigation caches pages), so wrapping must be idempotent.
        if (element.Parent is Grid { Name: WrapperName })
        {
            return;
        }

        // For a ScrollViewer that is itself; for a ListView or GridView it is the scroller inside the
        // applied template, which exists by the time Loaded runs.
        var scrollViewer = element as ScrollViewer ?? FindDescendantScrollViewer(element);
        if (scrollViewer is null)
        {
            return;
        }

        var upChevron = CreateChevron(MaterialIconKind.ChevronUp, VerticalAlignment.Top);
        var downChevron = CreateChevron(MaterialIconKind.ChevronDown, VerticalAlignment.Bottom);

        if (!TryWrap(element, upChevron, downChevron))
        {
            return;
        }

        void Update()
        {
            FadeTo(upChevron, HasMoreAbove(scrollViewer) ? 1d : 0d);
            FadeTo(downChevron, HasMoreBelow(scrollViewer) ? 1d : 0d);
        }

        scrollViewer.ViewChanged += (_, _) => Update();
        scrollViewer.SizeChanged += (_, _) => Update();

        // The content can grow after load (telemetry cards arriving), which changes scrollability without
        // any scroll or resize of the viewer itself.
        if (scrollViewer.Content is FrameworkElement content)
        {
            content.SizeChanged += (_, _) => Update();
        }

        Update();
    }

    /// <summary>True when there is content below the current viewport.</summary>
    private static bool HasMoreBelow(ScrollViewer scrollViewer)
        => scrollViewer.ScrollableHeight > EdgeEpsilon
            && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - EdgeEpsilon;

    /// <summary>
    /// True when there is content above the current viewport.
    /// </summary>
    /// <remarks>
    /// Needs no scrollable-height test of its own: an offset above zero already proves the area scrolls.
    /// </remarks>
    private static bool HasMoreAbove(ScrollViewer scrollViewer)
        => scrollViewer.VerticalOffset > EdgeEpsilon;

    /// <summary>
    /// Walks the applied template for the control's own scroller.
    /// </summary>
    /// <remarks>
    /// Breadth-first and shallow-biased on purpose: the scroller a ListView owns sits near the root of its
    /// template, whereas a nested one belonging to an item would be deeper — and driving the hint from an
    /// item's scroller would make it flicker with whatever the list happened to virtualise.
    /// </remarks>
    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        Queue<DependencyObject> queue = new();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var childCount = VisualTreeHelper.GetChildrenCount(current);

            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }

                queue.Enqueue(child);
            }
        }

        return null;
    }

    /// <summary>
    /// Puts a Grid where the element was, containing the element and the chevron above it.
    /// </summary>
    /// <returns>False when the parent is a shape this cannot rearrange without risking the layout.</returns>
    private static bool TryWrap(FrameworkElement scrollViewer, UIElement upChevron, UIElement downChevron)
    {
        var wrapper = new Grid { Name = WrapperName };

        void Fill()
        {
            wrapper.Children.Add(scrollViewer);
            wrapper.Children.Add(upChevron);
            wrapper.Children.Add(downChevron);
        }

        switch (scrollViewer.Parent)
        {
            case Panel panel:
            {
                var index = panel.Children.IndexOf(scrollViewer);
                if (index < 0)
                {
                    return false;
                }

                panel.Children.RemoveAt(index);
                MoveLayoutProperties(scrollViewer, wrapper);
                Fill();
                panel.Children.Insert(index, wrapper);
                return true;
            }

            case ContentControl contentControl when ReferenceEquals(contentControl.Content, scrollViewer):
                contentControl.Content = null;
                MoveLayoutProperties(scrollViewer, wrapper);
                Fill();
                contentControl.Content = wrapper;
                return true;

            case Border border when ReferenceEquals(border.Child, scrollViewer):
                border.Child = null;
                MoveLayoutProperties(scrollViewer, wrapper);
                Fill();
                border.Child = wrapper;
                return true;

            // UserControl derives from Control, NOT ContentControl, so it never matched the case above and
            // the hint silently did nothing on every ScrollViewer sitting directly inside one — which is the
            // shape of most of this app's section views.
            case UserControl userControl when ReferenceEquals(userControl.Content, scrollViewer):
                userControl.Content = null;
                MoveLayoutProperties(scrollViewer, wrapper);
                Fill();
                userControl.Content = wrapper;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the properties that positioned the ScrollViewer onto the wrapper, so the page lays out the same.
    /// </summary>
    /// <remarks>
    /// Grid cell attachments especially: leaving <c>Grid.Row</c> on the inner ScrollViewer would place the
    /// wrapper in row 0 and the page would visibly jump. The ScrollViewer keeps stretch alignment inside the
    /// wrapper, which is what it had relative to its old cell.
    /// </remarks>
    private static void MoveLayoutProperties(FrameworkElement from, FrameworkElement to)
    {
        Grid.SetRow(to, Grid.GetRow(from));
        Grid.SetColumn(to, Grid.GetColumn(from));
        Grid.SetRowSpan(to, Grid.GetRowSpan(from));
        Grid.SetColumnSpan(to, Grid.GetColumnSpan(from));
        Grid.SetRow(from, 0);
        Grid.SetColumn(from, 0);
        Grid.SetRowSpan(from, 1);
        Grid.SetColumnSpan(from, 1);

        to.Margin = from.Margin;
        from.Margin = new Thickness(0);

        to.HorizontalAlignment = from.HorizontalAlignment;
        to.VerticalAlignment = from.VerticalAlignment;
        from.HorizontalAlignment = HorizontalAlignment.Stretch;
        from.VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>
    /// One chevron: centred against the given edge, muted, and transparent to the pointer so it can never
    /// swallow a click meant for the content underneath it.
    /// </summary>
    private static UIElement CreateChevron(MaterialIconKind kind, VerticalAlignment alignment)
    {
        var icon = new MaterialIcon
        {
            Kind = kind,
            Width = 18,
            Height = 18,
            Foreground = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor),
        };

        return new Border
        {
            Name = alignment == VerticalAlignment.Top ? "ScrollHintChevronUp" : "ScrollHintChevronDown",
            Child = icon,
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = AppThemeBrushes.Get("CardSecondaryBackgroundBrush", AppThemeBrushes.CardBackgroundColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = alignment,
            Margin = alignment == VerticalAlignment.Top ? new Thickness(0, 8, 0, 0) : new Thickness(0, 0, 0, 8),
            IsHitTestVisible = false,
            Opacity = 0d,
        };
    }

    /// <summary>How long a chevron takes to fade in or out.</summary>
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(140);

    /// <summary>
    /// Fades <paramref name="chevron"/> to <paramref name="opacity"/>.
    /// </summary>
    /// <param name="chevron">The chevron to fade.</param>
    /// <param name="opacity">The opacity to end at.</param>
    /// <remarks>
    /// An explicit storyboard rather than <c>UIElement.OpacityTransition</c>: that property is a WinUI
    /// implicit animation Uno does not implement (Uno0001), so the fade it was supposed to provide simply
    /// did not happen on the Skia head — the chevrons snapped. A storyboard behaves the same on both.
    /// EnableDependentAnimation is required because Opacity here is not composition-animated.
    /// </remarks>
    private static void FadeTo(UIElement chevron, double opacity)
    {
        // Re-running the same fade on every scroll event would restart it continuously and leave the
        // chevron flickering; ViewChanged fires for the whole length of a scroll.
        if (Math.Abs(chevron.Opacity - opacity) < 0.01d)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = new Duration(FadeDuration),
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, chevron);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
