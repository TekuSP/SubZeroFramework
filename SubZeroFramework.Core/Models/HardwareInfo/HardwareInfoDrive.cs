namespace SubZeroFramework.Models;

public sealed record HardwareInfoDrive(
    uint Index,
    string? Name,
    string? Model,
    string? Caption,
    string? Description,
    string? Manufacturer,
    string? MediaType,
    string? SerialNumber,
    string? FirmwareRevision,
    ulong Size,
    ulong FreeSpace)
{
    public ulong ClampedFreeSpace => Math.Min(FreeSpace, Size);

    public ulong UsedSpace => Size > ClampedFreeSpace
        ? Size - ClampedFreeSpace
        : 0;

    public double UsagePercent => Size == 0
        ? 0d
        : Math.Clamp(UsedSpace * 100d / Size, 0d, 100d);

    public string DisplaySize => FormatBytes(Size, treatZeroAsUnknown: true);

    public string DisplayFreeSpace => Size == 0
        ? "Unknown"
        : FormatBytes(ClampedFreeSpace, treatZeroAsUnknown: false);

    public string DisplayUsedSpace => Size == 0
        ? "Unknown"
        : FormatBytes(UsedSpace, treatZeroAsUnknown: false);

    private static string FormatBytes(ulong bytes, bool treatZeroAsUnknown)
    {
        if (bytes == 0)
        {
            return treatZeroAsUnknown ? "Unknown" : "0 GB";
        }

        return bytes >= 1024UL * 1024UL * 1024UL * 1024UL
            ? $"{bytes / (1024d * 1024d * 1024d * 1024d):0.##} TB"
            : bytes >= 1024UL * 1024UL * 1024UL
                ? $"{bytes / (1024d * 1024d * 1024d):0.##} GB"
                : $"{bytes / (1024d * 1024d):0.##} MB";
    }
}