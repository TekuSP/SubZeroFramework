using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

/// <summary>One USB-C Power Delivery port card. Roles are surfaced as plain-language pills (never raw
/// Sink/Source/DFP/UFP), per the design.</summary>
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

    /// <summary>Port title — the physical position label ("Right Back", "Graphics module", …) when framework-system
    /// documents one for the platform, otherwise a plain "USB-C N".</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>Compact badges shown inline with the title (Charging / Extended power / Cable power).</summary>
    public ObservableCollection<PowerDeliveryPortPill> Badges { get; } = [];

    /// <summary>Plain-language role pills shown on the row below the title.</summary>
    public ObservableCollection<PowerDeliveryPortPill> RolePills { get; } = [];

    /// <summary>Negotiated power "V · A", or a "no contract" note.</summary>
    [ObservableProperty]
    public partial string PowerText { get; private set; } = string.Empty;

    /// <summary>Negotiated wattage pill text (e.g. "240 W"); empty when there is no contract.</summary>
    [ObservableProperty]
    public partial string WattText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility WattVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>Plain-language alt-mode line (e.g. "DisplayPort", "No alt-mode").</summary>
    [ObservableProperty]
    public partial string AltModeText { get; private set; } = "No alt-mode";

    /// <summary>The active charging port gets an accent outline.</summary>
    [ObservableProperty]
    public partial Brush CardBorderBrush { get; private set; } = AppThemeBrushes.Get("CardBorderBrush", AppThemeBrushes.BrandDisabledColor);

    [ObservableProperty]
    public partial Thickness CardBorderThickness { get; private set; } = new(1);

    /// <summary>Port-glyph chip fill — accent when this port is actively charging, recessed dark otherwise.</summary>
    [ObservableProperty]
    public partial Brush PortIconBackground { get; private set; } = AppThemeBrushes.Get("SurfaceWellBrush", AppThemeBrushes.BrandDisabledColor);

    [ObservableProperty]
    public partial Brush PortIconForeground { get; private set; } = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

    /// <summary>"Card: &lt;type&gt;" line — the expansion card detected in this slot, or "No card in slot".</summary>
    [ObservableProperty]
    public partial string CardTypeText { get; private set; } = "No card in slot";

    /// <summary>Static slot capability as individual pills (data lane / DisplayPort / charging / USB-A note).</summary>
    public ObservableCollection<PowerDeliveryPortPill> CapabilityPills { get; } = [];

    public void Update(PowerDeliveryPortStatus status)
    {
        // An "Invalid" CC state means the EC could not resolve the port; its other PD fields (voltage, EPR/VCONN,
        // alt-mode) are then unreliable, so we surface only "Unknown / error" and suppress the rest.
        var isInvalid = string.Equals(status.CState, "Invalid", System.StringComparison.OrdinalIgnoreCase);

        Title = string.IsNullOrEmpty(status.PortPosition) ? $"USB-C {SlotIndex + 1}" : status.PortPosition;

        // A slot the board documents as non-charging (e.g. FW16 slots 3 & 6, 900 mA) is a data-only USB port, not
        // a PD port — surface it by capability, without the PD-contract text or role pills.
        var isDataOnly = status.CapabilityDocumented && !status.SupportsCharging;

        PowerText = status.HasContract && !isInvalid
            ? $"{_unitFormattingService.FormatVoltage(status.VoltageVolts)} · {_unitFormattingService.FormatCurrent(status.CurrentAmperes)}"
            : isDataOnly ? string.Empty : "No PD contract";

        CapabilityPills.Clear();
        foreach (var pill in BuildCapabilityPills(status))
        {
            CapabilityPills.Add(pill);
        }

        var watts = status.VoltageVolts * status.CurrentAmperes;
        WattText = status.HasContract && !isInvalid && watts > 0d ? $"{System.Math.Round(watts):0} W" : string.Empty;
        WattVisibility = WattText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        AltModeText = status.AltModeFlags != 0 && !isInvalid ? "DisplayPort" : "No alt-mode";

        CardTypeText = status.IsPresent ? $"Card: {FriendlyCardType(status.CardType)}" : "No card in slot";

        var isActive = status.IsActivePort && status.HasContract;
        CardBorderBrush = isActive
            ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
            : AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.BrandDisabledColor);
        CardBorderThickness = isActive ? new Thickness(1.5) : new Thickness(1);
        PortIconBackground = isActive
            ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
            : AppThemeBrushes.Get("SurfaceWellBrush", AppThemeBrushes.BrandDisabledColor);
        PortIconForeground = isActive
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

        Badges.Clear();
        foreach (var badge in BuildBadges(status))
        {
            Badges.Add(badge);
        }

        RolePills.Clear();
        foreach (var pill in BuildRolePills(status))
        {
            RolePills.Add(pill);
        }
    }

    // Inline-with-title badges: charging state + extended/cable power flags (matches the design's `badges`).
    private static IEnumerable<PowerDeliveryPortPill> BuildBadges(PowerDeliveryPortStatus status)
    {
        // Invalid ports show only the "Unknown / error" role pill — their flag fields can't be trusted.
        if (string.Equals(status.CState, "Invalid", System.StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (status.IsActivePort && status.HasContract)
        {
            yield return PowerDeliveryPortPill.Accent("Charging");
        }

        if (status.IsEprActive)
        {
            yield return PowerDeliveryPortPill.Neutral("Extended power");
        }

        if (status.IsVconnActive)
        {
            yield return PowerDeliveryPortPill.Neutral("Cable power");
        }
    }

    // Role pills below the title: connection · power direction · data role (matches the design's `rolePills`).
    private static IEnumerable<PowerDeliveryPortPill> BuildRolePills(PowerDeliveryPortStatus status)
    {
        // Data-only slots (documented non-charging) aren't PD ports — their capability line carries the info.
        if (status.CapabilityDocumented && !status.SupportsCharging)
        {
            yield break;
        }

        if (string.Equals(status.CState, "Nothing", System.StringComparison.OrdinalIgnoreCase))
        {
            yield return PowerDeliveryPortPill.Muted("Nothing connected");
            yield break;
        }

        if (string.Equals(status.CState, "Invalid", System.StringComparison.OrdinalIgnoreCase))
        {
            yield return PowerDeliveryPortPill.Danger("Unknown / error");
            yield break;
        }

        yield return PowerDeliveryPortPill.Neutral(IsSource(status.CState) ? "Charger attached" : "Device attached");

        // Power direction. The headline "Charging" badge already calls out the active charging port, so this pill
        // stays purely about flow direction (the laptop receives power on the charging port) — no duplicate wording.
        if (IsSource(status.PowerRole))
        {
            yield return PowerDeliveryPortPill.Success("Providing power");
        }
        else
        {
            yield return PowerDeliveryPortPill.Neutral("Receiving power");
        }

        yield return PowerDeliveryPortPill.Neutral(IsHost(status.DataRole) ? "Host" : "Device");
    }

    // Build the static capability as individual pills (data lane / DisplayPort / charging / USB-A note).
    private static IEnumerable<PowerDeliveryPortPill> BuildCapabilityPills(PowerDeliveryPortStatus status)
    {
        if (!status.CapabilityDocumented)
        {
            yield break;
        }

        var lane = FormatDataLane(status.DataLane);
        if (lane.Length > 0)
        {
            yield return PowerDeliveryPortPill.Muted(lane);
        }

        var displayPort = FormatDisplayPort(status.DisplayPortCapability);
        if (displayPort.Length > 0)
        {
            yield return PowerDeliveryPortPill.Muted(displayPort);
        }

        if (status.SupportsCharging)
        {
            yield return PowerDeliveryPortPill.Muted(status.MaxChargeWatts > 0 ? $"{status.MaxChargeWatts} W" : "Charging");
        }
        else
        {
            yield return PowerDeliveryPortPill.Muted("No charging");
        }

        if (status.UsbAHighPower)
        {
            yield return PowerDeliveryPortPill.Muted("USB-A high power");
        }
    }

    private static string FormatDataLane(string dataLane) => dataLane switch
    {
        "Usb2" => "USB 2.0",
        "Usb32" => "USB 3.2",
        "Usb32Gen2x1" => "USB 3.2 Gen 2x1",
        "Usb32Gen2x2" => "USB 3.2 Gen 2x2",
        "Usb4" => "USB4",
        "Thunderbolt4" => "Thunderbolt 4",
        _ => string.Empty,
    };

    private static string FormatDisplayPort(string displayPort) => displayPort switch
    {
        "Dp14Hbr3" => "DP 1.4 HBR3",
        "Dp20" => "DP 2.0",
        "Dp20Uhbr10" => "DP 2.0 UHBR10",
        "Dp20Uhbr20" => "DP 2.0 UHBR20",
        "Dp21" => "DP 2.1",
        "Dp21Uhbr10" => "DP 2.1 UHBR10",
        "Dp21Uhbr20" => "DP 2.1 UHBR20",
        "Supported" => "DisplayPort",
        _ => string.Empty,
    };

    private static string FriendlyCardType(string cardType) => cardType switch
    {
        "DisplayPort" => "DisplayPort",
        "Hdmi" => "HDMI",
        "Audio" => "Audio",
        "UsbA" => "USB-A",
        "UsbC" => "USB-C",
        "Ethernet" => "Ethernet",
        "Ethernet10G" => "Ethernet 10G",
        "MicroSd" => "microSD",
        "Sd" => "SD",
        "Ssd" => "SSD",
        _ => "USB-C",
    };

    private static bool IsSource(string role) => role.Contains("Source", System.StringComparison.OrdinalIgnoreCase);

    private static bool IsHost(string role) => role.Contains("Dfp", System.StringComparison.OrdinalIgnoreCase) || role.Contains("Host", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>A plain-language status pill on a PD port card, carrying its own resolved brushes.</summary>
public sealed record PowerDeliveryPortPill(string Text, Brush Background, Brush Foreground, Brush Border)
{
    private static readonly Brush Transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <summary>Solid accent (charging) — blue fill, white text.</summary>
    public static PowerDeliveryPortPill Accent(string text) => new(
        text,
        AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor),
        new SolidColorBrush(Microsoft.UI.Colors.White),
        AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor));

    /// <summary>Neutral chip — faint fill, secondary text, hairline outline.</summary>
    public static PowerDeliveryPortPill Neutral(string text) => new(
        text,
        new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(22, 255, 255, 255)),
        AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor),
        AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.BrandDisabledColor));

    /// <summary>Muted chip — transparent fill, tertiary text, hairline outline ("Nothing connected").</summary>
    public static PowerDeliveryPortPill Muted(string text) => new(
        text,
        Transparent,
        AppThemeBrushes.Get("TextTertiaryBrush", AppThemeBrushes.TextSecondaryColor),
        AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.BrandDisabledColor));

    /// <summary>Success — faint green fill, green text ("Providing power").</summary>
    public static PowerDeliveryPortPill Success(string text)
    {
        var c = AppThemeBrushes.StatusSuccessColor;
        var fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(34, c.R, c.G, c.B));
        return new(text, fill, AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor), fill);
    }

    /// <summary>Danger — faint red fill, red text ("Unknown / error").</summary>
    public static PowerDeliveryPortPill Danger(string text)
    {
        var c = AppThemeBrushes.SeverityCriticalColor;
        var fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(34, c.R, c.G, c.B));
        return new(text, fill, AppThemeBrushes.Get("SeverityCriticalBrush", AppThemeBrushes.SeverityCriticalColor), fill);
    }
}
