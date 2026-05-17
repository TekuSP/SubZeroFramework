namespace SubZeroFramework.Models;

public sealed record HardwareInfoMemoryModule(
    string? BankLabel,
    ulong CapacityBytes,
    uint DataWidth,
    string? MemoryType,
    string? FormFactor,
    uint SpeedMHz,
    uint MaxVoltage,
    uint MinVoltage,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber)
{
    public string DisplayCapacity => CapacityBytes == 0
        ? "Unknown"
        : FormatBytes(CapacityBytes);

    public string DisplaySpeed => SpeedMHz > 0
        ? $"{SpeedMHz:N0} MHz"
        : "Unknown";

    public string DisplayDataWidth => DataWidth > 0
        ? $"{DataWidth}-bit"
        : "Unknown";

    private static string FormatBytes(ulong bytes)
    {
        const double OneGigabyte = 1024d * 1024d * 1024d;
        return bytes >= OneGigabyte
            ? $"{bytes / OneGigabyte:0.##} GB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }
}
