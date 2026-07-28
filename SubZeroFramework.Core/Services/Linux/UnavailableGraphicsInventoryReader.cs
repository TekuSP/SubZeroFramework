using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Null object for platforms that get their graphics inventory from Hardware.Info (Windows), so the provider
/// never branches on null.
/// </summary>
public sealed class UnavailableGraphicsInventoryReader : IGraphicsInventoryReader
{
    public static UnavailableGraphicsInventoryReader Instance { get; } = new();

    private UnavailableGraphicsInventoryReader()
    {
    }

    public bool IsAvailable => false;

    public GraphicsInventory Read() => GraphicsInventory.Empty;
}
