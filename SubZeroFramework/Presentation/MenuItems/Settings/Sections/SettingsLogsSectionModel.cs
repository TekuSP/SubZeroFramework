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
/// ViewModel for the Service logs section: fetches what the background service has logged since it started and
/// presents it as a scrollable list. Navigation constructs it (ViewMap-registered).
/// </summary>
/// <remarks>
/// A snapshot on demand, NOT a live tail. The service logs several times a second while polling; streaming that
/// into a list view would spend the UI thread redrawing instead of showing the user the line they are looking
/// for. Refresh is one click, and the point of the page is usually "copy this into a bug report".
/// </remarks>
public partial class SettingsLogsSectionModel : ObservableObject
{
    private readonly IFrameworkServiceConfigurationClient _serviceConfigurationClient;
    private readonly DispatcherQueue _dispatcherQueue;

    public SettingsLogsSectionModel(
        IFrameworkServiceConfigurationClient serviceConfigurationClient,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(serviceConfigurationClient);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _serviceConfigurationClient = serviceConfigurationClient;
        _dispatcherQueue = dispatcherQueue;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CopyAllCommand = new RelayCommand(CopyAll, () => Entries.Count > 0);

        _ = LoadAsync();
    }

    public ObservableCollection<ServiceLogEntryModel> Entries { get; } = [];

    /// <summary>Level filter, in the order shown by the segmented control.</summary>
    public IReadOnlyList<string> LevelOptions { get; } = ["All", "Info", "Warning", "Error"];

    [ObservableProperty]
    public partial int SelectedLevelIndex { get; set; } = 1;

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

    // Index into LevelOptions -> the lowest level the service should return.
    private LogLevel MinimumLevel => SelectedLevelIndex switch
    {
        1 => LogLevel.Information,
        2 => LogLevel.Warning,
        3 => LogLevel.Error,
        _ => LogLevel.Trace,
    };

    private async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var snapshot = await _serviceConfigurationClient.GetServiceLogsAsync(MinimumLevel, CancellationToken.None);

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                Entries.Clear();
                foreach (var entry in snapshot.Entries)
                {
                    Entries.Add(new ServiceLogEntryModel(entry));
                }

                StatusMessage = Entries.Count == 0
                    ? "The service has not logged anything at this level since it started."
                    : string.Empty;

                // Say plainly that this is the most recent slice rather than everything since start.
                TruncationNotice = snapshot.IsTruncated
                    ? $"Showing the most recent entries — the service keeps the last {snapshot.BufferCapacity:N0} and has dropped {snapshot.DroppedCount:N0} older one(s)."
                    : string.Empty;

                CopyAllCommand.NotifyCanExecuteChanged();
            });
        }
        catch (Exception exception)
        {
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                Entries.Clear();
                CopyAllCommand.NotifyCanExecuteChanged();
                TruncationNotice = string.Empty;
                StatusMessage = $"Could not read the service logs: {exception.Message}";
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
