using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesNetworkAdapterCardModel : ObservableObject
{
    public DeviceCapabilitiesNetworkAdapterCardModel(HardwareInfoNetworkAdapter snapshot)
    {
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(Title),
        nameof(ProductDisplay),
        nameof(AdapterTypeDisplay),
        nameof(ManufacturerDisplay),
        nameof(SpeedDisplay),
        nameof(MacAddressDisplay),
        nameof(IpAddressesDisplay),
        nameof(DefaultGatewaysDisplay))]
    public partial HardwareInfoNetworkAdapter Snapshot { get; set; } = default!;

    public string Title => FirstNonEmpty(Snapshot.NetConnectionId, Snapshot.Name, Snapshot.ProductName, Snapshot.Caption)
        ?? "Network Adapter";

    public string ProductDisplay => FirstNonEmpty(Snapshot.ProductName, Snapshot.Description, Snapshot.Caption, Snapshot.Name)
        ?? "Unknown";

    public string AdapterTypeDisplay => FirstNonEmpty(Snapshot.AdapterType) ?? "Unknown";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string SpeedDisplay => Snapshot.DisplaySpeed;

    public string MacAddressDisplay => FirstNonEmpty(Snapshot.MacAddress) ?? "Unknown";

    public string IpAddressesDisplay => Snapshot.DisplayIpAddresses;

    public string DefaultGatewaysDisplay => Snapshot.DisplayDefaultGateways;

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
