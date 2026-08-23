using System.Globalization;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Enumerates physical drives from the kernel's block tree, without Hardware.Info and without lshw.
/// </summary>
/// <remarks>
/// This is the Linux answer to Hardware.Info's drive list, which cannot describe a real device here: its
/// Linux implementation returns one synthetic entry built from mount points, with model, serial, firmware and
/// size all blank. Verified against Hardware.Info.Aot 110.0.0.1 running as root — this is a library gap, not a
/// privilege problem, so no amount of elevation fixes it.
///
/// Everything the UI needs is plain text under <c>/sys/block</c> and is world-readable, so this reader needs no
/// privileges at all: <c>device/model</c>, <c>device/serial</c>, <c>device/firmware_rev</c>,
/// <c>queue/rotational</c>, and <c>size</c> (always in 512-byte units regardless of the device's logical block
/// size).
///
/// Free space is the one thing sysfs cannot answer, so it is attributed from the mount table: each mounted
/// filesystem is traced back to the disk that ultimately backs it, through partitions AND through device-mapper
/// slaves, so an encrypted root (LUKS: filesystem on <c>/dev/mapper/x</c> → <c>dm-N</c> → partition → disk)
/// still reports its space against the physical drive rather than vanishing.
///
/// The mount table is parsed here rather than through <see cref="DriveInfo.GetDrives"/> because .NET does not
/// decode the octal escapes the kernel writes into <c>/proc/mounts</c> for space, tab, newline and backslash —
/// a mount path containing any of them yields a DriveInfo whose name cannot be stat'd, and asking it for
/// DriveFormat throws. That defect is what made Hardware.Info's whole drive enumeration throw in the first
/// place; unescaping here means a mount path with a space in it is simply handled.
///
/// Everything is best-effort. An unreadable attribute, a disk with no partitions, a mount whose device has
/// disappeared, and a machine with no block devices at all each degrade the result rather than failing the
/// inventory refresh.
/// </remarks>
/// <summary>
/// Default kernel paths the drive reader enumerates. Separate from the reader so the primary constructor can
/// use them as parameter defaults, mirroring <c>DrmSysfs</c>.
/// </summary>
public static class BlockSysfs
{
    public const string DefaultSysfsBlockRoot = "/sys/block";
    public const string DefaultProcMountsPath = "/proc/mounts";
}

public sealed class LinuxBlockDriveInventoryReader(
    ILogger<LinuxBlockDriveInventoryReader> logger,
    string sysfsBlockRoot = BlockSysfs.DefaultSysfsBlockRoot,
    string procMountsPath = BlockSysfs.DefaultProcMountsPath) : IDriveInventoryReader
{
    /// <summary>Kernel block size for the <c>size</c> attribute — fixed at 512 bytes, never the device's own.</summary>
    private const ulong SysfsSectorBytes = 512;

    private bool _loggedReadFailure;

    /// <summary>
    /// True when a populated block tree exists at the configured root.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT an <c>OperatingSystem.IsLinux()</c> check, for the same reason the DRM reader avoids
    /// one: this is ordinary file I/O over an injectable root, so gating it on the OS would make the
    /// enumeration untestable off Linux. Which platforms construct it at all is a DI decision.
    /// </remarks>
    public bool IsAvailable => Directory.Exists(sysfsBlockRoot);

    public DriveInventory Read()
    {
        if (!IsAvailable)
        {
            return DriveInventory.Empty;
        }

        try
        {
            return ReadCore();
        }
        catch (Exception exception)
        {
            // The inventory tier must survive anything sysfs does; log once so a persistent problem is
            // visible without a line per refresh.
            if (!_loggedReadFailure)
            {
                _loggedReadFailure = true;
                logger.LogWarning(exception, "Could not enumerate drives from {BlockPath}; the Storage page will be empty.", sysfsBlockRoot);
            }

            return DriveInventory.Empty;
        }
    }

    private DriveInventory ReadCore()
    {
        var diskNames = Directory.EnumerateDirectories(sysfsBlockRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            // A physical device has a "device" link; loop, ram, zram, md and device-mapper nodes do not. That
            // single test is what keeps the Storage page showing disks rather than every virtual block device.
            .Where(name => Directory.Exists(Path.Combine(sysfsBlockRoot, name, "device")))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        if (diskNames.Count == 0)
        {
            return DriveInventory.Empty;
        }

        var freeSpaceByDisk = ReadFreeSpaceByDisk(diskNames);

        List<HardwareInfoDrive> drives = [];
        uint index = 0;

        foreach (var diskName in diskNames)
        {
            var diskPath = Path.Combine(sysfsBlockRoot, diskName);
            var model = ReadAttribute(diskPath, "device/model");
            var isRotational = string.Equals(ReadAttribute(diskPath, "queue/rotational"), "1", StringComparison.Ordinal);
            var isRemovable = string.Equals(ReadAttribute(diskPath, "removable"), "1", StringComparison.Ordinal);
            var mediaType = isRemovable
                ? "Removable"
                : isRotational ? "HDD" : "SSD";

            drives.Add(new HardwareInfoDrive(
                Index: index,
                Name: $"/dev/{diskName}",
                Model: model,
                Caption: model ?? $"/dev/{diskName}",
                Description: isRotational ? "Rotational drive" : "Solid state drive",
                // NVMe exposes no vendor attribute; SCSI/ATA do. Null rather than a guess derived from the model.
                Manufacturer: ReadAttribute(diskPath, "device/vendor"),
                MediaType: mediaType,
                SerialNumber: ReadAttribute(diskPath, "device/serial"),
                // NVMe uses firmware_rev, SCSI/ATA use rev.
                FirmwareRevision: ReadAttribute(diskPath, "device/firmware_rev") ?? ReadAttribute(diskPath, "device/rev"),
                Size: ReadSizeBytes(diskPath),
                FreeSpace: freeSpaceByDisk.GetValueOrDefault(diskName)));

            index++;
        }

        return new DriveInventory { Drives = drives };
    }

    private ulong ReadSizeBytes(string diskPath)
        => ulong.TryParse(ReadAttribute(diskPath, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectors)
            ? sectors * SysfsSectorBytes
            : 0;

    private static string? ReadAttribute(string diskPath, string relativePath)
    {
        try
        {
            var path = Path.Combine(diskPath, relativePath);
            if (!File.Exists(path))
            {
                return null;
            }

            // sysfs pads several of these to a fixed width (model in particular), so the raw value is unusable
            // as a display string.
            var value = File.ReadAllText(path).Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sums the free space of every mounted filesystem against the physical disk that ultimately backs it.
    /// </summary>
    private Dictionary<string, ulong> ReadFreeSpaceByDisk(IReadOnlyCollection<string> diskNames)
    {
        Dictionary<string, ulong> freeSpaceByDisk = new(StringComparer.Ordinal);
        // One filesystem can appear at several mount points (bind mounts). Counting its free space once per
        // mount would inflate the total well past the size of the disk.
        HashSet<string> countedDevices = new(StringComparer.Ordinal);

        foreach (var (deviceName, mountPoint) in EnumerateMounts())
        {
            if (!countedDevices.Add(deviceName))
            {
                continue;
            }

            var owningDisk = ResolveOwningDisk(deviceName, diskNames);
            if (owningDisk is null)
            {
                continue;
            }

            try
            {
                var available = new DriveInfo(mountPoint).AvailableFreeSpace;
                if (available > 0)
                {
                    freeSpaceByDisk[owningDisk] = freeSpaceByDisk.GetValueOrDefault(owningDisk) + (ulong)available;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A mount can disappear between reading the table and stat'ing it, and some pseudo-filesystems
                // refuse the call outright. Neither is a reason to lose the rest of the inventory.
            }
        }

        return freeSpaceByDisk;
    }

    /// <summary>
    /// Yields (kernel device name, mount point) for every real block-device mount in the table.
    /// </summary>
    private IEnumerable<(string DeviceName, string MountPoint)> EnumerateMounts()
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(procMountsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var fields = line.Split(' ');
            if (fields.Length < 2)
            {
                continue;
            }

            var source = Unescape(fields[0]);
            if (!source.StartsWith("/dev/", StringComparison.Ordinal))
            {
                continue;
            }

            var deviceName = ResolveKernelDeviceName(source);
            if (deviceName is null)
            {
                continue;
            }

            yield return (deviceName, Unescape(fields[1]));
        }
    }

    /// <summary>
    /// Decodes the octal escapes the kernel writes for space, tab, newline and backslash.
    /// </summary>
    /// <remarks>
    /// .NET's own mount enumeration omits this step, which is why a mount path containing a space produces a
    /// DriveInfo that throws DriveNotFoundException on any property that stats the path.
    /// </remarks>
    internal static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\'
                && i + 3 < value.Length
                && IsOctalDigit(value[i + 1])
                && IsOctalDigit(value[i + 2])
                && IsOctalDigit(value[i + 3]))
            {
                builder.Append((char)(((value[i + 1] - '0') * 64) + ((value[i + 2] - '0') * 8) + (value[i + 3] - '0')));
                i += 3;
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();

        static bool IsOctalDigit(char candidate) => candidate is >= '0' and <= '7';
    }

    /// <summary>
    /// Turns a <c>/dev</c> path into the kernel's own device name, following symlinks so
    /// <c>/dev/mapper/cr_root</c> resolves to <c>dm-0</c>.
    /// </summary>
    /// <remarks>
    /// Falls back to the path's last segment when the node cannot be resolved, and that fallback is the
    /// point rather than a nicety. ResolveLinkTarget THROWS when the path does not exist, and returning null
    /// there dropped the whole mount — so a single absent device node cost the backing disk its entire
    /// free-space figure, silently. That is not a corner case: /dev is not fully populated in containers, a
    /// device can disappear between reading the mount table and resolving it, and a machine with no
    /// device-mapper at all has no /dev/dm-N to resolve.
    ///
    /// The fallback is also simply CORRECT for the common case: every non-symlink device path already ends
    /// in the kernel's own name (/dev/nvme0n1p2, /dev/sda1, /dev/dm-0), so the last segment is the answer
    /// resolution would have produced. Only /dev/mapper/&lt;name&gt; and /dev/disk/by-*/&lt;id&gt; need the
    /// symlink walk, and those still get it whenever /dev is readable. A name that resolves to nothing in
    /// sysfs is discarded by the caller anyway.
    /// </remarks>
    private static string? ResolveKernelDeviceName(string devicePath)
    {
        try
        {
            var resolved = File.ResolveLinkTarget(devicePath, returnFinalTarget: true)?.FullName ?? devicePath;
            var name = Path.GetFileName(resolved);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The node is absent or unreadable; the literal name below is still worth trying.
        }

        var literalName = Path.GetFileName(devicePath);
        return string.IsNullOrEmpty(literalName) ? null : literalName;
    }

    /// <summary>
    /// Walks from a mounted device to the physical disk backing it: a partition resolves to its parent, and a
    /// device-mapper node resolves through its slaves (so LUKS and LVM land on the real drive).
    /// </summary>
    private string? ResolveOwningDisk(string deviceName, IReadOnlyCollection<string> diskNames, int depth = 0)
    {
        // Stacked mappers are legitimate (LVM on LUKS), but the slave graph is attacker-independent kernel
        // state and a cycle would still hang the refresh, so the walk is bounded.
        if (depth > 8)
        {
            return null;
        }

        if (diskNames.Contains(deviceName))
        {
            return deviceName;
        }

        // A partition lives beneath its disk: /sys/block/nvme0n1/nvme0n1p2.
        foreach (var diskName in diskNames)
        {
            if (Directory.Exists(Path.Combine(sysfsBlockRoot, diskName, deviceName)))
            {
                return diskName;
            }
        }

        // Device-mapper and MD nodes name their backing devices under slaves/.
        var slavesPath = Path.Combine(sysfsBlockRoot, deviceName, "slaves");
        if (!Directory.Exists(slavesPath))
        {
            return null;
        }

        try
        {
            foreach (var slavePath in Directory.EnumerateFileSystemEntries(slavesPath))
            {
                var slaveName = Path.GetFileName(slavePath);
                if (string.IsNullOrEmpty(slaveName))
                {
                    continue;
                }

                if (ResolveOwningDisk(slaveName, diskNames, depth + 1) is { } owningDisk)
                {
                    return owningDisk;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
