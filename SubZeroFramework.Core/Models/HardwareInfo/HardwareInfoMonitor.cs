namespace SubZeroFramework.Models;

public sealed record HardwareInfoMonitor(
    bool Active,
    string? Caption,
    string? Description,
    string? ManufacturerName,
    string? MonitorManufacturer,
    string? MonitorType,
    string? Name,
    uint PixelsPerXLogicalInch,
    uint PixelsPerYLogicalInch,
    string? ProductCodeId,
    string? SerialNumberId,
    string? UserFriendlyName,
    ushort WeekOfManufacture,
    ushort YearOfManufacture)
{
    public string DisplayName => FirstNonEmpty(UserFriendlyName, Name, Caption, Description) ?? "Unknown monitor";

    public string DisplayManufacturer => FirstNonEmpty(ManufacturerName, MonitorManufacturer) ?? "Unknown";

    public string DisplayStatus => Active ? "Active" : "Inactive";

    public string DisplayLogicalDensity
    {
        get
        {
            if (PixelsPerXLogicalInch == 0 && PixelsPerYLogicalInch == 0)
            {
                return "Unknown";
            }

            if (PixelsPerXLogicalInch > 0 && PixelsPerYLogicalInch > 0)
            {
                return PixelsPerXLogicalInch == PixelsPerYLogicalInch
                    ? $"{PixelsPerXLogicalInch:N0} DPI"
                    : $"{PixelsPerXLogicalInch:N0} x {PixelsPerYLogicalInch:N0} DPI";
            }

            var density = PixelsPerXLogicalInch > 0 ? PixelsPerXLogicalInch : PixelsPerYLogicalInch;
            return $"{density:N0} DPI";
        }
    }

    public string DisplayManufactured => YearOfManufacture == 0
        ? "Unknown"
        : WeekOfManufacture > 0
            ? $"{YearOfManufacture}-W{WeekOfManufacture:D2}"
            : $"{YearOfManufacture}";

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}