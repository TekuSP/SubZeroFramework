using Material.Icons;

using Microsoft.UI.Xaml;

namespace SubZeroFramework.Controls;

/// <summary>
/// One settings/dashboard toggle row: accent MDI glyph + title + secondary subtitle on the left, a bare
/// ToggleSwitch pill on the right. Shared by the Settings "Startup &amp; alerts" pane and the Dashboard
/// quick-toggles card.
/// </summary>
public sealed partial class ToggleRowView : UserControl
{
    public ToggleRowView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind),
        typeof(MaterialIconKind),
        typeof(ToggleRowView),
        new PropertyMetadata(MaterialIconKind.ToggleSwitch));

    /// <summary>The MDI glyph leading the row.</summary>
    public MaterialIconKind IconKind
    {
        get => (MaterialIconKind)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(ToggleRowView),
        new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(ToggleRowView),
        new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn),
        typeof(bool),
        typeof(ToggleRowView),
        new PropertyMetadata(false));

    /// <summary>Two-way switch state; bind with Mode=TwoWay from the consumer.</summary>
    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public static readonly DependencyProperty IsToggleEnabledProperty = DependencyProperty.Register(
        nameof(IsToggleEnabled),
        typeof(bool),
        typeof(ToggleRowView),
        new PropertyMetadata(true));

    /// <summary>Gates the switch without dimming the descriptive text (e.g. while the service is unreachable).</summary>
    public bool IsToggleEnabled
    {
        get => (bool)GetValue(IsToggleEnabledProperty);
        set => SetValue(IsToggleEnabledProperty, value);
    }
}
