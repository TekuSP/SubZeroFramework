namespace SubZeroFramework.Models;

/// <summary>
/// Physical drives discovered outside Hardware.Info.
/// </summary>
/// <remarks>
/// Exists because Hardware.Info's Linux drive enumeration never describes the physical device: it builds a
/// single synthetic entry from mount points, leaving model, serial, firmware and size blank (verified against
/// Hardware.Info.Aot 110.0.0.1, as root — privileges make no difference). A platform that can enumerate the
/// real devices supplies them through <see cref="IDriveInventoryReader"/> instead, exactly as
/// <see cref="IGraphicsInventoryReader"/> does for adapters and displays.
/// </remarks>
public sealed record DriveInventory
{
    public static DriveInventory Empty { get; } = new()
    {
        Drives = [],
    };

    public required IReadOnlyList<HardwareInfoDrive> Drives { get; init; }

    public bool IsEmpty => Drives.Count == 0;
}

/// <summary>
/// Supplies physical drives on platforms where Hardware.Info cannot.
/// </summary>
/// <remarks>
/// Like the graphics and compute readers, every implementation is allowed to report nothing: a machine whose
/// kernel exposes no block devices (a container with no disks of its own) must yield an empty inventory rather
/// than throw. Runs on the SLOW inventory tier — the device set is near-static.
/// </remarks>
public interface IDriveInventoryReader
{
    /// <summary>False when this platform cannot enumerate; <see cref="Read"/> then returns empty.</summary>
    bool IsAvailable { get; }

    /// <summary>Every physical drive the platform can describe.</summary>
    DriveInventory Read();
}
