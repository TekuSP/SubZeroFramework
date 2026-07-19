using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Settings.Models;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>
/// ViewModel for the Licenses section: reads the build-generated third-party license report and exposes one
/// entry per package. Navigation constructs it (ViewMap-registered); the file read happens off-thread and
/// results marshal back through the dispatcher before touching bindable state.
/// </summary>
public partial class SettingsLicensesSectionModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcherQueue;

    public SettingsLicensesSectionModel(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _dispatcherQueue = dispatcherQueue;

        _ = LoadLicensesAsync();
    }

    public ObservableCollection<LicenseEntryModel> Licenses { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LicensesMessageVisibility))]
    public partial string LicensesMessage { get; set; } = "Loading license report…";

    public Visibility LicensesMessageVisibility => string.IsNullOrEmpty(LicensesMessage) ? Visibility.Collapsed : Visibility.Visible;

    private async Task LoadLicensesAsync()
    {
        try
        {
            var reportPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ThirdPartyLicenses", "third-party-licenses.json");
            if (!File.Exists(reportPath))
            {
                await _dispatcherQueue.EnqueueAsync(() =>
                    LicensesMessage = "The third-party license report was not generated during this build.");
                return;
            }

            var json = await File.ReadAllTextAsync(reportPath).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize(json, SettingsLicensesJsonContext.Default.LicenseReportEntryArray) ?? [];

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                Licenses.Clear();
                foreach (var entry in entries.OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase))
                {
                    Licenses.Add(new LicenseEntryModel(entry.PackageId, entry.Version, entry.License, entry.Text, entry.ImportedBy ?? string.Empty));
                }

                LicensesMessage = Licenses.Count == 0 ? "The license report is empty." : string.Empty;
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await _dispatcherQueue.EnqueueAsync(() =>
                LicensesMessage = $"The third-party license report could not be read: {exception.Message}");
        }
    }

    internal sealed record LicenseReportEntry(string PackageId, string Version, string License, string Text, string? ImportedBy);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsLicensesSectionModel.LicenseReportEntry[]))]
internal sealed partial class SettingsLicensesJsonContext : JsonSerializerContext;
