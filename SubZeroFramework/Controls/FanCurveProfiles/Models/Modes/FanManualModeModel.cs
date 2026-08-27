using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

/// <summary>
/// Body ViewModel for the Manual mode route: the fixed-duty slider + quick presets. Extends the shared
/// gauge/description with the duty value (two-way back to the coordinator, which debounces the apply) and
/// the preset toggles.
/// </summary>
public sealed partial class FanManualModeModel : FanModeModelBase
{
    public FanManualModeModel(FanCoordinatorAccessor coordinatorAccessor) : base(coordinatorAccessor)
    {
        RefreshDerivedState();
    }

    /// <summary>
    /// The slider's thumb, in the user's ratio unit. Two-way: writes flow to the coordinator, which converts
    /// back to canonical percent and debounces the apply. Mirrored (not a computed pass-through) so the
    /// assignment itself raises PropertyChanged.
    /// </summary>
    [ObservableProperty]
    public partial double ManualDutyDisplayValue { get; set; }

    partial void OnManualDutyDisplayValueChanged(double value) => Page.ManualDutyDisplayValue = value;

    // Slider bounds in the display unit, so a non-percent ratio preference gets a scale in its own unit.
    [ObservableProperty]
    public partial double ManualDutyDisplayMinimum { get; private set; }

    [ObservableProperty]
    public partial double ManualDutyDisplayMaximum { get; private set; }

    [ObservableProperty]
    public partial double ManualDutyDisplayStep { get; private set; }

    [ObservableProperty]
    public partial double ManualDutyDisplayTickFrequency { get; private set; }

    /// <summary>The big duty readout beside the slider, in canonical percent — UnitFormatConverter formats it.</summary>
    [ObservableProperty]
    public partial double ManualDutyPercent { get; private set; }

    [ObservableProperty]
    public partial bool IsPreset25 { get; private set; }

    [ObservableProperty]
    public partial bool IsPreset50 { get; private set; }

    [ObservableProperty]
    public partial bool IsPreset80 { get; private set; }

    [ObservableProperty]
    public partial bool IsPreset100 { get; private set; }

    public IRelayCommand<string?> SetManualPresetCommand => Page.SetManualPresetCommand;

    protected override void RefreshDerivedState()
    {
        base.RefreshDerivedState();

        // Assigning an unchanged value is a no-op, so the write-back in OnManualDutyDisplayValueChanged
        // settles rather than ping-ponging with the coordinator.
        ManualDutyDisplayValue = Page.ManualDutyDisplayValue;
        ManualDutyDisplayMinimum = Page.ManualDutyDisplayMinimum;
        ManualDutyDisplayMaximum = Page.ManualDutyDisplayMaximum;
        ManualDutyDisplayStep = Page.ManualDutyDisplayStep;
        ManualDutyDisplayTickFrequency = Page.ManualDutyDisplayTickFrequency;
        ManualDutyPercent = Page.ManualDutyPercent;
        IsPreset25 = Page.IsPreset25;
        IsPreset50 = Page.IsPreset50;
        IsPreset80 = Page.IsPreset80;
        IsPreset100 = Page.IsPreset100;
    }

    protected override bool AffectsDerivedState(string propertyName) => propertyName switch
    {
        nameof(FanCurveProfilesModel.ManualDutyPercent) => true,
        nameof(FanCurveProfilesModel.ManualDutyDisplayValue) => true,
        nameof(FanCurveProfilesModel.IsPreset25) => true,
        nameof(FanCurveProfilesModel.IsPreset50) => true,
        nameof(FanCurveProfilesModel.IsPreset80) => true,
        nameof(FanCurveProfilesModel.IsPreset100) => true,
        _ => base.AffectsDerivedState(propertyName),
    };
}
