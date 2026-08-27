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
/// gauge + description the body needs, mirrored as STORED properties that
/// <see cref="RefreshDerivedState"/> reassigns when the coordinator reports a relevant change. Assignment
/// raises PropertyChanged only for values that actually changed, so nothing re-renders needlessly.
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
        RefreshDerivedState();
    }

    /// <summary>
    /// Unsubscribe from the coordinator. Called from the view's Unloaded handler.
    /// </summary>
    /// <remarks>
    /// Virtual because this — not <see cref="Dispose"/> — is the teardown the views actually call: a
    /// navigation-resolved mode model is never disposed, so a derived body that subscribes to anything else
    /// must release it here or leak it for the life of the app.
    /// </remarks>
    public virtual void Detach()
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
    public partial FanCardModel? SelectedFan { get; private set; }

    [ObservableProperty]
    public partial string ModeDescriptionTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ModeDescriptionText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ModeTargetText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double[] ModeGaugeTargetValues { get; private set; } = [];

    [ObservableProperty]
    public partial double[] ModeGaugeTargetRemaining { get; private set; } = [];

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Visibility ModeGaugeTargetVisibility { get; private set; }

    [ObservableProperty]
    public partial bool CanSelectMode { get; private set; }

    // Paints are built once by the coordinator and never swapped, so these stay plain pass-throughs.
    public SolidColorPaint ModeGaugeTargetPaint => Page.ModeGaugeTargetPaint;

    public SolidColorPaint ModeGaugeTargetTrackPaint => Page.ModeGaugeTargetTrackPaint;

    /// <summary>
    /// Reassigns the shared gauge/description projections from the coordinator. Override to mirror the
    /// mode-specific ones too, and call <c>base</c>.
    /// </summary>
    protected virtual void RefreshDerivedState()
    {
        SelectedFan = Page.SelectedFan;
        ModeDescriptionTitle = Page.ModeDescriptionTitle;
        ModeDescriptionText = Page.ModeDescriptionText;
        ModeTargetText = Page.ModeTargetText;
        ModeGaugeTargetValues = Page.ModeGaugeTargetValues;
        ModeGaugeTargetRemaining = Page.ModeGaugeTargetRemaining;
        ModeGaugeTargetVisibility = Page.ModeGaugeTargetVisibility;
        CanSelectMode = Page.CanSelectMode;
    }

    /// <summary>
    /// Whether a coordinator change touches anything this body mirrors. Override to add the mode-specific
    /// property names, falling back to <c>base</c>.
    /// </summary>
    protected virtual bool AffectsDerivedState(string propertyName) => propertyName switch
    {
        nameof(FanCurveProfilesModel.SelectedFan) => true,
        nameof(FanCurveProfilesModel.ModeDescriptionTitle) => true,
        nameof(FanCurveProfilesModel.ModeDescriptionText) => true,
        nameof(FanCurveProfilesModel.ModeTargetText) => true,
        nameof(FanCurveProfilesModel.ModeGaugeTargetValues) => true,
        nameof(FanCurveProfilesModel.ModeGaugeTargetRemaining) => true,
        nameof(FanCurveProfilesModel.ModeGaugeTargetVisibility) => true,
        nameof(FanCurveProfilesModel.CanSelectMode) => true,
        _ => false,
    };

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the coordinator's "everything changed" signal, raised when the display units
        // change — the mirrored text and the gauge bounds both move with it.
        if (string.IsNullOrEmpty(e.PropertyName) || AffectsDerivedState(e.PropertyName))
        {
            RefreshDerivedState();
        }
    }

    public virtual void Dispose() => Detach();
}
