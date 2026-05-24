using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkDesktopFanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly ObservableCollection<FrameworkDesktopFanOptionModel> _supportedFanOptions = [];
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkDesktopFanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        SupportedFanOptions = new ReadOnlyObservableCollection<FrameworkDesktopFanOptionModel>(_supportedFanOptions);
    }

    [ObservableProperty]
    public partial string Platform { get; set; } = string.Empty;

    public ReadOnlyObservableCollection<FrameworkDesktopFanOptionModel> SupportedFanOptions { get; }

    public void UpdateFrom(FrameworkDesktopCoolingDetails details)
    {
        Platform = details.Platform;

        for (var index = 0; index < details.SupportedFanOptions.Length; index++)
        {
            var option = details.SupportedFanOptions[index];

            if (index < _supportedFanOptions.Count)
            {
                _supportedFanOptions[index].UpdateFrom(option);
                continue;
            }

            var optionModel = new FrameworkDesktopFanOptionModel(_unitFormattingService);
            optionModel.UpdateFrom(option);
            _supportedFanOptions.Add(optionModel);
        }

        while (_supportedFanOptions.Count > details.SupportedFanOptions.Length)
        {
            _supportedFanOptions.RemoveAt(_supportedFanOptions.Count - 1);
        }
    }

    public override void RefreshUnitFormatting()
    {
        foreach (var option in _supportedFanOptions)
        {
            option.RefreshUnitFormatting();
        }
    }
}
