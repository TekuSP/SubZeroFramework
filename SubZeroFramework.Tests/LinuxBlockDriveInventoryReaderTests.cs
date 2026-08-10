using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the Linux block enumeration against a synthetic sysfs tree and mount table.
/// </summary>
/// <remarks>
/// The reader takes its sysfs root and mount-table path as constructor arguments precisely so this is
/// possible: the layout of <c>/sys/block</c> is stable and documented, so a fixture tree reproduces it
/// faithfully and covers what would otherwise need several machines — an NVMe SSD and a rotational SATA disk,
/// a LUKS volume whose filesystem sits on a device-mapper node rather than the partition, virtual block
/// devices that must be excluded, and a mount path containing a space.
///
/// That last case is the one that started this: the kernel escapes a space as <c>\040</c> in
/// <c>/proc/mounts</c>, .NET's own mount enumeration does not decode it, and the resulting DriveInfo throws
/// when asked for anything that stats the path — which is what made Hardware.Info's entire drive list throw.
/// </remarks>
[TestFixture]
public class LinuxBlockDriveInventoryReaderTests
{
    private string _root = string.Empty;
    private string _mounts = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "szf-block-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _mounts = Path.Combine(_root, "mounts");
        File.WriteAllText(_mounts, string.Empty);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private LinuxBlockDriveInventoryReader CreateReader()
        => new(NullLogger<LinuxBlockDriveInventoryReader>.Instance, _root, _mounts);

    private void WriteAttribute(string relativePath, string value)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }

    /// <summary>An NVMe SSD, padded exactly the way sysfs pads the model field.</summary>
    private void GivenNvmeDisk(string name = "nvme0n1", string sectors = "1953525168")
    {
        WriteAttribute($"{name}/device/model", "CT1000P3PSSD8                           \n");
        WriteAttribute($"{name}/device/serial", "24514CFE212E        \n");
        WriteAttribute($"{name}/device/firmware_rev", "P9CR413 \n");
        WriteAttribute($"{name}/queue/rotational", "0\n");
        WriteAttribute($"{name}/removable", "0\n");
        WriteAttribute($"{name}/size", sectors + "\n");
    }

    [Test]
    public void Read_WhenBlockRootMissing_ReportsUnavailableAndEmpty()
    {
        var reader = new LinuxBlockDriveInventoryReader(
            NullLogger<LinuxBlockDriveInventoryReader>.Instance,
            Path.Combine(_root, "does-not-exist"),
            _mounts);

        Assert.That(reader.IsAvailable, Is.False);
        Assert.That(reader.Read().IsEmpty, Is.True);
    }

    [Test]
    public void Read_PopulatesHardwareFieldsFromSysfs()
    {
        GivenNvmeDisk();

        var drive = CreateReader().Read().Drives.Single();

        Assert.Multiple(() =>
        {
            Assert.That(drive.Name, Is.EqualTo("/dev/nvme0n1"));
            // sysfs pads these to a fixed width; the reader must not surface the padding.
            Assert.That(drive.Model, Is.EqualTo("CT1000P3PSSD8"));
            Assert.That(drive.SerialNumber, Is.EqualTo("24514CFE212E"));
            Assert.That(drive.FirmwareRevision, Is.EqualTo("P9CR413"));
            Assert.That(drive.MediaType, Is.EqualTo("SSD"));
            // The size attribute is always in 512-byte units, never the device's logical block size.
            Assert.That(drive.Size, Is.EqualTo(1953525168UL * 512UL));
        });
    }

    [Test]
    public void Read_WhenDiskIsRotational_ReportsHdd()
    {
        GivenNvmeDisk("sda");
        WriteAttribute("sda/queue/rotational", "1\n");
        // SCSI and ATA expose "rev" where NVMe exposes "firmware_rev".
        File.Delete(Path.Combine(_root, "sda/device/firmware_rev"));
        WriteAttribute("sda/device/rev", "SB10\n");

        var drive = CreateReader().Read().Drives.Single();

        Assert.That(drive.MediaType, Is.EqualTo("HDD"));
        Assert.That(drive.FirmwareRevision, Is.EqualTo("SB10"));
    }

    [Test]
    public void Read_ExcludesVirtualBlockDevices()
    {
        GivenNvmeDisk();
        // loop, zram and device-mapper nodes have no "device" link — that is the whole test for exclusion.
        Directory.CreateDirectory(Path.Combine(_root, "loop0"));
        Directory.CreateDirectory(Path.Combine(_root, "zram0"));
        Directory.CreateDirectory(Path.Combine(_root, "dm-0"));

        var drives = CreateReader().Read().Drives;

        Assert.That(drives.Select(drive => drive.Name), Is.EqualTo(new[] { "/dev/nvme0n1" }));
    }

    [Test]
    public void Read_WhenMountPathContainsEscapedSpace_DoesNotThrow()
    {
        GivenNvmeDisk();
        Directory.CreateDirectory(Path.Combine(_root, "nvme0n1", "nvme0n1p1"));
        WriteAttribute("nvme0n1/nvme0n1p1/partition", "1\n");
        // The kernel writes a space as \040. Decoding it is the reader's job; .NET's own enumeration omits it.
        File.WriteAllText(_mounts, "/dev/nvme0n1p1 /mnt/Google\\040Drive ext4 rw 0 0\n");

        var drives = CreateReader().Read().Drives;

        Assert.That(drives, Has.Count.EqualTo(1));
        Assert.That(drives[0].Model, Is.EqualTo("CT1000P3PSSD8"));
    }

    [TestCase("plain", "plain")]
    [TestCase("with\\040space", "with space")]
    [TestCase("tab\\011here", "tab\there")]
    [TestCase("back\\134slash", "back\\slash")]
    [TestCase("trailing\\", "trailing\\")]
    [TestCase("\\04", "\\04")]
    public void Unescape_DecodesOctalSequencesAndLeavesEverythingElseAlone(string input, string expected)
        => Assert.That(LinuxBlockDriveInventoryReader.Unescape(input), Is.EqualTo(expected));

    [Test]
    public void Read_AttributesFreeSpaceThroughDeviceMapperSlaves()
    {
        GivenNvmeDisk();
        Directory.CreateDirectory(Path.Combine(_root, "nvme0n1", "nvme0n1p2"));
        WriteAttribute("nvme0n1/nvme0n1p2/partition", "2\n");
        // A LUKS volume: the filesystem is mounted on the mapper node, which names its backing partition
        // under slaves/. Without the walk the space would be attributed to no disk at all.
        Directory.CreateDirectory(Path.Combine(_root, "dm-0", "slaves", "nvme0n1p2"));

        // The mount point must exist for the free-space stat to succeed; the reader is expected to survive
        // either way, so this asserts the resolution reached the disk rather than a specific byte count.
        var mountPoint = Path.Combine(_root, "mnt");
        Directory.CreateDirectory(mountPoint);
        File.WriteAllText(_mounts, $"/dev/dm-0 {mountPoint.Replace(" ", "\\040")} btrfs rw 0 0\n");

        var drive = CreateReader().Read().Drives.Single();

        Assert.That(drive.Name, Is.EqualTo("/dev/nvme0n1"));
        Assert.That(drive.FreeSpace, Is.GreaterThan(0UL), "free space should be traced through dm-0 to its backing disk");
    }

    [Test]
    public void Read_CountsAFilesystemOnceAcrossManyMountPoints()
    {
        GivenNvmeDisk();
        Directory.CreateDirectory(Path.Combine(_root, "nvme0n1", "nvme0n1p2"));
        WriteAttribute("nvme0n1/nvme0n1p2/partition", "2\n");

        var mountPoint = Path.Combine(_root, "mnt");
        Directory.CreateDirectory(mountPoint);

        // btrfs subvolumes surface as many mounts of ONE filesystem. Summing per mount would report multiple
        // times the disk's real free space — on the machine this was written against, 1.18 TB on a 931 GB SSD.
        var singleMount = $"/dev/nvme0n1p2 {mountPoint} btrfs rw 0 0\n";
        File.WriteAllText(_mounts, singleMount);
        var oneMountFreeSpace = CreateReader().Read().Drives.Single().FreeSpace;

        File.WriteAllText(_mounts, string.Concat(Enumerable.Repeat(singleMount, 9)));
        var nineMountFreeSpace = CreateReader().Read().Drives.Single().FreeSpace;

        Assert.That(nineMountFreeSpace, Is.EqualTo(oneMountFreeSpace));
    }
}
