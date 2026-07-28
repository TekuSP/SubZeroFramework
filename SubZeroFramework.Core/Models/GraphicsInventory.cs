namespace SubZeroFramework.Models;

/// <summary>
/// Graphics adapters and displays discovered outside Hardware.Info.
/// </summary>
/// <remarks>
/// Exists because Hardware.Info implements both lists by shelling out to <c>xrandr</c>, which needs a display
/// server and therefore cannot work from a headless root service — see the Linux branch in
/// <c>FrameworkDataProvider.RefreshHardwareInformation</c>. A platform that can enumerate them another way
/// supplies them through <see cref="IGraphicsInventoryReader"/> instead.
/// </remarks>
public sealed record GraphicsInventory
{
    public static GraphicsInventory Empty { get; } = new()
    {
        VideoControllers = [],
        Monitors = [],
    };

    public required IReadOnlyList<HardwareInfoVideoController> VideoControllers { get; init; }

    public required IReadOnlyList<HardwareInfoMonitor> Monitors { get; init; }

    public bool IsEmpty => VideoControllers.Count == 0 && Monitors.Count == 0;
}

/// <summary>
/// Supplies graphics adapters and displays on platforms where Hardware.Info cannot.
/// </summary>
/// <remarks>
/// Like the compute readers, every implementation is allowed to report nothing: a machine whose kernel exposes
/// no DRM devices (a VM with a virtio display, a container) must yield an empty inventory rather than throw.
/// Runs on the SLOW inventory tier — the device set is near-static.
/// </remarks>
public interface IGraphicsInventoryReader
{
    /// <summary>False when this platform cannot enumerate; <see cref="Read"/> then returns empty.</summary>
    bool IsAvailable { get; }

    /// <summary>Every adapter and display the platform can describe.</summary>
    GraphicsInventory Read();
}
