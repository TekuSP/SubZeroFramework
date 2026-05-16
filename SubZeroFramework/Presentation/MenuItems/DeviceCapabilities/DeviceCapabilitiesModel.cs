using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;
using SubZeroFramework.Services;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

public partial class DeviceCapabilitiesModel : ObservableObject, IDisposable
{
    private readonly IHardwareInfoClient _hardwareInfoClient;
    private readonly SynchronizationContext _synchronizationContext;
    private IDisposable? _hardwareInfoSubscription;

    [ObservableProperty]
    public partial HardwareInfoSnapshot? Snapshot { get; set; }

    public DeviceCapabilitiesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IHardwareInfoClient hardwareInfoClient,
        SynchronizationContext synchronizationContext)
    {
        _hardwareInfoClient = hardwareInfoClient;
        _synchronizationContext = synchronizationContext;

        _hardwareInfoSubscription = _hardwareInfoClient
            .WatchHardwareInfo()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateSnapshot);
    }

    private void UpdateSnapshot(HardwareInfoSnapshot snapshot) => Snapshot = snapshot;

    public string FormatBytes(ulong bytes)
    {
        const double OneGigabyte = 1024d * 1024d * 1024d;
        if (bytes == 0)
        {
            return "0 GB";
        }

        return bytes >= OneGigabyte
            ? $"{bytes / OneGigabyte:0.##} GB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }

    public void Dispose()
    {
        _hardwareInfoSubscription?.Dispose();
    }
}
