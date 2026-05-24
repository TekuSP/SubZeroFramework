using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesNetworkAdapterCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesNetworkAdapterCardModel(HardwareInfoNetworkAdapter snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
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

    public string SpeedDisplay => Snapshot.HasKnownSpeed
        ? _unitFormattingService.FormatBitRateBitsPerSecond(Snapshot.Speed)
        : "Unknown";

    public string MacAddressDisplay => FirstNonEmpty(Snapshot.MacAddress) ?? "Unknown";

    public string IpAddressesDisplay => Snapshot.DisplayIpAddresses;

    public string DefaultGatewaysDisplay => Snapshot.DisplayDefaultGateways;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedDisplay))]
    private partial int UnitFormattingRevision { get; set; }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
