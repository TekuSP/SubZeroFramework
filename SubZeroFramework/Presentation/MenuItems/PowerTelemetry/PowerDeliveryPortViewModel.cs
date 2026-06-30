using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

/// <summary>Display wrapper for one USB-C Power Delivery port, updated in place so the list does not flicker.</summary>
public partial class PowerDeliveryPortViewModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public PowerDeliveryPortViewModel(IUnitFormattingService unitFormattingService, PowerDeliveryPortStatus status)
    {
        _unitFormattingService = unitFormattingService;
        SlotIndex = status.SlotIndex;
        Update(status);
    }

    public int SlotIndex { get; }

    public string Title => $"USB-C {SlotIndex + 1}";

    /// <summary>Negotiated power, or a "no contract" note when there is no active PD contract.</summary>
    [ObservableProperty]
    public partial string PowerText { get; private set; } = string.Empty;

    /// <summary>Connection state + roles + polarity, e.g. "Attached · Sink · Device · CC1".</summary>
    [ObservableProperty]
    public partial string DetailLine { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility ActiveBadgeVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility ContractBadgeVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EprBadgeVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility VconnBadgeVisibility { get; private set; } = Visibility.Collapsed;

    public void Update(PowerDeliveryPortStatus status)
    {
        PowerText = status.HasContract
            ? $"{_unitFormattingService.FormatVoltage(status.VoltageVolts)} · {_unitFormattingService.FormatCurrent(status.CurrentAmperes)}"
            : "No PD contract";

        var connection = string.IsNullOrWhiteSpace(status.CState) ? "Unknown" : status.CState;
        DetailLine = $"{connection} · {status.PowerRole} · {status.DataRole} · {status.CcPolarity}";

        ActiveBadgeVisibility = status.IsActivePort ? Visibility.Visible : Visibility.Collapsed;
        ContractBadgeVisibility = status.HasContract ? Visibility.Visible : Visibility.Collapsed;
        EprBadgeVisibility = status.IsEprActive ? Visibility.Visible : Visibility.Collapsed;
        VconnBadgeVisibility = status.IsVconnActive ? Visibility.Visible : Visibility.Collapsed;
    }
}
