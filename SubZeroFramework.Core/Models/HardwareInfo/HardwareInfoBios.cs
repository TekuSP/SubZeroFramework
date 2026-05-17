using System.Globalization;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoBios(
    string? Manufacturer,
    string? Caption,
    string? Description,
    string? Name,
    string? Version,
    string? ReleaseDate,
    string? SerialNumber,
    string? SoftwareElementId)
{
    public string DisplayReleaseDate
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReleaseDate))
            {
                return "Unknown";
            }

            if (ReleaseDate.Length >= 8
                && ReleaseDate[..8].All(char.IsDigit)
                && DateTime.TryParseExact(ReleaseDate[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compactDate))
            {
                return compactDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return DateTime.TryParse(ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                ? parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : ReleaseDate;
        }
    }
}
