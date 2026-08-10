using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the SMBIOS type 17 parse against synthetic structure tables.
/// </summary>
/// <remarks>
/// The reader takes its table path as a constructor argument so the parse can be driven from a fixture: SMBIOS
/// is a published, stable binary layout, so a hand-built table reproduces what firmware emits and covers what
/// would otherwise need a pile of different machines — a populated slot beside an empty one, the pre-2.7 size
/// encoding beside the extended one, a truncated 2.1-era structure, and a table with no memory devices at all.
///
/// The empty-slot case is the one that motivated this reader: firmware publishes a structure for every socket
/// whether or not it holds a module, and Hardware.Info compounded that by also reporting lshw's container node,
/// so a two-stick machine listed three modules.
/// </remarks>
[TestFixture]
public class LinuxDmiMemoryInventoryReaderTests
{
    private string _tablePath = string.Empty;

    [SetUp]
    public void SetUp()
        => _tablePath = Path.Combine(Path.GetTempPath(), "szf-dmi-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(_tablePath))
            {
                File.Delete(_tablePath);
            }
        }
        catch (IOException)
        {
        }
    }

    private LinuxDmiMemoryInventoryReader CreateReader()
        => new(NullLogger<LinuxDmiMemoryInventoryReader>.Instance, _tablePath);

    /// <summary>
    /// Builds one SMBIOS type 17 structure. Field offsets follow SMBIOS 7.18; the structure is emitted at the
    /// full 2.8 length so every field the reader knows about is present.
    /// </summary>
    private static byte[] MemoryDevice(
        ushort sizeField,
        uint extendedSize = 0,
        ushort speed = 3200,
        byte memoryType = 0x22,
        byte formFactor = 0x0D,
        ushort dataWidth = 64,
        string? deviceLocator = "Controller0-ChannelA",
        string? bankLocator = "BANK 0",
        string? manufacturer = "Crucial",
        string? serial = "E7DF1A22",
        string? partNumber = "CT32G56C46S5.M8G1")
    {
        const int StructureLength = 0x28;
        var body = new byte[StructureLength];
        body[0] = 17;                       // type
        body[1] = StructureLength;          // length
        body[2] = 0x10;                     // handle low
        body[3] = 0x00;                     // handle high

        void WriteWord(int offset, ushort value)
        {
            body[offset] = (byte)(value & 0xFF);
            body[offset + 1] = (byte)(value >> 8);
        }

        WriteWord(0x08, 64);                // total width
        WriteWord(0x0A, dataWidth);
        WriteWord(0x0C, sizeField);
        body[0x0E] = formFactor;
        body[0x10] = 1;                     // device locator -> string 1
        body[0x11] = 2;                     // bank locator   -> string 2
        body[0x12] = memoryType;
        WriteWord(0x15, speed);
        body[0x17] = 3;                     // manufacturer   -> string 3
        body[0x18] = 4;                     // serial         -> string 4
        body[0x1A] = 5;                     // part number    -> string 5
        body[0x1C] = (byte)(extendedSize & 0xFF);
        body[0x1D] = (byte)((extendedSize >> 8) & 0xFF);
        body[0x1E] = (byte)((extendedSize >> 16) & 0xFF);
        body[0x1F] = (byte)((extendedSize >> 24) & 0xFF);
        WriteWord(0x22, 1100);              // min voltage (mV)
        WriteWord(0x24, 1100);              // max voltage (mV)

        return [.. body, .. StringTable(deviceLocator, bankLocator, manufacturer, serial, partNumber)];
    }

    /// <summary>A structure's trailing string table: NUL-separated entries closed by a double NUL.</summary>
    private static byte[] StringTable(params string?[] values)
    {
        List<byte> bytes = [];
        foreach (var value in values)
        {
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(value ?? string.Empty));
            bytes.Add(0);
        }

        // A structure with no strings is still terminated by two NULs.
        if (values.Length == 0)
        {
            bytes.Add(0);
        }

        bytes.Add(0);
        return [.. bytes];
    }

    /// <summary>The end-of-table marker every real table closes with.</summary>
    private static byte[] EndOfTable() => [127, 4, 0x20, 0x00, 0x00, 0x00];

    private void WriteTable(params byte[][] structures)
        => File.WriteAllBytes(_tablePath, structures.SelectMany(static structure => structure).ToArray());

    [Test]
    public void Read_WhenTableMissing_ReportsUnavailableAndEmpty()
    {
        var reader = CreateReader();

        Assert.That(reader.IsAvailable, Is.False);
        Assert.That(reader.Read().IsEmpty, Is.True);
    }

    [Test]
    public void Read_PopulatesEveryFieldFromTheStructure()
    {
        // 16384 MB is the largest power-of-two capacity still expressible in the plain megabyte form: bit 15
        // is the kilobyte flag, so 32768 would decode as "0 KB" rather than 32 GB. Anything that size or
        // larger has to use the extended field, which Read_DecodesExtendedSizeForLargeModules covers.
        WriteTable(MemoryDevice(sizeField: 16384), EndOfTable());

        var module = CreateReader().Read().Modules.Single();

        Assert.Multiple(() =>
        {
            Assert.That(module.CapacityBytes, Is.EqualTo(16384UL * 1024UL * 1024UL));
            Assert.That(module.MemoryType, Is.EqualTo("DDR5"));
            Assert.That(module.FormFactor, Is.EqualTo("SODIMM"));
            Assert.That(module.SpeedMHz, Is.EqualTo(3200));
            Assert.That(module.DataWidth, Is.EqualTo(64));
            Assert.That(module.BankLabel, Is.EqualTo("BANK 0"));
            Assert.That(module.Manufacturer, Is.EqualTo("Crucial"));
            Assert.That(module.SerialNumber, Is.EqualTo("E7DF1A22"));
            Assert.That(module.PartNumber, Is.EqualTo("CT32G56C46S5.M8G1"));
            Assert.That(module.MinVoltage, Is.EqualTo(1100));
        });
    }

    [Test]
    public void Read_OmitsEmptySlots()
    {
        // Firmware publishes a structure per socket. A zero size means the socket is empty — listing it is
        // exactly the phantom-module bug this reader exists to avoid.
        // Mirrors the machine this was written against: a 32 GB stick (extended size, since it exceeds what the
        // megabyte form can express), an empty socket, and a 16 GB stick.
        WriteTable(
            MemoryDevice(sizeField: 0x7FFF, extendedSize: 32768),
            MemoryDevice(sizeField: 0),
            MemoryDevice(sizeField: 16384),
            EndOfTable());

        var modules = CreateReader().Read().Modules;

        Assert.That(modules, Has.Count.EqualTo(2));
        Assert.That(
            modules.Sum(module => (long)module.CapacityBytes),
            Is.EqualTo(48L * 1024 * 1024 * 1024),
            "the two installed sticks should total 48 GiB with no aggregate entry");
    }

    [Test]
    public void Read_DecodesKilobyteScaledSizes()
    {
        // Bit 15 set means the value is in kilobytes rather than megabytes.
        WriteTable(MemoryDevice(sizeField: 0x8000 | 512), EndOfTable());

        Assert.That(CreateReader().Read().Modules.Single().CapacityBytes, Is.EqualTo(512UL * 1024UL));
    }

    [Test]
    public void Read_DecodesExtendedSizeForLargeModules()
    {
        // 0x7FFF redirects to the 2.7 extended dword, which is how modules above 32 GB are expressed.
        WriteTable(MemoryDevice(sizeField: 0x7FFF, extendedSize: 65536), EndOfTable());

        Assert.That(CreateReader().Read().Modules.Single().CapacityBytes, Is.EqualTo(64UL * 1024 * 1024 * 1024));
    }

    [Test]
    public void Read_WhenSizeIsUnknown_OmitsTheModule()
    {
        WriteTable(MemoryDevice(sizeField: 0xFFFF), EndOfTable());

        Assert.That(CreateReader().Read().IsEmpty, Is.True);
    }

    [Test]
    public void Read_WhenStructureIsTruncatedBeforeLaterFields_StillReportsWhatIsPresent()
    {
        // An SMBIOS 2.1 structure legitimately ends before speed, manufacturer and the rest. Those fields must
        // read as unknown rather than picking up the neighbouring structure's bytes.
        var truncated = MemoryDevice(sizeField: 16384)[..0x15];
        truncated[1] = 0x15;

        WriteTable([.. truncated, .. StringTable()], EndOfTable());

        var module = CreateReader().Read().Modules.Single();

        Assert.Multiple(() =>
        {
            Assert.That(module.CapacityBytes, Is.EqualTo(16384UL * 1024UL * 1024UL));
            Assert.That(module.FormFactor, Is.EqualTo("SODIMM"));
            Assert.That(module.SpeedMHz, Is.Zero);
            Assert.That(module.PartNumber, Is.Null);
            Assert.That(module.SerialNumber, Is.Null);
        });
    }

    [Test]
    public void Read_StopsAtEndOfTableMarker()
    {
        // Anything after the 127 marker is not part of the table and must not be parsed.
        WriteTable(MemoryDevice(sizeField: 16384), EndOfTable(), MemoryDevice(sizeField: 32768));

        Assert.That(CreateReader().Read().Modules, Has.Count.EqualTo(1));
    }

    [Test]
    public void Read_WhenTableHasNoMemoryDevices_ReturnsEmpty()
    {
        // A type 1 (System Information) structure, then the marker.
        WriteTable([1, 4, 0x01, 0x00, 0x00, 0x00], EndOfTable());

        Assert.That(CreateReader().Read().IsEmpty, Is.True);
    }

    [Test]
    public void Read_WhenTableIsTruncatedMidStructure_DoesNotThrow()
    {
        WriteTable(MemoryDevice(sizeField: 16384)[..10]);

        Assert.That(() => CreateReader().Read(), Throws.Nothing);
    }

    [TestCase((ushort)0, 0UL)]
    [TestCase((ushort)0xFFFF, 0UL)]
    [TestCase((ushort)1024, 1024UL * 1024 * 1024)]
    public void ReadCapacityBytes_HandlesSentinelValues(ushort sizeField, ulong expected)
        => Assert.That(LinuxDmiMemoryInventoryReader.ReadCapacityBytes(sizeField, 0), Is.EqualTo(expected));

    [Test]
    public void ReadString_WhenIndexIsZero_ReturnsNull()
        => Assert.That(LinuxDmiMemoryInventoryReader.ReadString([0, 0], 0, 0), Is.Null);

    [Test]
    public void ReadString_WhenIndexIsPastTheTable_ReturnsNull()
    {
        var table = StringTable("first", "second");

        Assert.That(LinuxDmiMemoryInventoryReader.ReadString(table, 0, 5), Is.Null);
    }
}
