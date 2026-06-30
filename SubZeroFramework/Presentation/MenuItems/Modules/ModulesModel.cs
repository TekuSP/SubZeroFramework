using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Modules;

/// <summary>
/// View-model for the Modules page. The page renders a physical module/port map whose layout is chosen by the
/// chassis <see cref="FrameworkPlatformFamily"/> (FW16 / FW12-13 / Desktop), plus a per-slot detail card. This is
/// currently a scaffold: it tracks live status so the page can pick its layout once the per-platform maps land.
/// </summary>
public partial class ModulesModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];

    public ModulesModel(
        IFrameworkStatusClient frameworkStatusClient,
        SynchronizationContext synchronizationContext)
    {
        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(synchronizationContext)
            .Subscribe(status => LastStatus = status)
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformFamilyDisplay))]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    /// <summary>Detected chassis family, used as the placeholder caption until the per-platform maps exist.</summary>
    public string PlatformFamilyDisplay =>
        LastStatus?.PlatformFamily?.ToString() ?? "Detecting device…";

    public void Dispose() => _subscriptions.Dispose();
}
