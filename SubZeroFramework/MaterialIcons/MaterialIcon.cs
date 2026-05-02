using System;
using System.Collections.Generic;
using System.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Material.Icons.UNO;

public partial class MaterialIcon : Control
{
    public static readonly DependencyProperty DataProperty
        = DependencyProperty.Register(nameof(Data), typeof(string), typeof(MaterialIcon), new PropertyMetadata(""));

    public static readonly DependencyProperty KindProperty
        = DependencyProperty.Register(nameof(Kind), typeof(MaterialIconKind), typeof(MaterialIcon),
            new PropertyMetadata(default(MaterialIconKind), KindPropertyChangedCallback));

    public MaterialIcon()
    {
        this.DefaultStyleKey = typeof(MaterialIcon);
        // ..OverrideMetadata(typeof(MaterialIcon), new FrameworkPropertyMetadata(typeof(MaterialIcon)));
    }

    /// <summary>
    /// Gets the icon path data for the current <see cref="Kind"/>.
    /// </summary>
    [TypeConverter(typeof(GeometryConverter))]
    public string? Data
    {
        get => (string?)GetValue(DataProperty);
        private set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon to display.
    /// </summary>
    public MaterialIconKind Kind
    {
        get => (MaterialIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        UpdateData();
    }

    private static void KindPropertyChangedCallback(DependencyObject dependencyObject,
                                                                DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs)
        => ((MaterialIcon)dependencyObject).UpdateData();

    private void UpdateData()
    {
        Data = MaterialIconDataProvider.GetData(Kind);
    }
}
