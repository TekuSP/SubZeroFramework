using System.Collections.Immutable;
using System.Globalization;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Enumerates graphics adapters and displays from the kernel's DRM tree, with no display server.
/// </summary>
/// <remarks>
/// This is the Linux answer to Hardware.Info's xrandr-based lists, which cannot run in a headless root
/// service. Adapters come from <c>/sys/class/drm/card*/device</c> (PCI IDs named through pci.ids); displays
/// come from each card's connectors, with their properties decoded from the connector's raw EDID.
///
/// A real advantage over the Windows path: the adapter/monitor link is EXACT here. A connector directory is
/// named after the card that owns it (<c>card0-eDP-1</c>), so a display is attributed to its GPU by kernel
/// topology rather than by the display-name heuristic the Windows side has to use.
///
/// Everything is best-effort. Missing pci.ids, an unreadable EDID, a card with no connectors and a machine
/// with no DRM devices at all each degrade the result rather than failing the inventory refresh.
/// </remarks>
public sealed class LinuxDrmGraphicsInventoryReader(
    ILogger<LinuxDrmGraphicsInventoryReader> logger,
    string sysfsRoot = DrmSysfs.DefaultSysfsRoot) : IGraphicsInventoryReader
{
    private readonly DrmSysfs _sysfs = new(sysfsRoot);
    private bool _loggedReadFailure;
    private bool _loggedMissingPciIds;

    /// <summary>
    /// True when a populated DRM tree exists at the configured root.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT an <c>OperatingSystem.IsLinux()</c> check: the whole reader is ordinary file I/O over
    /// an injectable root, so gating it on the OS would make the enumeration untestable off Linux. Which
    /// platforms construct it at all is a DI decision (see the service's Program.cs); a machine with no DRM
    /// tree — Windows, or a VM with no DRM devices — simply reports nothing.
    /// </remarks>
    public bool IsAvailable => Directory.Exists(_sysfs.ClassDrmPath);

    public GraphicsInventory Read()
    {
        if (!IsAvailable)
        {
            return GraphicsInventory.Empty;
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
                logger.LogWarning(exception, "Could not enumerate graphics devices from {DrmPath}; the Graphics page will be empty.", _sysfs.ClassDrmPath);
            }

            return GraphicsInventory.Empty;
        }
    }

    private GraphicsInventory ReadCore()
    {
        var cards = _sysfs.EnumerateCardNames();
        if (cards.Count == 0)
        {
            return GraphicsInventory.Empty;
        }

        // Pass 1: identify each card, so every PCI ID can be named in ONE streaming pass of pci.ids.
        List<CardRecord> cardRecords = [];
        foreach (var cardName in cards)
        {
            var devicePath = _sysfs.GetCardDevicePath(cardName);
            var uevent = DrmUevent.Parse(DrmSysfs.ReadAttribute(Path.Combine(devicePath, "uevent")));

            var vendorId = uevent.VendorId ?? DrmSysfs.ReadHexIdAttribute(Path.Combine(devicePath, "vendor"));
            var deviceId = uevent.DeviceId ?? DrmSysfs.ReadHexIdAttribute(Path.Combine(devicePath, "device"));

            cardRecords.Add(new CardRecord
            {
                CardName = cardName,
                DevicePath = devicePath,
                Driver = uevent.Driver,
                PciSlotName = uevent.PciSlotName,
                VendorId = vendorId,
                DeviceId = deviceId,
            });
        }

        var pciNames = ResolvePciNames(cardRecords);

        // Pass 2: build adapters and their displays together, so each side can name the other.
        List<HardwareInfoVideoController> controllers = [];
        List<HardwareInfoMonitor> monitors = [];
        List<(int MonitorIndex, string ControllerName)> monitorLinks = [];

        foreach (var card in cardRecords)
        {
            var controllerName = BuildAdapterName(card, pciNames);
            var connectors = ReadConnectors(card, ReadActiveModes(card));

            List<string> linkedMonitorNames = [];
            foreach (var connector in connectors)
            {
                var monitor = BuildMonitor(connector, controllerName);
                monitors.Add(monitor);
                monitorLinks.Add((monitors.Count - 1, controllerName));
                linkedMonitorNames.Add(monitor.DisplayName);
            }

            // The active mode of the first connected display doubles as the adapter's current mode, which is
            // what the WMI-shaped fields on the Windows side mean.
            var primary = connectors.FirstOrDefault(connector => connector.Status == DrmConnectorStatus.Connected);

            controllers.Add(new HardwareInfoVideoController(
                AdapterRAM: ReadVideoMemoryBytes(card),
                Caption: controllerName,
                CurrentBitsPerPixel: 0,
                CurrentHorizontalResolution: (uint)(primary?.Width ?? 0),
                CurrentNumberOfColors: 0,
                CurrentRefreshRate: (uint)Math.Round(primary?.RefreshHz ?? 0d, MidpointRounding.AwayFromZero),
                CurrentVerticalResolution: (uint)(primary?.Height ?? 0),
                Description: BuildAdapterDescription(card),
                // Linux has no driver DATE; the kernel exposes only a driver name and version.
                DriverDate: null,
                DriverVersion: ReadDriverVersion(card),
                Manufacturer: pciNames.GetValueOrDefault(GetPciKey(card))?.VendorName,
                MaxRefreshRate: 0,
                MinRefreshRate: 0,
                Name: controllerName,
                VideoModeDescription: BuildVideoModeDescription(primary),
                VideoProcessor: pciNames.GetValueOrDefault(GetPciKey(card))?.DeviceName,
                LinkedMonitorDisplayNames: [.. linkedMonitorNames]));
        }

        // Backfill each monitor's linked-adapter names now that the adapter names are final.
        var linkedControllersByMonitor = monitorLinks
            .GroupBy(link => link.MonitorIndex)
            .ToDictionary(group => group.Key, group => group.Select(link => link.ControllerName).ToImmutableArray());

        for (var index = 0; index < monitors.Count; index++)
        {
            if (linkedControllersByMonitor.TryGetValue(index, out var linked))
            {
                monitors[index] = monitors[index] with { LinkedVideoControllerDisplayNames = linked };
            }
        }

        return new GraphicsInventory
        {
            VideoControllers = controllers,
            Monitors = monitors,
        };
    }

    private IReadOnlyDictionary<PciDeviceId, PciDeviceNames> ResolvePciNames(IReadOnlyList<CardRecord> cards)
    {
        var ids = cards
            .Where(card => card.VendorId is not null && card.DeviceId is not null)
            .Select(GetPciKey)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<PciDeviceId, PciDeviceNames>();
        }

        var names = PciIdDatabase.Lookup(ids);
        if (names.Count == 0 && !_loggedMissingPciIds)
        {
            _loggedMissingPciIds = true;
            logger.LogInformation(
                "No pci.ids database found (looked in {Paths}); graphics adapters will be listed by their PCI IDs. Install the hwdata or pciutils package for full names.",
                string.Join(", ", PciIdDatabase.DefaultSearchPaths));
        }

        return names;
    }

    private static PciDeviceId GetPciKey(CardRecord card) => new(card.VendorId ?? 0, card.DeviceId ?? 0);

    /// <summary>Best available adapter name: the pci.ids device name, else the driver, else the raw IDs.</summary>
    private static string BuildAdapterName(CardRecord card, IReadOnlyDictionary<PciDeviceId, PciDeviceNames> pciNames)
    {
        if (pciNames.TryGetValue(GetPciKey(card), out var names))
        {
            if (!string.IsNullOrWhiteSpace(names.DeviceName))
            {
                return names.DeviceName;
            }

            if (!string.IsNullOrWhiteSpace(names.VendorName))
            {
                // "Advanced Micro Devices, Inc. [AMD/ATI] 150e" — vendor known, specific model too new for the database.
                return $"{names.VendorName} {card.DeviceId:x4}";
            }
        }

        if (!string.IsNullOrWhiteSpace(card.Driver))
        {
            return $"{card.Driver} {GetPciKey(card)}";
        }

        return card.VendorId is null || card.DeviceId is null
            ? card.CardName
            : GetPciKey(card).ToString();
    }

    private static string BuildAdapterDescription(CardRecord card)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(card.Driver))
        {
            parts.Add($"{card.Driver} driver");
        }

        if (!string.IsNullOrWhiteSpace(card.PciSlotName))
        {
            parts.Add($"PCI {card.PciSlotName}");
        }

        return parts.Count == 0 ? card.CardName : string.Join(" · ", parts);
    }

    private static string? BuildVideoModeDescription(ConnectorRecord? connector)
    {
        if (connector is null || connector.Width <= 0)
        {
            return null;
        }

        return connector.RefreshHz > 0d
            ? $"{connector.Width} x {connector.Height} @ {connector.RefreshHz.ToString("N0", CultureInfo.InvariantCulture)} Hz"
            : $"{connector.Width} x {connector.Height}";
    }

    /// <summary>
    /// Video memory in bytes. amdgpu and the Intel drivers expose totals in sysfs; NVIDIA's proprietary driver
    /// does not, so its cards report 0 ("Unknown") unless NVML is available elsewhere.
    /// </summary>
    private static ulong ReadVideoMemoryBytes(CardRecord card)
    {
        foreach (var attribute in (string[])["mem_info_vram_total", "mem_info_vis_vram_total"])
        {
            var value = DrmSysfs.ReadInt64Attribute(Path.Combine(card.DevicePath, attribute));
            if (value is > 0)
            {
                return (ulong)value.Value;
            }
        }

        return 0;
    }

    /// <summary>
    /// Kernel module version, e.g. /sys/module/nvidia/version, falling back to the driver's own DRM version.
    /// </summary>
    /// <remarks>
    /// The module attribute only exists for out-of-tree modules, so on a machine running any in-tree driver
    /// (i915, amdgpu, nouveau — i.e. most machines) it is absent and this used to report nothing. The DRM
    /// VERSION ioctl answers for every driver, so it backs the sysfs path rather than replacing it: where a
    /// module version exists it is the more specific answer (NVIDIA's "580.82.09" beats its DRM triple).
    /// </remarks>
    private string? ReadDriverVersion(CardRecord card)
    {
        if (!string.IsNullOrWhiteSpace(card.Driver))
        {
            // The module version path is outside the DRM tree, so it is derived from the sysfs root of the card.
            var moduleVersion = DrmSysfs.ReadAttribute(Path.Combine("/sys", "module", card.Driver, "version"));
            if (!string.IsNullOrWhiteSpace(moduleVersion))
            {
                return moduleVersion;
            }
        }

        // "card0" -> 0; the DRM device node index matches the sysfs card index.
        return int.TryParse(card.CardName.AsSpan(4), out var cardIndex)
            ? DrmModeReader.ReadDriverVersion(cardIndex, logger)
            : null;
    }

    /// <summary>
    /// Asks the kernel what each connector is CURRENTLY scanning out.
    /// </summary>
    /// <remarks>
    /// sysfs and EDID only describe capability. The preferred timing of a 165 Hz panel commonly reads 60 Hz
    /// because its high-refresh modes live in an EDID extension block, so "current refresh rate" has to come
    /// from the mode-setting state — which is what the DRM ioctls expose, headless. Optional: a machine with
    /// no /dev/dri node simply keeps the EDID-derived values.
    /// </remarks>
    private IReadOnlyDictionary<string, DrmActiveMode> ReadActiveModes(CardRecord card)
    {
        // "card0" -> 0; the DRM device node index matches the sysfs card index.
        if (!int.TryParse(card.CardName.AsSpan(4), out var cardIndex))
        {
            return new Dictionary<string, DrmActiveMode>();
        }

        return DrmModeReader.ReadActiveModes(cardIndex, logger);
    }

    private IReadOnlyList<ConnectorRecord> ReadConnectors(CardRecord card, IReadOnlyDictionary<string, DrmActiveMode> activeModes)
    {
        List<ConnectorRecord> records = [];

        foreach (var connectorDirectory in _sysfs.EnumerateConnectorNames(card.CardName))
        {
            if (!DrmConnectorName.TryParse(connectorDirectory, out var name))
            {
                continue;
            }

            var path = _sysfs.GetConnectorPath(connectorDirectory);
            var status = DrmConnectorStatusParser.Parse(DrmSysfs.ReadAttribute(Path.Combine(path, "status")));

            // A disconnected port is not a display. Listing every unused HDMI socket as a monitor would make
            // the page useless on a docked machine.
            if (status != DrmConnectorStatus.Connected)
            {
                continue;
            }

            EdidDisplayInfo? edid = null;
            var edidBytes = DrmSysfs.ReadEdid(Path.Combine(path, "edid"));
            if (edidBytes is not null)
            {
                EdidParser.TryParse(edidBytes, out edid);
            }

            var modes = DrmMode.ParseList(DrmSysfs.ReadAttribute(Path.Combine(path, "modes")));

            records.Add(new ConnectorRecord
            {
                Name = name,
                Status = status,
                Edid = edid,
                // The kernel lists the preferred mode first.
                PreferredMode = modes.Count > 0 ? modes[0] : null,
                ActiveMode = activeModes.GetValueOrDefault(name.DisplayName),
                Enabled = string.Equals(DrmSysfs.ReadAttribute(Path.Combine(path, "enabled")), "enabled", StringComparison.OrdinalIgnoreCase),
            });
        }

        return records;
    }

    private static HardwareInfoMonitor BuildMonitor(ConnectorRecord connector, string controllerName)
    {
        var edid = connector.Edid;
        var width = connector.Width;
        var height = connector.Height;

        var (dpiX, dpiY) = ComputePhysicalDpi(edid, width, height);

        return new HardwareInfoMonitor(
            Active: connector.Enabled,
            Caption: connector.Name.DisplayName,
            Description: BuildMonitorDescription(connector),
            ManufacturerName: string.IsNullOrWhiteSpace(edid?.ManufacturerId) ? null : edid.ManufacturerId,
            MonitorManufacturer: string.IsNullOrWhiteSpace(edid?.ManufacturerId) ? null : edid.ManufacturerId,
            MonitorType: connector.Name.IsInternalPanel ? "Internal panel" : connector.Name.ConnectorType,
            Name: edid?.MonitorName ?? connector.Name.DisplayName,
            PixelsPerXLogicalInch: dpiX,
            PixelsPerYLogicalInch: dpiY,
            ProductCodeId: edid is null ? null : edid.ProductCode.ToString("X4", CultureInfo.InvariantCulture),
            SerialNumberId: BuildSerialNumber(edid),
            UserFriendlyName: edid?.DisplayName,
            WeekOfManufacture: (ushort)(edid?.WeekOfManufacture ?? 0),
            YearOfManufacture: (ushort)(edid?.YearOfManufacture ?? 0),
            CurrentHorizontalResolution: (uint)width,
            CurrentVerticalResolution: (uint)height,
            CurrentRefreshRate: (uint)Math.Round(connector.RefreshHz, MidpointRounding.AwayFromZero),
            LinkedVideoControllerDisplayNames: [controllerName]);
    }

    private static string BuildMonitorDescription(ConnectorRecord connector)
    {
        var edid = connector.Edid;
        List<string> parts = [$"Connector {connector.Name.DisplayName}"];

        if (edid?.WidthCentimeters is > 0 && edid.HeightCentimeters > 0)
        {
            var diagonalInches = Math.Sqrt(
                (edid.WidthCentimeters * (double)edid.WidthCentimeters) +
                (edid.HeightCentimeters * (double)edid.HeightCentimeters)) / 2.54d;
            parts.Add($"{edid.WidthCentimeters} x {edid.HeightCentimeters} cm (~{diagonalInches:N0}\")");
        }

        if (edid is { ChecksumValid: false })
        {
            parts.Add("EDID checksum mismatch");
        }

        return string.Join(" · ", parts);
    }

    private static string? BuildSerialNumber(EdidDisplayInfo? edid)
    {
        if (edid is null)
        {
            return null;
        }

        // Panels report either an ASCII serial descriptor or a binary one; prefer the readable form.
        if (!string.IsNullOrWhiteSpace(edid.SerialNumberText))
        {
            return edid.SerialNumberText;
        }

        return edid.SerialNumber == 0 ? null : edid.SerialNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// PHYSICAL pixel density from the EDID size, which is the only density a headless service can know.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same quantity Windows reports in these fields: there it is the logical (scaled)
    /// DPI of the desktop, which requires a session. Physical density is the honest headless answer and is
    /// what the panel actually is — a 2560x1600 16" panel reads ~189 DPI here.
    /// </remarks>
    private static (uint DpiX, uint DpiY) ComputePhysicalDpi(EdidDisplayInfo? edid, int width, int height)
    {
        if (edid is null || width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        // Millimetres from the detailed timing are finer grained than the base block's whole centimetres.
        var widthMm = edid.PreferredTiming?.WidthMillimeters ?? 0;
        var heightMm = edid.PreferredTiming?.HeightMillimeters ?? 0;

        if (widthMm <= 0 || heightMm <= 0)
        {
            widthMm = edid.WidthCentimeters * 10;
            heightMm = edid.HeightCentimeters * 10;
        }

        if (widthMm <= 0 || heightMm <= 0)
        {
            return (0, 0);
        }

        const double MillimetersPerInch = 25.4d;
        var dpiX = width / (widthMm / MillimetersPerInch);
        var dpiY = height / (heightMm / MillimetersPerInch);

        return ((uint)Math.Round(dpiX, MidpointRounding.AwayFromZero), (uint)Math.Round(dpiY, MidpointRounding.AwayFromZero));
    }

    private sealed record CardRecord
    {
        public required string CardName { get; init; }

        public required string DevicePath { get; init; }

        public string? Driver { get; init; }

        public string? PciSlotName { get; init; }

        public ushort? VendorId { get; init; }

        public ushort? DeviceId { get; init; }
    }

    private sealed record ConnectorRecord
    {
        public required DrmConnectorName Name { get; init; }

        public required DrmConnectorStatus Status { get; init; }

        public EdidDisplayInfo? Edid { get; init; }

        public DrmMode? PreferredMode { get; init; }

        /// <summary>What the connector is scanning out right now, when the kernel would tell us.</summary>
        public DrmActiveMode? ActiveMode { get; init; }

        public required bool Enabled { get; init; }

        /// <summary>Live mode first, then the panel's preferred timing — capability is the fallback, not the answer.</summary>
        public int Width => ActiveMode?.Width ?? PreferredMode?.Width ?? Edid?.PreferredTiming?.HorizontalActive ?? 0;

        public int Height => ActiveMode?.Height ?? PreferredMode?.Height ?? Edid?.PreferredTiming?.VerticalActive ?? 0;

        public double RefreshHz => ActiveMode?.RefreshHz ?? Edid?.PreferredTiming?.RefreshHz ?? 0d;
    }
}
