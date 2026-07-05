using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One About row: component name, its version (live-updated for values carried on the status stream),
/// and an optional GitHub link.
/// </summary>
public partial class AboutRowModel(string name, string initialValue, string? linkUrl) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Value { get; set; } = initialValue;

    public Uri? LinkUri { get; } = linkUrl is null ? null : new Uri(linkUrl);

    public Visibility LinkVisibility { get; } = linkUrl is null ? Visibility.Collapsed : Visibility.Visible;
}
