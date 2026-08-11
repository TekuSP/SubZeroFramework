namespace SubZeroFramework.Models;

/// <summary>
/// Installed memory modules discovered outside Hardware.Info.
/// </summary>
/// <remarks>
/// Exists because Hardware.Info's Linux memory list is built by parsing <c>lshw</c>, which reports the
/// "System Memory" container node alongside the real banks — so a machine with two sticks lists three modules,
/// the first being their total with no form factor and no speed. It also leaves manufacturer, part number,
/// serial number, bank label, memory type and data width empty even as root. A platform that can enumerate the
/// real devices supplies them through <see cref="IMemoryInventoryReader"/> instead, exactly as
/// <see cref="IDriveInventoryReader"/> and <see cref="IGraphicsInventoryReader"/> do for their device classes.
/// </remarks>
public sealed record MemoryInventory
{
    public static MemoryInventory Empty { get; } = new()
    {
        Modules = [],
    };

    public required IReadOnlyList<HardwareInfoMemoryModule> Modules { get; init; }

    public bool IsEmpty => Modules.Count == 0;
}

/// <summary>
/// Supplies installed memory modules on platforms where Hardware.Info cannot.
/// </summary>
/// <remarks>
/// Like the drive, graphics and compute readers, every implementation is allowed to report nothing: a virtual
/// machine whose firmware publishes no memory devices must yield an empty inventory rather than throw. Runs on
/// the SLOW inventory tier — modules do not change without a power cycle.
/// </remarks>
public interface IMemoryInventoryReader
{
    /// <summary>False when this platform cannot enumerate; <see cref="Read"/> then returns empty.</summary>
    bool IsAvailable { get; }

    /// <summary>Every installed module the platform can describe. Empty slots are omitted.</summary>
    MemoryInventory Read();
}
