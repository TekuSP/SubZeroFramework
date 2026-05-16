namespace SubZeroFramework.Models;

public sealed record HardwareInfoBios(
    string? Manufacturer,
    string? Caption,
    string? Description,
    string? Name,
    string? Version,
    string? ReleaseDate,
    string? SerialNumber,
    string? SoftwareElementId);
