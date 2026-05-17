using System.Globalization;

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
    string? VideoProcessor)
{
    public string DisplayAdapterRam => AdapterRAM == 0
        ? "Unknown"
        : FormatBytes(AdapterRAM);

    public string DisplayDriverDate
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DriverDate))
            {
                return "Unknown";
            }

            if (DriverDate.Length >= 8
                && DriverDate[..8].All(char.IsDigit)
                && DateTime.TryParseExact(DriverDate[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compactDate))
            {
                return compactDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return DateTime.TryParse(DriverDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                ? parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DriverDate;
        }
    }

    public string DisplayResolution
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
                ? CurrentHorizontalResolution.ToString("N0", CultureInfo.InvariantCulture)
                : CurrentVerticalResolution.ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    public string DisplayRefreshRate => CurrentRefreshRate > 0
        ? $"{CurrentRefreshRate:N0} Hz"
        : "Unknown";

    private static string FormatBytes(ulong bytes)
    {
        const double OneGigabyte = 1024d * 1024d * 1024d;
        return bytes >= OneGigabyte
            ? $"{bytes / OneGigabyte:0.##} GB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }
}
