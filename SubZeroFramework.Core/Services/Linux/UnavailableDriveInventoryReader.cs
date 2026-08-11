using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Null object for platforms that get their drive inventory from Hardware.Info (Windows), so the provider
/// never branches on null.
/// </summary>
public sealed class UnavailableDriveInventoryReader : IDriveInventoryReader
{
    public static UnavailableDriveInventoryReader Instance { get; } = new();

    private UnavailableDriveInventoryReader()
    {
    }

    public bool IsAvailable => false;

    public DriveInventory Read() => DriveInventory.Empty;
}
