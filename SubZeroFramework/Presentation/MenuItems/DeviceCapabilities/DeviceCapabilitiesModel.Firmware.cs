using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

using SubZeroFramework.Models;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

/// <summary>
/// The firmware-versions section: what each peripheral, power-delivery controller and drive is running.
/// </summary>
/// <remarks>
/// Its own partial because it is derived from one field of the snapshot and has nothing to do with the
/// telemetry joins that make up the rest of this page. Everything here is static for the life of the machine.
/// </remarks>
public partial class DeviceCapabilitiesModel
{
    /// <summary>Every firmware row, already grouped and labelled.</summary>
    public ObservableCollection<FirmwareGroupModel> FirmwareGroups { get; } = [];

    /// <summary>
    /// The whole section, hidden when the machine reports no versions at all.
    /// </summary>
    /// <remarks>
    /// Hidden rather than shown empty: a firmware panel with no rows reads as a broken feature, where an
    /// absent one reads — correctly — as a machine that does not report versions.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareSectionVisibility))]
    public partial bool HasFirmware { get; private set; }

    public Visibility FirmwareSectionVisibility => HasFirmware ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Rebuilds the firmware groups, omitting any that is empty.
    /// </summary>
    /// <remarks>
    /// Empty groups are dropped rather than rendered with a heading and nothing under it. A Laptop 13 has no
    /// input modules and no retimer, and four empty headings would suggest four things had failed to read.
    /// </remarks>
    private void RefreshFirmware(FirmwareInventorySnapshot firmware)
    {
        List<FirmwareGroupModel> groups = [];

        AddGroup(groups, "Cameras", "Camera", firmware.Cameras);
        AddGroup(groups, "Input modules", "Input module", firmware.InputModules);
        AddGroup(groups, "USB hubs", "USB hub", firmware.UsbHubs);
        AddGroup(groups, "Audio", "Audio card", firmware.AudioCards);

        // The retimer joins the power-delivery group rather than getting a heading of its own. It is a
        // USB-C signal component and there is only ever one, so a "Retimer" heading over a single "Retimer"
        // row said the same word twice and nothing else.
        List<FirmwareRowModel> powerDelivery =
        [
            .. firmware.PowerDeliveryControllers.Select(static controller =>
                new FirmwareRowModel(PowerDeliverySlotName(controller), controller.Version)),
        ];

        if (firmware.RetimerVersion.Length > 0)
        {
            powerDelivery.Add(new FirmwareRowModel("Retimer", firmware.RetimerVersion));
        }

        if (powerDelivery.Count > 0)
        {
            groups.Add(new FirmwareGroupModel("Power delivery", powerDelivery));
        }

        if (firmware.NvmeDrives.Count > 0)
        {
            groups.Add(new FirmwareGroupModel(
                "Storage",
                [.. firmware.NvmeDrives.Select(static drive =>
                    new FirmwareRowModel(drive.ModelNumber, drive.FirmwareVersion))]));
        }

        // Assigned only when the CONTENT changed. The snapshot is rebuilt on the slow tier and almost always
        // says the same thing; handing the repeater a fresh equal list would re-create every row for nothing.
        if (!FirmwareGroups.SequenceEqual(groups))
        {
            FirmwareGroups.Clear();
            foreach (var group in groups)
            {
                FirmwareGroups.Add(group);
            }
        }

        HasFirmware = groups.Count > 0;

        ApplyNvmeFirmwareToDriveCards(firmware);
    }

    /// <summary>
    /// Fills in each drive card's firmware where the operating system did not report one.
    /// </summary>
    /// <remarks>
    /// Matched by device path, which is the same string the collector read the drive by — an exact join, not
    /// a name heuristic. The card keeps the operating system's value when it has one; this only fills the gap
    /// on drives WMI describes as having no firmware revision at all.
    /// </remarks>
    private void ApplyNvmeFirmwareToDriveCards(FirmwareInventorySnapshot firmware)
    {
        if (firmware.NvmeDrives.Count == 0)
        {
            return;
        }

        foreach (var card in StorageDriveCards)
        {
            var match = firmware.NvmeDrives
                .FirstOrDefault(drive => string.Equals(drive.DevicePath, card.Snapshot.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                card.NvmeFirmwareRevision = match.FirmwareVersion;
            }
        }
    }

    /// <summary>
    /// Names a power-delivery controller by the ports it drives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The firmware identifies these as Right01 / Left23 / Back, which is the probe order and the EC's own
    /// port numbering — index 0 and 1 are the right-hand pair, 2 and 3 the left. That numbering appears
    /// nowhere a user can see it: the ports are labelled USB-C 1 to 4 on the Power page, counting from the
    /// right. So the raw name is not merely terse, it is off by one against everything else in the app.
    /// </para>
    /// <para>
    /// An unrecognised slot keeps whatever the firmware called it. A wrong friendly name would be worse than
    /// a terse true one.
    /// </para>
    /// </remarks>
    private static string PowerDeliverySlotName(FirmwareComponent controller) => controller.ProductName switch
    {
        "Right01" => "Right side (USB-C 1 & 2)",
        "Left23" => "Left side (USB-C 3 & 4)",
        "Back" => "Rear",
        _ => controller.ProductName.Length > 0 ? controller.ProductName : $"Controller {controller.SlotIndex + 1}",
    };

    /// <summary>
    /// Adds one group, naming any component that reports no name of its own.
    /// </summary>
    /// <remarks>
    /// The fallback counts within the group rather than echoing the slot index. A lone unnamed hub rendered
    /// as "Slot 0" reads as a slot number the user could go and look at, when it is really a USB enumeration
    /// index that corresponds to nothing on the machine. "USB hub 1" claims only what is true: it is the
    /// first hub in this list.
    /// </remarks>
    private static void AddGroup(
        List<FirmwareGroupModel> groups,
        string title,
        string singular,
        IReadOnlyList<FirmwareComponent> components)
    {
        if (components.Count == 0)
        {
            return;
        }

        groups.Add(new FirmwareGroupModel(
            title,
            [.. components.Select((component, position) => new FirmwareRowModel(
                component.ProductName.Length > 0
                    ? component.ProductName
                    : components.Count > 1 ? $"{singular} {position + 1}" : singular,
                component.Version))]));
    }
}

/// <summary>One firmware row: what it is, and what it is running.</summary>
public sealed record FirmwareRowModel(string Name, string Version);

/// <summary>A heading and its rows. A record so an unchanged rebuild compares equal and is skipped.</summary>
public sealed record FirmwareGroupModel(string Title, IReadOnlyList<FirmwareRowModel> Rows);
