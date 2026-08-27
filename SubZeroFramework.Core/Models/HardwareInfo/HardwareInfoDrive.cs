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

}