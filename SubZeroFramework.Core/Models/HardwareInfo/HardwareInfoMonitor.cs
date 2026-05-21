using System.Collections.Immutable;

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
    ushort YearOfManufacture,
    uint CurrentHorizontalResolution,
    uint CurrentVerticalResolution,
    uint CurrentRefreshRate,
    ImmutableArray<string> LinkedVideoControllerDisplayNames)
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

    public string DisplayCurrentResolution
    {
        get
        {
            if (CurrentHorizontalResolution == 0 && CurrentVerticalResolution == 0)
            {
                return "Unknown";
            }

            if (CurrentHorizontalResolution > 0 && CurrentVerticalResolution > 0)
            {
                return $"{CurrentHorizontalResolution:N0} x {CurrentVerticalResolution:N0}";
            }

            return CurrentHorizontalResolution > 0
                ? CurrentHorizontalResolution.ToString("N0")
                : CurrentVerticalResolution.ToString("N0");
        }
    }

    public string DisplayCurrentRefreshRate => CurrentRefreshRate > 0
        ? $"{CurrentRefreshRate:N0} Hz"
        : "Unknown";

    public string DisplayLinkedVideoControllerSummary => LinkedVideoControllerDisplayNames.Length == 0
        ? "No linked graphics adapter reported"
        : string.Join(", ", LinkedVideoControllerDisplayNames);

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}