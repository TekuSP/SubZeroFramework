using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>
/// ViewModel for the About section: component versions and project links. Navigation constructs it
/// (ViewMap-registered); the EC build and connection-library rows ride the live status stream, marshaled to
/// the UI thread. The page that navigated here disposes it when another section takes over.
/// </summary>
public sealed partial class SettingsAboutSectionModel : ObservableObject, IDisposable
{
    private const string SubZeroRepositoryUrl = "https://github.com/TekuSP/SubZeroFramework";
    private const string FrameworkDotnetRepositoryUrl = "https://github.com/TekuSP/framework-dotnet";
    private const string FfiExtensionsRepositoryUrl = "https://github.com/TekuSP/framework-system-ffi-extensions";
    private const string FrameworkSystemRepositoryUrl = "https://github.com/FrameworkComputer/framework-system";

    private readonly CompositeDisposable _subscriptions = [];

    public SettingsAboutSectionModel(
        IFrameworkStatusClient frameworkStatusClient,
        IHardwareInfoClient hardwareInfoClient,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(frameworkStatusClient);
        ArgumentNullException.ThrowIfNull(hardwareInfoClient);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        AboutRows =
        [
            new AboutRowModel("SubZero", ResolveAppVersion(), SubZeroRepositoryUrl),
            new AboutRowModel("EC Build", "Waiting for service", null),
            new AboutRowModel("framework-dotnet", ResolveFrameworkDotnetVersion(), FrameworkDotnetRepositoryUrl),
            new AboutRowModel("framework-system-ffi-extensions", ResolveAssemblyMetadata("FrameworkSystemFfiExtensionsVersion"), FfiExtensionsRepositoryUrl),
            new AboutRowModel("framework-system", ResolveAssemblyMetadata("FrameworkSystemVersion"), FrameworkSystemRepositoryUrl),
        ];

        frameworkStatusClient
            .WatchStatus()
            .Sample(TelemetryRateLimits.LiveReadout)
            .Select(status => Observable.FromAsync(_ => dispatcherQueue.EnqueueAsync(() => ApplyStatus(status))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        // The same firmware the Device Capabilities page lists, repeated here on purpose. About is where a
        // person goes to collect everything a bug report needs, and sending them to another page for half of
        // it is how half of it goes missing.
        hardwareInfoClient
            .WatchHardwareInfo()
            .Sample(TelemetryRateLimits.Inventory)
            .Select(snapshot => Observable.FromAsync(_ => dispatcherQueue.EnqueueAsync(() => ApplyFirmware(snapshot.Firmware))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    public IReadOnlyList<AboutRowModel> AboutRows { get; }

    /// <summary>Component firmware versions. Empty on a machine that reports none.</summary>
    public ObservableCollection<AboutRowModel> FirmwareRows { get; } = [];

    /// <summary>Hides the heading as well as the list, so no lone heading sits over nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareVisibility))]
    public partial bool HasFirmware { get; private set; }

    public Visibility FirmwareVisibility => HasFirmware ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Rebuilds the firmware rows.
    /// </summary>
    /// <remarks>
    /// Flat and ungrouped, unlike Device Capabilities: this page is a list of "component — version" lines and
    /// a person reads it top to bottom or copies the lot. Group headings would be structure without purpose
    /// here, so each row carries its own qualifier instead.
    /// </remarks>
    private void ApplyFirmware(FirmwareInventorySnapshot firmware)
    {
        List<AboutRowModel> rows = [];

        AddFirmwareRows(rows, firmware.Cameras, "Camera");
        AddFirmwareRows(rows, firmware.InputModules, "Input module");
        AddFirmwareRows(rows, firmware.UsbHubs, "USB hub");
        AddFirmwareRows(rows, firmware.AudioCards, "Audio card");

        foreach (var controller in firmware.PowerDeliveryControllers)
        {
            // Through the shared catalog, not the raw ProductName. The firmware calls these Right01 / Left23,
            // which is the EC's port numbering and is off by one against the USB-C 1-4 labels on the Power
            // page — the whole reason that catalog exists.
            rows.Add(new AboutRowModel(
                $"PD controller — {FirmwareComponentDisplay.PowerDeliverySlotName(controller)}",
                controller.Version,
                linkUrl: null));
        }

        if (firmware.RetimerVersion.Length > 0)
        {
            rows.Add(new AboutRowModel("Retimer", firmware.RetimerVersion, linkUrl: null));
        }

        foreach (var drive in firmware.NvmeDrives)
        {
            rows.Add(new AboutRowModel(drive.ModelNumber, drive.FirmwareVersion, linkUrl: null));
        }

        // Compared by name and value, because AboutRowModel is a mutable ObservableObject rather than a
        // record: rebuilding an equal list every inventory tick would replace every row for no visible change.
        if (rows.Count == FirmwareRows.Count
            && rows.Zip(FirmwareRows).All(static pair => pair.First.Name == pair.Second.Name && pair.First.Value == pair.Second.Value))
        {
            return;
        }

        FirmwareRows.Clear();
        foreach (var row in rows)
        {
            FirmwareRows.Add(row);
        }

        HasFirmware = FirmwareRows.Count > 0;
    }

    private static void AddFirmwareRows(List<AboutRowModel> rows, IReadOnlyList<FirmwareComponent> components, string singular)
    {
        for (var index = 0; index < components.Count; index++)
        {
            rows.Add(new AboutRowModel(
                FirmwareComponentDisplay.ComponentName(components[index], singular, index, components.Count),
                components[index].Version,
                linkUrl: null));
        }
    }

    private void ApplyStatus(FrameworkSystemStatus status)
    {
        // Live values only stream while the service is reachable; keep the last known ones otherwise.
        if (!string.IsNullOrWhiteSpace(status.EcBuildInfo))
        {
            AboutRows[1].Value = status.EcBuildInfo!;
        }

        if (!string.IsNullOrWhiteSpace(status.ConnectionLibraryVersion) && status.ConnectionLibraryVersion != "Unknown")
        {
            AboutRows[2].Value = status.ConnectionLibraryVersion;
        }
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(SettingsAboutSectionModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the "+<commit-hash>" build-metadata suffix SourceLink appends.
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return plusIndex > 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string ResolveFrameworkDotnetVersion()
        => typeof(FrameworkDotnet.FrameworkSystem).Assembly.GetName().Version?.ToString() ?? "Unknown";

    private static string ResolveAssemblyMetadata(string key)
    {
        // framework-dotnet does not embed its native component versions yet (recorded as a library
        // follow-up); show an honest placeholder instead of a stale hardcoded number.
        var metadata = typeof(FrameworkDotnet.FrameworkSystem).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(metadata?.Value) ? "Bundled with framework-dotnet" : metadata!.Value!;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
