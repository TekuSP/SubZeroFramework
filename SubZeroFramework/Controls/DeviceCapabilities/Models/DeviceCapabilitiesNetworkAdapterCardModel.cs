using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using Microsoft.UI.Xaml.Media;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

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
        nameof(SpeedBrush),
        nameof(MacAddressDisplay),
        nameof(IpAddressesDisplay),
        nameof(DefaultGatewaysDisplay))]
    public partial HardwareInfoNetworkAdapter Snapshot { get; set; } = default!;

    public string Title => FirstNonEmpty(Snapshot.NetConnectionId, Snapshot.Name, Snapshot.ProductName, Snapshot.Caption)
        ?? "Network Adapter";

    public string ProductDisplay => FirstNonEmpty(Snapshot.ProductName, Snapshot.Description, Snapshot.Caption, Snapshot.Name)
        ?? "Unknown";

    public string AdapterTypeDisplay => FirstNonEmpty(Snapshot.AdapterType) ?? "Unknown";

    /// <summary>Friendly connection type per the redesign ("Wi-Fi 7" / "2.5 GbE" / "Bluetooth PAN" — never "802.11").</summary>
    public string FriendlyTypeDisplay
    {
        get
        {
            var text = $"{Title} {ProductDisplay} {AdapterTypeDisplay}".ToLowerInvariant();

            if (text.Contains("bluetooth", StringComparison.Ordinal))
            {
                return "Bluetooth PAN";
            }

            if (IsTunnelText(text))
            {
                return "VPN";
            }

            if (text.Contains("wi-fi", StringComparison.Ordinal) || text.Contains("wifi", StringComparison.Ordinal)
                || text.Contains("wireless", StringComparison.Ordinal) || text.Contains("802.11", StringComparison.Ordinal))
            {
                if (text.Contains("wi-fi 7", StringComparison.Ordinal) || text.Contains("be200", StringComparison.Ordinal) || text.Contains("802.11be", StringComparison.Ordinal))
                {
                    return "Wi-Fi 7";
                }

                if (text.Contains("6e", StringComparison.Ordinal))
                {
                    return "Wi-Fi 6E";
                }

                return text.Contains("wi-fi 6", StringComparison.Ordinal) || text.Contains("ax2", StringComparison.Ordinal) || text.Contains("802.11ax", StringComparison.Ordinal)
                    ? "Wi-Fi 6"
                    : "Wi-Fi";
            }

            // Wired: prefer the reported link speed, fall back to speed hints in the product name.
            if (Snapshot.HasKnownSpeed)
            {
                return Snapshot.Speed switch
                {
                    >= 10_000_000_000 => "10 GbE",
                    >= 5_000_000_000 => "5 GbE",
                    >= 2_500_000_000 => "2.5 GbE",
                    >= 1_000_000_000 => "GbE",
                    >= 100_000_000 => "FE",
                    _ => "Ethernet",
                };
            }

            if (text.Contains("10g", StringComparison.Ordinal))
            {
                return "10 GbE";
            }

            if (text.Contains("2.5g", StringComparison.Ordinal))
            {
                return "2.5 GbE";
            }

            return text.Contains("gigabit", StringComparison.Ordinal) ? "GbE" : "Ethernet";
        }
    }

    /// <summary>Combined vendor-bearing text for the brand-logo resolver (MediaTek / Intel / Microsoft…).</summary>
    public string LogoVendor => $"{ProductDisplay} {ManufacturerDisplay}";

    /// <summary>Fallback glyph when no brand logo is shipped (e.g. Realtek → the type glyph).</summary>
    public MaterialIconKind TypeIconKind
    {
        get
        {
            var friendly = FriendlyTypeDisplay;
            if (friendly.StartsWith("Wi-Fi", StringComparison.Ordinal))
            {
                return MaterialIconKind.Wifi;
            }

            return friendly switch
            {
                "Bluetooth PAN" => MaterialIconKind.Bluetooth,
                "VPN" => MaterialIconKind.Vpn,
                _ => MaterialIconKind.Ethernet,
            };
        }
    }

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string SpeedDisplay => Snapshot.HasKnownSpeed
        ? _unitFormattingService.FormatBitRateBitsPerSecond(Snapshot.Speed)
        : "Unknown";

    /// <summary>Mockup state colour: green when the link reports a speed.</summary>
    public Brush SpeedBrush => Snapshot.HasKnownSpeed
        ? AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusWarningColor);

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

    /// <summary>True for VPN/TAP/tunnel adapters, whose virtual link speed shouldn't win "Fastest link".</summary>
    public static bool IsTunnelAdapter(HardwareInfoNetworkAdapter snapshot)
        => IsTunnelText($"{snapshot.NetConnectionId} {snapshot.Name} {snapshot.ProductName} {snapshot.Description} {snapshot.Caption} {snapshot.AdapterType}".ToLowerInvariant());

    private static bool IsTunnelText(string lowerText)
        => lowerText.Contains("vpn", StringComparison.Ordinal)
            || lowerText.Contains("tap-", StringComparison.Ordinal)
            || lowerText.Contains("tunnel", StringComparison.Ordinal);

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
