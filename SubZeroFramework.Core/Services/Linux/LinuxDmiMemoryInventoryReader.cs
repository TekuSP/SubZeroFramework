using System.Text;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Default kernel path to the raw SMBIOS structure table. Separate from the reader so the primary constructor
/// can use it as a parameter default, mirroring <c>DrmSysfs</c> and <see cref="BlockSysfs"/>.
/// </summary>
public static class DmiTables
{
    public const string DefaultDmiTablePath = "/sys/firmware/dmi/tables/DMI";
}

/// <summary>
/// Enumerates installed memory modules from the firmware's own SMBIOS table, without Hardware.Info or lshw.
/// </summary>
/// <remarks>
/// This is the Linux answer to Hardware.Info's memory list, which is wrong in two separate ways here. It parses
/// <c>lshw</c> output and keeps the "System Memory" CONTAINER node as though it were a module, so two installed
/// sticks are reported as three entries — the first being their total, with no form factor and no speed. And it
/// leaves manufacturer, part number, serial number, bank label, memory type and data width empty even when run
/// as root.
///
/// SMBIOS type 17 ("Memory Device") carries all of it, one structure per physical slot, with no aggregate node
/// to filter out: capacity, memory type (DDR4/DDR5/LPDDR5), rated and configured speed, data and total width,
/// form factor, manufacturer, part number, serial number, and both locator strings. Empty slots are present too
/// and are identified by a zero size, so they are skipped rather than listed as phantom modules.
///
/// The table is <c>0400 root</c>, which the service already satisfies; an unprivileged caller gets an empty
/// inventory rather than an error, matching what Hardware.Info does unprivileged (it also reports nothing).
///
/// Parsing is defensive throughout, in the same spirit as the EDID parser: every field read is bounds-checked
/// against the structure's own declared length before it is touched, the structure walk stops at the end-of-table
/// marker or the first malformed header rather than running off the buffer, and the string table is scanned
/// within the buffer only. Firmware tables are not adversarial input, but they are frequently wrong.
/// </remarks>
public sealed class LinuxDmiMemoryInventoryReader(
    ILogger<LinuxDmiMemoryInventoryReader> logger,
    string dmiTablePath = DmiTables.DefaultDmiTablePath) : IMemoryInventoryReader
{
    /// <summary>SMBIOS structure type for a Memory Device.</summary>
    private const byte MemoryDeviceType = 17;

    /// <summary>SMBIOS structure type marking the end of the table.</summary>
    private const byte EndOfTableType = 127;

    /// <summary>Every SMBIOS structure begins with type, length, and a two-byte handle.</summary>
    private const int StructureHeaderLength = 4;

    private bool _loggedReadFailure;

    /// <summary>
    /// True when the firmware published a structure table at the configured path.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT an <c>OperatingSystem.IsLinux()</c> check, and deliberately not a readability probe:
    /// this is ordinary file I/O over an injectable path so the parse stays testable off Linux, and the
    /// property is consulted on every snapshot, so it must stay cheap. An unreadable table degrades to an
    /// empty inventory inside <see cref="Read"/>.
    /// </remarks>
    public bool IsAvailable => File.Exists(dmiTablePath);

    public MemoryInventory Read()
    {
        if (!IsAvailable)
        {
            return MemoryInventory.Empty;
        }

        try
        {
            return new MemoryInventory { Modules = ParseMemoryDevices(File.ReadAllBytes(dmiTablePath)) };
        }
        catch (Exception exception)
        {
            // The inventory tier must survive anything firmware does; log once so a persistent problem is
            // visible without a line per refresh. UnauthorizedAccessException is the ordinary unprivileged
            // case and is not worth a warning every time.
            if (!_loggedReadFailure)
            {
                _loggedReadFailure = true;
                logger.LogWarning(exception, "Could not enumerate memory modules from {DmiPath}; the Memory page will be empty.", dmiTablePath);
            }

            return MemoryInventory.Empty;
        }
    }

    private static List<HardwareInfoMemoryModule> ParseMemoryDevices(byte[] table)
    {
        List<HardwareInfoMemoryModule> modules = [];
        var offset = 0;

        while (offset + StructureHeaderLength <= table.Length)
        {
            var structureType = table[offset];
            int structureLength = table[offset + 1];

            // A structure shorter than its own header, or one that claims to extend past the table, means the
            // table is malformed. Stop rather than guess.
            if (structureLength < StructureHeaderLength || offset + structureLength > table.Length)
            {
                break;
            }

            var stringTableStart = offset + structureLength;
            var nextStructure = SkipStringTable(table, stringTableStart);

            if (structureType == EndOfTableType)
            {
                break;
            }

            if (structureType == MemoryDeviceType
                && ParseMemoryDevice(table, offset, structureLength, stringTableStart) is { } module)
            {
                modules.Add(module);
            }

            // A string table that ran to the end of the buffer cannot be followed by another structure.
            if (nextStructure <= offset)
            {
                break;
            }

            offset = nextStructure;
        }

        return modules;
    }

    /// <summary>
    /// Returns the offset just past the double-NUL that terminates a structure's string table.
    /// </summary>
    private static int SkipStringTable(byte[] table, int start)
    {
        var index = start;
        while (index + 1 < table.Length && (table[index] != 0 || table[index + 1] != 0))
        {
            index++;
        }

        return index + 2;
    }

    private static HardwareInfoMemoryModule? ParseMemoryDevice(byte[] table, int offset, int structureLength, int stringTableStart)
    {
        // Every accessor below is bounds-checked against the structure's DECLARED length, not the buffer: an
        // SMBIOS 2.1 table legitimately stops before the fields added in 2.3 and 2.7, and reading past the
        // declared length would return a neighbouring structure's bytes rather than "unknown".
        byte ReadByte(int fieldOffset)
            => fieldOffset < structureLength ? table[offset + fieldOffset] : (byte)0;

        ushort ReadWord(int fieldOffset)
            => fieldOffset + 1 < structureLength
                ? (ushort)(table[offset + fieldOffset] | (table[offset + fieldOffset + 1] << 8))
                : (ushort)0;

        uint ReadDword(int fieldOffset)
            => fieldOffset + 3 < structureLength
                ? (uint)(table[offset + fieldOffset]
                    | (table[offset + fieldOffset + 1] << 8)
                    | (table[offset + fieldOffset + 2] << 16)
                    | (table[offset + fieldOffset + 3] << 24))
                : 0;

        var capacityBytes = ReadCapacityBytes(ReadWord(0x0C), ReadDword(0x1C));

        // A zero size means the slot is present but empty. Listing those is what makes a two-stick machine
        // appear to have four modules; they are not installed memory and are omitted.
        if (capacityBytes == 0)
        {
            return null;
        }

        // Rated speed first, configured speed as the fallback, so a module reports the speed it is specified
        // for (matching what dmidecode and lshw show) rather than the lower rate a mismatched pair runs at.
        var speed = ReadWord(0x15);
        if (speed == 0)
        {
            speed = ReadWord(0x20);
        }

        return new HardwareInfoMemoryModule(
            BankLabel: ReadString(table, stringTableStart, ReadByte(0x11)),
            CapacityBytes: capacityBytes,
            DataWidth: NormalizeWidth(ReadWord(0x0A)),
            MemoryType: DescribeMemoryType(ReadByte(0x12)),
            FormFactor: DescribeFormFactor(ReadByte(0x0E)),
            SpeedMHz: speed,
            MaxVoltage: ReadWord(0x24),
            MinVoltage: ReadWord(0x22),
            Manufacturer: ReadString(table, stringTableStart, ReadByte(0x17)),
            PartNumber: ReadString(table, stringTableStart, ReadByte(0x1A)),
            SerialNumber: ReadString(table, stringTableStart, ReadByte(0x18)));
    }

    /// <summary>
    /// Decodes the type 17 size field, including the 2.7 extended form used above 32 GB per module.
    /// </summary>
    internal static ulong ReadCapacityBytes(ushort sizeField, uint extendedSize)
    {
        const ushort NotInstalled = 0;
        const ushort UnknownSize = 0xFFFF;
        const ushort UseExtendedSize = 0x7FFF;

        if (sizeField is NotInstalled or UnknownSize)
        {
            return 0;
        }

        if (sizeField == UseExtendedSize)
        {
            // Bit 31 is reserved; the remainder is a megabyte count.
            return (ulong)(extendedSize & 0x7FFFFFFF) * 1024UL * 1024UL;
        }

        // Bit 15 selects the unit: set means kilobytes, clear means megabytes.
        var magnitude = (ulong)(sizeField & 0x7FFF);
        return (sizeField & 0x8000) != 0
            ? magnitude * 1024UL
            : magnitude * 1024UL * 1024UL;
    }

    /// <summary>0xFFFF is SMBIOS for "unknown"; the model represents that as zero.</summary>
    private static uint NormalizeWidth(ushort width) => width == 0xFFFF ? 0u : width;

    /// <summary>
    /// Reads a 1-based string-table entry. Index 0 means the field was not populated.
    /// </summary>
    internal static string? ReadString(byte[] table, int stringTableStart, byte index)
    {
        if (index == 0 || stringTableStart >= table.Length)
        {
            return null;
        }

        var position = stringTableStart;
        var current = 1;

        while (position < table.Length)
        {
            var start = position;
            while (position < table.Length && table[position] != 0)
            {
                position++;
            }

            // An empty entry is the table terminator, so the requested index does not exist.
            if (position == start)
            {
                return null;
            }

            if (current == index)
            {
                // SMBIOS strings are 7-bit ASCII in practice, but Latin-1 decodes any byte without throwing
                // and firmware does occasionally pad with high bytes.
                var value = Encoding.Latin1.GetString(table, start, position - start).Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }

            current++;
            position++;
        }

        return null;
    }

    /// <summary>SMBIOS 7.18.2 Memory Device — Form Factor.</summary>
    private static string? DescribeFormFactor(byte formFactor) => formFactor switch
    {
        0x03 => "SIMM",
        0x04 => "SIP",
        0x05 => "Chip",
        0x06 => "DIP",
        0x07 => "ZIP",
        0x08 => "Proprietary Card",
        0x09 => "DIMM",
        0x0A => "TSOP",
        0x0B => "Row of chips",
        0x0C => "RIMM",
        0x0D => "SODIMM",
        0x0E => "SRIMM",
        0x0F => "FB-DIMM",
        0x10 => "Die",
        // 0x01 Other and 0x02 Unknown carry no more information than saying nothing does.
        _ => null,
    };

    /// <summary>SMBIOS 7.18.3 Memory Device — Memory Type.</summary>
    private static string? DescribeMemoryType(byte memoryType) => memoryType switch
    {
        0x03 => "DRAM",
        0x0F => "SDRAM",
        0x11 => "SDRAM",
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x19 => "FBD2",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x1F => "Logical non-volatile device",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x24 => "HBM3",
        _ => null,
    };
}
