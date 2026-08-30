using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>
/// Presentation names for the firmware components, so every view spells them the same way.
/// </summary>
/// <remarks>
/// Shared rather than page-local because the same components appear in Device Capabilities and in
/// Settings → About. It lived on one of those pages first, and the other promptly rendered the raw firmware
/// identifiers instead — which is exactly the failure a shared catalog exists to prevent.
/// </remarks>
public static class FirmwareComponentDisplay
{
    /// <summary>
    /// Names a power-delivery controller by the ports it drives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Firmware identifies these as Right01 / Left23 / Back — the EC's probe order and its own port
    /// numbering, where indices 0 and 1 are the right-hand pair and 2 and 3 the left. That numbering appears
    /// nowhere a user can see it: the ports are labelled USB-C 1 to 4 on the Power page, counting from the
    /// right. So the raw name is not merely terse, it is off by one against everything else in the app.
    /// </para>
    /// <para>
    /// An unrecognised slot keeps whatever the firmware called it. A wrong friendly name is worse than a
    /// terse true one.
    /// </para>
    /// </remarks>
    public static string PowerDeliverySlotName(FirmwareComponent controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return controller.ProductName switch
        {
            "Right01" => "Right side (USB-C 1 & 2)",
            "Left23" => "Left side (USB-C 3 & 4)",
            "Back" => "Rear",
            _ => controller.ProductName.Length > 0
                ? controller.ProductName
                : $"Controller {controller.SlotIndex + 1}",
        };
    }

    /// <summary>
    /// Names a component that reports no product name of its own.
    /// </summary>
    /// <remarks>
    /// Counts within the group rather than echoing the slot index. A lone unnamed hub rendered as "Slot 0"
    /// reads as a slot number the user could go and look at, when it is really a USB enumeration index that
    /// corresponds to nothing on the machine. "USB hub" — or "USB hub 2" where there are several — claims
    /// only what is true.
    /// </remarks>
    /// <param name="component">The component being named.</param>
    /// <param name="singular">What one of these is called, e.g. "USB hub".</param>
    /// <param name="position">Zero-based position within its group.</param>
    /// <param name="groupSize">How many components share the group.</param>
    public static string ComponentName(FirmwareComponent component, string singular, int position, int groupSize)
    {
        ArgumentNullException.ThrowIfNull(component);

        return component.ProductName.Length > 0
            ? component.ProductName
            : groupSize > 1 ? $"{singular} {position + 1}" : singular;
    }
}
