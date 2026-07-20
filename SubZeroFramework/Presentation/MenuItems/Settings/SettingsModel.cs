using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using Material.Icons;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Navigation;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

/// <summary>
/// Page model for the Settings shell: the sub-navigation card (Service / Display units / Startup &amp;
/// alerts / Licenses / About) with a "Service reachable" footer. The section bodies are separate
/// ViewMap-registered ViewModels (<c>SettingsServiceSectionModel</c> etc.) that nested-region navigation
/// constructs with their own dependencies — this model only drives which section shows.
/// </summary>
public partial class SettingsModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];

    public SettingsModel(
        IFrameworkStatusClient frameworkStatusClient,
        NavigationGuardRegistry navigationGuardRegistry,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(frameworkStatusClient);
        ArgumentNullException.ThrowIfNull(navigationGuardRegistry);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        GuardRegistry = navigationGuardRegistry;

        Sections =
        [
            new SettingsSectionRailItemModel(0, "Service", "Lifecycle & recovery", MaterialIconKind.Wrench),
            new SettingsSectionRailItemModel(1, "Display units", "Temperature, power, speed", MaterialIconKind.Ruler),
            new SettingsSectionRailItemModel(2, "Startup & alerts", "Launch behavior", MaterialIconKind.RocketLaunchOutline),
            new SettingsSectionRailItemModel(3, "Licenses", "Open-source notices", MaterialIconKind.ScaleBalance),
            new SettingsSectionRailItemModel(4, "About", "Version & links", MaterialIconKind.InformationOutline),
        ];
        Sections[0].IsSelected = true;

        SelectSectionCommand = new RelayCommand<SettingsSectionRailItemModel>(SelectSection);

        frameworkStatusClient
            .WatchStatus()
            .Select(status => Observable.FromAsync(_ => dispatcherQueue.EnqueueAsync(() => ApplyStatus(status))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    /// <summary>The shell's unsaved-changes registry (the page registers the active section as the "Settings" guard).</summary>
    public NavigationGuardRegistry GuardRegistry { get; }

    // ----- Sub-navigation -----

    public IReadOnlyList<SettingsSectionRailItemModel> Sections { get; }

    [ObservableProperty]
    public partial int SelectedSectionIndex { get; set; }

    public IRelayCommand<SettingsSectionRailItemModel> SelectSectionCommand { get; }

    private void SelectSection(SettingsSectionRailItemModel? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var item in Sections)
        {
            item.IsSelected = item.Index == section.Index;
        }

        SelectedSectionIndex = section.Index;
    }

    // ----- Service reachability footer -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterText))]
    [NotifyPropertyChangedFor(nameof(FooterBrush))]
    [NotifyPropertyChangedFor(nameof(FooterIconKind))]
    public partial InfoBarSeverity ServiceStateSeverity { get; set; } = InfoBarSeverity.Informational;

    public string FooterText => ServiceStateSeverity switch
    {
        InfoBarSeverity.Success => "Service reachable",
        InfoBarSeverity.Warning => "Service degraded",
        InfoBarSeverity.Error => "Service unreachable",
        _ => "Checking service",
    };

    // Brushes are derived (never stored): creating a SolidColorBrush is only legal on the UI thread, and
    // computed getters evaluate at binding time instead.
    public Brush FooterBrush => ServiceStateSeverity switch
    {
        InfoBarSeverity.Success => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        InfoBarSeverity.Warning => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        InfoBarSeverity.Error => AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.SeverityCriticalColor),
        _ => AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusInfoColor),
    };

    public MaterialIconKind FooterIconKind => ServiceStateSeverity switch
    {
        InfoBarSeverity.Success => MaterialIconKind.CheckDecagram,
        InfoBarSeverity.Warning => MaterialIconKind.AlertOutline,
        InfoBarSeverity.Error => MaterialIconKind.AlertOctagonOutline,
        _ => MaterialIconKind.InformationOutline,
    };

    private void ApplyStatus(FrameworkSystemStatus status)
        => ServiceStateSeverity =
            !status.IsGrpcActive || !status.IsLibraryAvailable ? InfoBarSeverity.Error
            : status.RequiresElevation || !string.IsNullOrEmpty(status.LastError) ? InfoBarSeverity.Warning
            : InfoBarSeverity.Success;

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
