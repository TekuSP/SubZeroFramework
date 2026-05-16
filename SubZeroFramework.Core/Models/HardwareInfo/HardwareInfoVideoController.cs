namespace SubZeroFramework.Models;

public sealed record HardwareInfoVideoController(
    ulong AdapterRAM,
    string? Caption,
    uint CurrentBitsPerPixel,
    uint CurrentHorizontalResolution,
    ulong CurrentNumberOfColors,
    uint CurrentRefreshRate,
    uint CurrentVerticalResolution,
    string? Description,
    string? DriverDate,
    string? DriverVersion,
    string? Manufacturer,
    uint MaxRefreshRate,
    uint MinRefreshRate,
    string? Name,
    string? VideoModeDescription,
    string? VideoProcessor);
