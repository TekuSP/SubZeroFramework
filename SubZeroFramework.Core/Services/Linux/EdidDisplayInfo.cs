namespace SubZeroFramework.Services.Linux;

/// <summary>
/// What a monitor says about itself in its EDID block.
/// </summary>
/// <remarks>
/// This is the only description of a display available to a headless service: the kernel exposes each DRM
/// connector's raw EDID at <c>/sys/class/drm/card*-*/edid</c>, with no display server involved. Every field is
/// optional because real panels omit them — a monitor with no name descriptor is normal, not an error.
/// </remarks>
public sealed record EdidDisplayInfo
{
    /// <summary>Three-letter PNP vendor ID packed into bytes 8–9, e.g. "BOE". Empty when unparseable.</summary>
    public required string ManufacturerId { get; init; }

    /// <summary>Vendor-assigned product code (bytes 10–11, little endian). Displayed as four hex digits.</summary>
    public required ushort ProductCode { get; init; }

    /// <summary>Binary serial (bytes 12–15). 0 means the panel does not report one — very common on laptops.</summary>
    public required uint SerialNumber { get; init; }

    /// <summary>Week 1–54, or null when unspecified (byte 16 = 0) or when byte 16 = 0xFF marks a model year.</summary>
    public int? WeekOfManufacture { get; init; }

    /// <summary>Manufacture (or model) year, already offset from the EDID 1990 epoch.</summary>
    public int? YearOfManufacture { get; init; }

    public required int VersionMajor { get; init; }

    public required int VersionMinor { get; init; }

    /// <summary>Physical width in centimetres, 0 when the panel does not report a size.</summary>
    public required int WidthCentimeters { get; init; }

    public required int HeightCentimeters { get; init; }

    /// <summary>Descriptor tag 0xFC — the human name ("NE160QDM-NZ6"). Null when the panel has no name descriptor.</summary>
    public string? MonitorName { get; init; }

    /// <summary>Descriptor tag 0xFF — an ASCII serial, which panels use more often than the binary one.</summary>
    public string? SerialNumberText { get; init; }

    /// <summary>Descriptor tag 0xFE — a free-text string, often a panel/vendor code.</summary>
    public string? DataString { get; init; }

    /// <summary>
    /// The first detailed timing descriptor, which EDID defines as the preferred mode.
    /// </summary>
    /// <remarks>
    /// NOT necessarily the panel's maximum refresh rate: a 165 Hz laptop panel commonly carries a 60 Hz
    /// preferred timing in the base block and declares its high-refresh modes in a CTA/DisplayID extension.
    /// Treat this as "the mode the panel asks for", and prefer the connector's active mode when one is known.
    /// </remarks>
    public EdidDetailedTiming? PreferredTiming { get; init; }

    /// <summary>Number of extension blocks following the base block (byte 126).</summary>
    public required int ExtensionBlockCount { get; init; }

    /// <summary>
    /// False when the base block's bytes do not sum to a multiple of 256. Parsed anyway — the kernel hands us
    /// EDIDs it already accepted, and a stale checksum with otherwise sane fields is common on cheap panels —
    /// but callers may want to distrust the values.
    /// </summary>
    public required bool ChecksumValid { get; init; }

    /// <summary>Best available human name, falling back through the descriptors to the vendor + product code.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MonitorName))
            {
                return MonitorName;
            }

            if (!string.IsNullOrWhiteSpace(DataString))
            {
                return DataString;
            }

            return string.IsNullOrWhiteSpace(ManufacturerId)
                ? "Unknown monitor"
                : $"{ManufacturerId} {ProductCode:X4}";
        }
    }
}

/// <summary>One EDID detailed timing descriptor, reduced to what a user would recognise.</summary>
public sealed record EdidDetailedTiming
{
    public required int HorizontalActive { get; init; }

    public required int VerticalActive { get; init; }

    /// <summary>Pixel clock in hertz (the descriptor stores it in 10 kHz units).</summary>
    public required long PixelClockHz { get; init; }

    /// <summary>Vertical refresh in hertz, computed from the clock and the full blanking-inclusive totals.</summary>
    public required double RefreshHz { get; init; }

    /// <summary>Image size in millimetres as the descriptor reports it; 0 when unspecified.</summary>
    public required int WidthMillimeters { get; init; }

    public required int HeightMillimeters { get; init; }

    public required bool Interlaced { get; init; }
}
