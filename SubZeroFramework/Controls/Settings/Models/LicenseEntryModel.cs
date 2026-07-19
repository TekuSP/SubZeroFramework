using Microsoft.UI.Xaml;

namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One third-party library on the Licenses page: identity column (name, version, license chip, and — for
/// transitive dependencies — which parent package imported it) and the full license text. Sourced from the
/// build-generated <c>third-party-licenses.json</c> asset.
/// </summary>
public sealed record LicenseEntryModel(string Name, string Version, string License, string Text, string ImportedBy)
{
    public string ImportedByDisplay { get; } = string.IsNullOrEmpty(ImportedBy) ? string.Empty : $"Imported by {ImportedBy}";

    public Visibility ImportedByVisibility { get; } = string.IsNullOrEmpty(ImportedBy) ? Visibility.Collapsed : Visibility.Visible;
}
