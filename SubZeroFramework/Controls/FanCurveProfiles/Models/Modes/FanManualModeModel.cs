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
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualDutyPercent))]
    [NotifyPropertyChangedFor(nameof(ManualDutyDisplay))]
    [NotifyPropertyChangedFor(nameof(IsPreset25))]
    [NotifyPropertyChangedFor(nameof(IsPreset50))]
    [NotifyPropertyChangedFor(nameof(IsPreset80))]
    [NotifyPropertyChangedFor(nameof(IsPreset100))]
    private partial int ManualRefreshVersion { get; set; }

    /// <summary>Two-way: writes flow to the coordinator, which debounces and applies the duty.</summary>
    public double ManualDutyPercent
    {
        get => Page.ManualDutyPercent;
        set => Page.ManualDutyPercent = value;
    }

    public string ManualDutyDisplay => Page.ManualDutyDisplay;

    public bool IsPreset25 => Page.IsPreset25;

    public bool IsPreset50 => Page.IsPreset50;

    public bool IsPreset80 => Page.IsPreset80;

    public bool IsPreset100 => Page.IsPreset100;

    public IRelayCommand<string?> SetManualPresetCommand => Page.SetManualPresetCommand;

    protected override void OnPageChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(FanCurveProfilesModel.ManualDutyPercent):
            case nameof(FanCurveProfilesModel.ManualDutyDisplay):
            case nameof(FanCurveProfilesModel.IsPreset25):
            case nameof(FanCurveProfilesModel.IsPreset50):
            case nameof(FanCurveProfilesModel.IsPreset80):
            case nameof(FanCurveProfilesModel.IsPreset100):
                ManualRefreshVersion++;
                break;
        }
    }
}
