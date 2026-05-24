using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoNetworkAdapter(
    string? Name,
    string? NetConnectionId,
    string? ProductName,
    string? Caption,
    string? Description,
    string? Manufacturer,
    string? AdapterType,
    string? MacAddress,
    ulong Speed,
    ImmutableArray<string> IpAddresses,
    ImmutableArray<string> DefaultGateways)
{
    private const ulong UnknownSpeedSentinel = long.MaxValue;

    public bool HasAssignedAddress => !IpAddresses.IsDefaultOrEmpty;

    public bool HasKnownSpeed => Speed is > 0 and < UnknownSpeedSentinel;

    public string DisplaySpeed => !HasKnownSpeed
        ? "Unknown"
        : Speed >= 1_000_000_000UL
            ? $"{Speed / 1_000_000_000d:0.##} Gbps"
            : Speed >= 1_000_000UL
                ? $"{Speed / 1_000_000d:0.##} Mbps"
                : $"{Speed / 1_000d:0.##} Kbps";

    public string DisplayIpAddresses => IpAddresses.IsDefaultOrEmpty
        ? "None"
        : string.Join(", ", IpAddresses);

    public string DisplayDefaultGateways => DefaultGateways.IsDefaultOrEmpty
        ? "None"
        : string.Join(", ", DefaultGateways);
}