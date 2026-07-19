using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Controls.Settings.Models;
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

    public SettingsAboutSectionModel(IFrameworkStatusClient frameworkStatusClient, DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(frameworkStatusClient);
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
            .Select(status => Observable.FromAsync(_ => dispatcherQueue.EnqueueAsync(() => ApplyStatus(status))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    public IReadOnlyList<AboutRowModel> AboutRows { get; }

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
