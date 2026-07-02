using System.ComponentModel;

using FrameworkDotnet.Enums;

using SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.Modules;

/// <summary>
/// Modules page: a per-platform layout sub-region (<c>LayoutRegionHost</c>) that resolves the chassis map body
/// once the platform family is known. Navigation is deferred to a later UI tick so it never runs re-entrantly
/// during the page's own navigation (same pattern as <c>DeviceCapabilitiesPage</c>).
/// </summary>
public sealed partial class ModulesPage : Page, INotifyPropertyChanged
{
    private bool _syncQueued;
    private bool _layoutNavigated;

    public ModulesPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
        Loaded += (_, _) => QueueLayoutSync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Page exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation is required to push DataContext updates.")]
    public ModulesModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            if (field is not null)
            {
                field.PropertyChanged -= OnViewModelPropertyChanged;
            }

            field = value;

            if (field is not null)
            {
                field.PropertyChanged += OnViewModelPropertyChanged;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
            QueueLayoutSync();
        }
    } = default!;

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is ModulesModel model)
        {
            ViewModel = model;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModulesModel.LastStatus))
        {
            QueueLayoutSync();
        }
    }

    // Coalesce + defer: navigating the child region during the page's own navigation deadlocks the UI thread.
    private void QueueLayoutSync()
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncQueued = false;
            SyncLayoutRegion();
        });
    }

    private void SyncLayoutRegion()
    {
        if (_layoutNavigated || ViewModel?.LastStatus is not { PlatformFamily: { } family } status)
        {
            return;
        }

        if (family is not (FrameworkPlatformFamily.Framework12 or FrameworkPlatformFamily.Framework13
            or FrameworkPlatformFamily.Framework16 or FrameworkPlatformFamily.FrameworkDesktop))
        {
            return;
        }

        if (LayoutRegionHost.Navigator() is not { } navigator)
        {
            // Region not attached yet; a later Loaded/status change re-queues.
            return;
        }

        _layoutNavigated = true;
        _ = family switch
        {
            FrameworkPlatformFamily.Framework12 => navigator.NavigateViewModelAsync<ModulesFw12Model>(this),
            // The platform enum has no explicit "13 Pro"; the Intel Core Ultra boards route to the Pro view.
            FrameworkPlatformFamily.Framework13 when status.Platform
                is FrameworkPlatform.IntelCoreUltra1 or FrameworkPlatform.IntelCoreUltra3
                => navigator.NavigateViewModelAsync<ModulesFw13ProModel>(this),
            FrameworkPlatformFamily.Framework13 => navigator.NavigateViewModelAsync<ModulesFw13Model>(this),
            FrameworkPlatformFamily.FrameworkDesktop => navigator.NavigateViewModelAsync<ModulesFwDesktopModel>(this),
            _ => navigator.NavigateViewModelAsync<ModulesFw16Model>(this),
        };
    }
}
