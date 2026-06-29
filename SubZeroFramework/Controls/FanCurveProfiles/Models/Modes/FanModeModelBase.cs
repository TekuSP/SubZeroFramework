using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.SkiaSharpView.Painting;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

/// <summary>
/// Shared base for the per-mode body ViewModels (Auto / Manual / Max) resolved by the mode navigation
/// sub-region. Each is a thin slice over the shared <see cref="FanCurveProfilesModel"/> coordinator
/// (the same parent-projection pattern as <c>DeviceCapabilitiesCpuSectionModel</c>): it exposes only the
/// gauge + description the body needs and bumps <see cref="RefreshVersion"/> when the coordinator raises a
/// relevant change so the computed pass-throughs re-evaluate.
/// </summary>
public abstract partial class FanModeModelBase : ObservableObject, IDisposable
{
    private bool _attached;

    protected FanModeModelBase(FanCoordinatorAccessor coordinatorAccessor)
    {
        // Read the page-driven coordinator the displayed page published, NOT a DI-resolved one (Uno's nested
        // navigation would otherwise inject a separate, dead FanCurveProfilesModel). Captured once: the instance
        // is stable while this body is on screen, so Attach/Detach subscribe and unsubscribe the same object.
        Page = coordinatorAccessor.Current
            ?? throw new InvalidOperationException(
                "Fan Control coordinator was not published before a mode body was created.");
    }

    /// <summary>
    /// Subscribe to the (singleton) coordinator. Called from the view's Loaded handler so the subscription
    /// lives only while the body is on screen — navigation creates a fresh mode VM per switch, so subscribing
    /// in the ctor would leak handlers onto the immortal coordinator and progressively slow the app.
    /// </summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Page.PropertyChanged += OnPagePropertyChanged;
        RefreshVersion++;
    }

    /// <summary>Unsubscribe from the coordinator. Called from the view's Unloaded handler.</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        Page.PropertyChanged -= OnPagePropertyChanged;
    }

    /// <summary>The shared coordinator. Exposed for the Custom body which reuses the full curve editor.</summary>
    public FanCurveProfilesModel Page { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFan))]
    [NotifyPropertyChangedFor(nameof(ModeDescriptionTitle))]
    [NotifyPropertyChangedFor(nameof(ModeDescriptionText))]
    [NotifyPropertyChangedFor(nameof(ModeTargetText))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetValues))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetRemaining))]
    [NotifyPropertyChangedFor(nameof(ModeGaugeTargetVisibility))]
    [NotifyPropertyChangedFor(nameof(CanSelectMode))]
    private partial int RefreshVersion { get; set; }

    public FanCardModel? SelectedFan => Page.SelectedFan;

    public string ModeDescriptionTitle => Page.ModeDescriptionTitle;

    public string ModeDescriptionText => Page.ModeDescriptionText;

    public string ModeTargetText => Page.ModeTargetText;

    public double[] ModeGaugeTargetValues => Page.ModeGaugeTargetValues;

    public double[] ModeGaugeTargetRemaining => Page.ModeGaugeTargetRemaining;

    public Microsoft.UI.Xaml.Visibility ModeGaugeTargetVisibility => Page.ModeGaugeTargetVisibility;

    public SolidColorPaint ModeGaugeTargetPaint => Page.ModeGaugeTargetPaint;

    public SolidColorPaint ModeGaugeTargetTrackPaint => Page.ModeGaugeTargetTrackPaint;

    public bool CanSelectMode => Page.CanSelectMode;

    /// <summary>Override to also re-raise mode-specific pass-throughs; call <c>base</c>.</summary>
    protected virtual void OnPageChanged(string? propertyName)
    {
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FanCurveProfilesModel.SelectedFan):
            case nameof(FanCurveProfilesModel.ModeDescriptionTitle):
            case nameof(FanCurveProfilesModel.ModeDescriptionText):
            case nameof(FanCurveProfilesModel.ModeTargetText):
            case nameof(FanCurveProfilesModel.ModeGaugeTargetValues):
            case nameof(FanCurveProfilesModel.ModeGaugeTargetRemaining):
            case nameof(FanCurveProfilesModel.ModeGaugeTargetVisibility):
            case nameof(FanCurveProfilesModel.CanSelectMode):
                RefreshVersion++;
                break;
        }

        OnPageChanged(e.PropertyName);
    }

    public virtual void Dispose() => Detach();
}
