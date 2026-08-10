using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Null object for platforms that get their memory inventory from Hardware.Info (Windows), so the provider
/// never branches on null.
/// </summary>
public sealed class UnavailableMemoryInventoryReader : IMemoryInventoryReader
{
    public static UnavailableMemoryInventoryReader Instance { get; } = new();

    private UnavailableMemoryInventoryReader()
    {
    }

    public bool IsAvailable => false;

    public MemoryInventory Read() => MemoryInventory.Empty;
}
