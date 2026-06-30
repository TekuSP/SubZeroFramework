using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

public partial class PowerTelemetryModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly SynchronizationContext _synchronizationContext;

    public PowerTelemetryModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IPowerDeliveryClient powerDeliveryClient,
        IUnitFormattingService unitFormattingService,
        SynchronizationContext synchronizationContext)
    {
        ArgumentNullException.ThrowIfNull(powerDeliveryClient);
        _unitFormattingService = unitFormattingService;
        _synchronizationContext = synchronizationContext;

        _subscriptions.Add(powerDeliveryClient.WatchPorts()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdatePorts));
    }

    /// <summary>The reported USB-C Power Delivery ports, kept in sync (in place) with the live stream.</summary>
    public ObservableCollection<PowerDeliveryPortViewModel> Ports { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPorts))]
    [NotifyPropertyChangedFor(nameof(PortsVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial int PortsRevision { get; private set; }

    public bool HasPorts => Ports.Count > 0;

    public Microsoft.UI.Xaml.Visibility PortsVisibility =>
        HasPorts ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility EmptyVisibility =>
        HasPorts ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    private void UpdatePorts(IReadOnlyList<PowerDeliveryPortStatus> ports)
    {
        var bySlot = Ports.ToDictionary(static vm => vm.SlotIndex);
        foreach (var status in ports.OrderBy(static p => p.SlotIndex))
        {
            if (bySlot.TryGetValue(status.SlotIndex, out var existing))
            {
                existing.Update(status);
            }
            else
            {
                Ports.Add(new PowerDeliveryPortViewModel(_unitFormattingService, status));
            }
        }

        var keep = ports.Select(static p => p.SlotIndex).ToHashSet();
        for (var index = Ports.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(Ports[index].SlotIndex))
            {
                Ports.RemoveAt(index);
            }
        }

        PortsRevision++;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
