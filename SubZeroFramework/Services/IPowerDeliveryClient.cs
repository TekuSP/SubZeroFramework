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
}

/// <summary>Streams the live USB-C Power Delivery port state from the service.</summary>
public interface IPowerDeliveryClient
{
    /// <summary>A shared, reconnecting stream of the current set of reported PD ports.</summary>
    IObservable<IReadOnlyList<PowerDeliveryPortStatus>> WatchPorts();
}
