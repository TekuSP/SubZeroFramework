using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesGraphicsCardGroupModel : ObservableObject
{
    private readonly ObservableCollection<DeviceCapabilitiesMonitorCardModel> _monitorCards = [];

    public DeviceCapabilitiesGraphicsCardGroupModel(bool isUnknownGraphicsCard, DeviceCapabilitiesVideoControllerCardModel? videoController = null)
    {
        MonitorCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel>(_monitorCards);
        IsUnknownGraphicsCard = isUnknownGraphicsCard;
        VideoController = videoController;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(KnownGraphicsCardVisibility))]
    [NotifyPropertyChangedFor(nameof(UnknownGraphicsCardVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyMonitorMessage))]
    public partial bool IsUnknownGraphicsCard { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial DeviceCapabilitiesVideoControllerCardModel? VideoController { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(HasMonitorCardsVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyMonitorMessageVisibility))]
    private partial int MonitorRevision { get; set; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards { get; }

    public string DisplayName => IsUnknownGraphicsCard
        ? "Unknown graphics card"
        : VideoController?.Name ?? "Unknown graphics card";

    public string MonitorCountDisplay => MonitorCards.Count.ToString("N0");

    public Visibility KnownGraphicsCardVisibility => IsUnknownGraphicsCard
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility UnknownGraphicsCardVisibility => IsUnknownGraphicsCard
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility HasMonitorCardsVisibility => MonitorCards.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyMonitorMessageVisibility => MonitorCards.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string EmptyMonitorMessage => IsUnknownGraphicsCard
        ? "These monitors did not report a linked graphics card."
        : "No monitors are currently associated with this graphics card.";

    public void SynchronizeMonitorCards(IReadOnlyList<DeviceCapabilitiesMonitorCardModel> monitorCards)
    {
        for (var index = 0; index < monitorCards.Count; index++)
        {
            var monitorCard = monitorCards[index];
            if (index < _monitorCards.Count)
            {
                if (ReferenceEquals(_monitorCards[index], monitorCard))
                {
                    continue;
                }

                _monitorCards[index] = monitorCard;
                continue;
            }

            _monitorCards.Add(monitorCard);
        }

        while (_monitorCards.Count > monitorCards.Count)
        {
            _monitorCards.RemoveAt(_monitorCards.Count - 1);
        }

        MonitorRevision++;
    }
}
