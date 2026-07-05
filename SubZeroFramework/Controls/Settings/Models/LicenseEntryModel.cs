namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One third-party library on the Licenses page: identity column (name, version, license chip) and the
/// full license text. Sourced from the build-generated <c>third-party-licenses.json</c> asset.
/// </summary>
public sealed record LicenseEntryModel(string Name, string Version, string License, string Text);
