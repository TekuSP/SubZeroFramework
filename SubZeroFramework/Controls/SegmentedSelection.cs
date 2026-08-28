using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace SubZeroFramework.Controls;

/// <summary>
/// Keeps a segmented pill built from <see cref="ToggleButton"/>s from ever having nothing selected.
/// </summary>
/// <remarks>
/// <para>
/// Opt in with one attribute on each segment — <c>controls:SegmentedSelection.KeepsSelection="True"</c>.
/// </para>
/// <para>
/// These pills are RADIO groups wearing ToggleButton clothes: a fan is always in some mode, a chart always
/// has some history window. But a ToggleButton unchecks itself when the user clicks it while checked, and
/// every one of these binds <c>IsChecked</c> ONE WAY from the view model. So clicking the already-active
/// segment unchecked it locally, the view model's value did not change, the binding therefore never pushed
/// back, and the pill was left showing no selection at all — a state the underlying data cannot represent.
/// </para>
/// <para>
/// Re-asserting the check on click is enough: the view model stays the only thing that decides which segment
/// is lit, and clicking the active one becomes the no-op it always should have been.
/// </para>
/// </remarks>
public static class SegmentedSelection
{
    public static readonly DependencyProperty KeepsSelectionProperty = DependencyProperty.RegisterAttached(
        "KeepsSelection",
        typeof(bool),
        typeof(SegmentedSelection),
        new PropertyMetadata(false, OnKeepsSelectionChanged));

    public static bool GetKeepsSelection(DependencyObject element) => (bool)element.GetValue(KeepsSelectionProperty);

    public static void SetKeepsSelection(DependencyObject element, bool value) => element.SetValue(KeepsSelectionProperty, value);

    private static void OnKeepsSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton toggle)
        {
            return;
        }

        // Unhooked first so toggling the property twice cannot double-subscribe.
        toggle.Click -= OnToggleClick;

        if (e.NewValue is true)
        {
            toggle.Click += OnToggleClick;
        }
    }

    /// <remarks>
    /// Runs BEFORE the view model's own Click handler or Command, which is what makes it safe: the segment
    /// is put back to checked, and whatever the view model then does with the selection is unaffected.
    /// </remarks>
    private static void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle)
        {
            toggle.IsChecked = true;
        }
    }
}
