namespace SubZeroFramework.Services;

/// <summary>Client-side USB-C Power Delivery state for one expansion-card slot (enum values kept as display strings).</summary>
public sealed record PowerDeliveryPortStatus
{
    public required int SlotIndex { get; init; }

    public required bool IsPresent { get; init; }

    public required bool IsActivePort { get; init; }

    public required bool HasContract { get; init; }

    public required string CState { get; init; }

    public required string PowerRole { get; init; }

    public required string DataRole { get; init; }

    public required string CcPolarity { get; init; }

    public required double VoltageVolts { get; init; }

    public required double CurrentAmperes { get; init; }

    public required bool IsVconnActive { get; init; }

    public required bool IsEprActive { get; init; }

    public required bool IsEprSupported { get; init; }

    public required byte AltModeFlags { get; init; }

    /// <summary>Expansion card in the slot (FrameworkExpansionCardType name; "Unknown"/empty when none).</summary>
    public required string CardType { get; init; }

    /// <summary>Static USB-C data-lane capability of this slot (FrameworkUsbCDataLane name).</summary>
    public required string DataLane { get; init; }

    /// <summary>Static DisplayPort capability/version of this slot (FrameworkDisplayPortCapability name).</summary>
    public required string DisplayPortCapability { get; init; }

    /// <summary>Whether this slot supports USB-PD charging (false for power-limited slots).</summary>
    public required bool SupportsCharging { get; init; }

    /// <summary>Maximum charge power in watts (0 when not a charging slot, or undocumented).</summary>
    public required int MaxChargeWatts { get; init; }

    /// <summary>Whether the "higher power consumption" USB-A note applies to this slot.</summary>
    public required bool UsbAHighPower { get; init; }

    /// <summary>Whether a documented capability matrix covers this slot and platform.</summary>
    public required bool CapabilityDocumented { get; init; }

    /// <summary>Where the port lives: "Mainboard" (numbered slots) or "GraphicsModule" (the expansion-bay GPU port).</summary>
    public required string PortSource { get; init; }

    /// <summary>Physical position label (upstream framework-system: "Right Back", "Left Middle", "Graphics module", …);
    /// empty on platforms with no documented mapping.</summary>
    public required string PortPosition { get; init; }

    /// <summary>Whether the port is on the left side of the chassis (upstream: PD ports 2 &amp; 3 are left).</summary>
    public required bool PortIsLeft { get; init; }
}

/// <summary>Streams the live USB-C Power Delivery port state from the service.</summary>
public interface IPowerDeliveryClient
{
    /// <summary>A shared, reconnecting stream of the current set of reported PD ports.</summary>
    IObservable<IReadOnlyList<PowerDeliveryPortStatus>> WatchPorts();
}
