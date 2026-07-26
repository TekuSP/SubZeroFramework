using System.Collections.ObjectModel;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>
/// ViewModel for the logs section: shows what the background service AND this app have logged since they
/// started, interleaved into one list. Navigation constructs it (ViewMap-registered).
/// </summary>
/// <remarks>
/// A snapshot on demand, NOT a live tail. The service logs several times a second while polling; streaming that
/// into a list view would spend the UI thread redrawing instead of showing the user the line they are looking
/// for. Refresh is one click, and the point of the page is usually "copy this into a bug report".
///
/// Both sides are shown because they fail independently: the service can lose the EC while the app is fine,
/// and the app can lose the service connection while the service is healthy. Reading only one half hides
/// exactly the case where the two disagree.
/// </remarks>
public partial class SettingsLogsSectionModel : ObservableObject
{
    private readonly IFrameworkServiceConfigurationClient _serviceConfigurationClient;
    private readonly InMemoryLogBuffer _appLogBuffer;
    private readonly DispatcherQueue _dispatcherQueue;

    public SettingsLogsSectionModel(
        IFrameworkServiceConfigurationClient serviceConfigurationClient,
        InMemoryLogBuffer appLogBuffer,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(serviceConfigurationClient);
        ArgumentNullException.ThrowIfNull(appLogBuffer);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _serviceConfigurationClient = serviceConfigurationClient;
        _appLogBuffer = appLogBuffer;
        _dispatcherQueue = dispatcherQueue;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CopyAllCommand = new RelayCommand(CopyAll, () => Entries.Count > 0);

        _ = LoadAsync();
    }

    public ObservableCollection<ServiceLogEntryModel> Entries { get; } = [];

    /// <summary>Level filter, in the order shown by the segmented control.</summary>
    public IReadOnlyList<string> LevelOptions { get; } = ["All", "Info", "Warning", "Error"];

    /// <summary>Source filter, in the order shown by the segmented control.</summary>
    public IReadOnlyList<string> SourceOptions { get; } = ["Both", "Service", "App"];

    [ObservableProperty]
    public partial int SelectedLevelIndex { get; set; } = 1;

    /// <summary>Defaults to Both — that is what belongs in a bug report.</summary>
    [ObservableProperty]
    public partial int SelectedSourceIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageVisibility))]
    public partial string StatusMessage { get; set; } = "Loading service logs…";

    public Visibility StatusMessageVisibility => string.IsNullOrEmpty(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Shown when the service's buffer has evicted older entries, so the list is not the whole history.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TruncationNoticeVisibility))]
    public partial string TruncationNotice { get; set; } = string.Empty;

    public Visibility TruncationNoticeVisibility => string.IsNullOrEmpty(TruncationNotice) ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand CopyAllCommand { get; }

    partial void OnSelectedLevelIndexChanged(int value) => _ = LoadAsync();

    partial void OnSelectedSourceIndexChanged(int value) => _ = LoadAsync();

    // Index into LevelOptions -> the lowest level the service should return.
    private LogLevel MinimumLevel => SelectedLevelIndex switch
    {
        1 => LogLevel.Information,
        2 => LogLevel.Warning,
        3 => LogLevel.Error,
        _ => LogLevel.Trace,
    };

    private bool IncludesService => SelectedSourceIndex != 2;

    private bool IncludesApp => SelectedSourceIndex != 1;

    private async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            List<ServiceLogEntryModel> merged = [];
            string? serviceError = null;
            var truncationNotices = new List<string>();

            if (IncludesService)
            {
                // The service half is the only part that can fail — it is a gRPC call to another process.
                // A dead service must still leave the app's own entries readable, since those are exactly
                // what explain why it looks dead, so this failure is reported without discarding the rest.
                try
                {
                    var snapshot = await _serviceConfigurationClient.GetServiceLogsAsync(MinimumLevel, CancellationToken.None);
                    foreach (var entry in snapshot.Entries)
                    {
                        merged.Add(new ServiceLogEntryModel(entry, ServiceLogEntrySource.Service));
                    }

                    if (snapshot.IsTruncated)
                    {
                        truncationNotices.Add($"the service keeps the last {snapshot.BufferCapacity:N0} and has dropped {snapshot.DroppedCount:N0} older one(s)");
                    }
                }
                catch (Exception exception)
                {
                    serviceError = exception.Message;
                }
            }

            if (IncludesApp)
            {
                var (appEntries, appDropped) = _appLogBuffer.Snapshot();

                // The buffer holds whatever the app's configured filters let through, so the level filter is
                // applied here rather than at capture time — the service applies the same filter its side.
                foreach (var entry in appEntries.Where(entry => entry.Level >= MinimumLevel))
                {
                    merged.Add(new ServiceLogEntryModel(entry, ServiceLogEntrySource.App));
                }

                if (appDropped > 0)
                {
                    truncationNotices.Add($"the app keeps the last {InMemoryLogBuffer.Capacity:N0} and has dropped {appDropped:N0} older one(s)");
                }
            }

            // Interleave by time so cause and effect read in order across the process boundary — a client
            // reconnect warning next to the service restart that caused it is the whole point of merging.
            merged.Sort(static (left, right) => left.ObservedAt.CompareTo(right.ObservedAt));

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                Entries.Clear();
                foreach (var entry in merged)
                {
                    Entries.Add(entry);
                }

                StatusMessage = serviceError is not null
                    ? $"Could not read the service logs: {serviceError}"
                    : Entries.Count == 0
                        ? "Nothing has been logged at this level since startup."
                        : string.Empty;

                // Say plainly that this is the most recent slice rather than everything since start.
                TruncationNotice = truncationNotices.Count == 0
                    ? string.Empty
                    : $"Showing the most recent entries — {string.Join("; ", truncationNotices)}.";

                CopyAllCommand.NotifyCanExecuteChanged();
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    // The usual reason to open this page is to paste the log into a bug report, so make that one click.
    private void CopyAll()
    {
        StringBuilder builder = new();
        foreach (var entry in Entries)
        {
            builder.AppendLine(entry.ToClipboardLine());
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(builder.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

        StatusMessage = $"Copied {Entries.Count:N0} log line(s) to the clipboard.";
    }
}
