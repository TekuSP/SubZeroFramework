namespace SubZeroFramework.Models;

/// <summary>
/// One thing in the machine that reports a firmware version.
/// </summary>
/// <param name="SlotIndex">Which slot it occupies, for matching against a physical position.</param>
/// <param name="ProductName">What the device calls itself, or empty where it reports no name.</param>
/// <param name="Version">Major.Minor.SubMinor, already formatted.</param>
/// <param name="VendorId">USB vendor id, for telling two otherwise identical modules apart.</param>
/// <param name="ProductId">USB product id.</param>
public sealed record FirmwareComponent(
    int SlotIndex,
    string ProductName,
    string Version,
    ushort VendorId,
    ushort ProductId)
{
    /// <summary>Formats the three version bytes the peripheral descriptors carry.</summary>
    public static string FormatVersion(byte major, byte minor, byte subMinor) => $"{major}.{minor}.{subMinor}";
}

/// <summary>An NVMe drive's model and firmware, matched to a drive by its device path.</summary>
public sealed record NvmeFirmware(string DevicePath, string ModelNumber, string FirmwareVersion);

/// <summary>
/// Every firmware version the machine will report, collected in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Read on demand rather than polled. None of it changes while the machine runs — a firmware update requires
/// a restart — and each component costs its own round trip.
/// </para>
/// <para>
/// Peripheral versions come from the library's standalone peripherals surface and need NO embedded-controller
/// connection. Power-delivery and retimer versions do. A machine whose EC is unavailable therefore still
/// reports its cameras and hubs, and the collector must not gate the whole snapshot on a connection.
/// </para>
/// </remarks>
public sealed record FirmwareInventorySnapshot
{
    /// <summary>Nothing was collected.</summary>
    public static FirmwareInventorySnapshot Empty { get; } = new();

    public IReadOnlyList<FirmwareComponent> Cameras { get; init; } = [];

    public IReadOnlyList<FirmwareComponent> InputModules { get; init; } = [];

    public IReadOnlyList<FirmwareComponent> UsbHubs { get; init; } = [];

    public IReadOnlyList<FirmwareComponent> AudioCards { get; init; } = [];

    public IReadOnlyList<FirmwareComponent> PowerDeliveryControllers { get; init; } = [];

    /// <summary>The retimer's version, or empty where the machine has none or would not say.</summary>
    public string RetimerVersion { get; init; } = string.Empty;

    public IReadOnlyList<NvmeFirmware> NvmeDrives { get; init; } = [];

    /// <summary>When this was collected.</summary>
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// Whether anything at all was reported.
    /// </summary>
    /// <remarks>
    /// Consumers hide their whole section on false rather than rendering empty headings — a firmware panel
    /// listing nothing reads as a broken feature, where an absent one reads as a machine that does not
    /// report versions.
    /// </remarks>
    public bool HasAny => Cameras.Count > 0
        || InputModules.Count > 0
        || UsbHubs.Count > 0
        || AudioCards.Count > 0
        || PowerDeliveryControllers.Count > 0
        || NvmeDrives.Count > 0
        || RetimerVersion.Length > 0;
}
