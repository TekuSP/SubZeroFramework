namespace SubZeroFramework.Models;

public sealed record HardwareInfoComputerSystem(
    string? Vendor,
    string? Caption,
    string? Description,
    string? Name,
    string? Skunumber,
    string? Uuid,
    string? Version);
