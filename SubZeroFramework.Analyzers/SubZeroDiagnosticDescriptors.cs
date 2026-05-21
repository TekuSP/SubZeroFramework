using Microsoft.CodeAnalysis;

namespace SubZeroFramework.Analyzers;

internal static class SubZeroDiagnosticDescriptors
{
    internal static readonly DiagnosticDescriptor AvoidDirectOnPropertyChanged = new(
        id: "SZF0001",
        title: "Use NotifyPropertyChangedFor",
        messageFormat: "Use [NotifyPropertyChangedFor] on an [ObservableProperty] dependency instead of calling OnPropertyChanged directly",
        category: "SubZeroFramework.Mvvm",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework view models should express dependent notifications through CommunityToolkit attributes instead of manual OnPropertyChanged calls.");

    internal static readonly DiagnosticDescriptor AvoidDirectNotifyCanExecuteChanged = new(
        id: "SZF0002",
        title: "Use NotifyCanExecuteChangedFor",
        messageFormat: "Use [NotifyCanExecuteChangedFor] on the relevant [ObservableProperty] instead of calling NotifyCanExecuteChanged directly",
        category: "SubZeroFramework.Mvvm",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework command invalidation should flow from CommunityToolkit attributes instead of manual NotifyCanExecuteChanged calls.");

    internal static readonly DiagnosticDescriptor ObservablePropertyMustBePartialProperty = new(
        id: "SZF0003",
        title: "ObservableProperty must be a partial property",
        messageFormat: "Use [ObservableProperty] only on partial properties; field-backed generation is not allowed in this repo",
        category: "SubZeroFramework.Mvvm",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework uses CommunityToolkit partial properties rather than field-backed ObservableProperty generation.");

    internal static readonly DiagnosticDescriptor ObservableSubscriptionMustObserveOn = new(
        id: "SZF0004",
        title: "Observable subscriptions must use ObserveOn",
        messageFormat: "Observable subscriptions must call ObserveOn(...) before Subscribe(...) in this repo",
        category: "SubZeroFramework.Reactive",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework observable subscriptions should explicitly marshal to the intended scheduler before subscribing.");

    internal static readonly DiagnosticDescriptor ObservableSubscriptionMustDisposeWith = new(
        id: "SZF0005",
        title: "Observable subscriptions must use DisposeWith",
        messageFormat: "Observable subscriptions must end with DisposeWith(...) in this repo",
        category: "SubZeroFramework.Reactive",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework observable subscriptions should be attached to a CompositeDisposable via DisposeWith.");

    internal static readonly DiagnosticDescriptor ObservableSubscriptionsNeedCompositeDisposable = new(
        id: "SZF0006",
        title: "Observable subscriptions require CompositeDisposable",
        messageFormat: "Type '{0}' implements IDisposable and subscribes to IObservable, so it should own a CompositeDisposable",
        category: "SubZeroFramework.Reactive",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework disposable types that subscribe to observables should keep a CompositeDisposable member.");

    internal static readonly DiagnosticDescriptor PollingStateMustBeMethodDriven = new(
        id: "SZF0007",
        title: "Polling state must be method-driven",
        messageFormat: "Polling state property '{0}' must be getter-only; mutate polling through Set*/Start*/Stop* methods instead of property setters",
        category: "SubZeroFramework.Polling",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework polling control stays method-driven so interval and lifecycle changes remain explicit.");

    internal static readonly DiagnosticDescriptor CurrentTelemetryIdentityMustBePreserved = new(
        id: "SZF0008",
        title: "Current telemetry identity must be preserved",
        messageFormat: "Current-telemetry change handlers should mark snapshots unavailable instead of removing them from caches",
        category: "SubZeroFramework.DynamicData",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Current telemetry entities should stay in caches and flip IsAvailable to false instead of being removed on ChangeReason.Remove.");

    internal static readonly DiagnosticDescriptor AvoidDirectPropertyChangedEventInvocation = new(
        id: "SZF0009",
        title: "Avoid direct PropertyChanged event invocation",
        messageFormat: "Avoid invoking PropertyChanged directly; prefer ObservableProperty/NotifyPropertyChangedFor or another non-manual notification mechanism",
        category: "SubZeroFramework.Mvvm",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework UI state should prefer CommunityToolkit attributes and generated property-notification flows over direct PropertyChanged event invocation.");

    internal static readonly DiagnosticDescriptor DisposableRegistryMustDisposeRemainingValues = new(
        id: "SZF0010",
        title: "Disposable registry values must be disposed in Dispose",
        messageFormat: "Disposable registry '{0}' stores IDisposable values and must dispose remaining values in Dispose()",
        category: "SubZeroFramework.Reactive",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Keyed IDisposable registries should dispose their remaining values during type disposal to avoid leaking subscriptions or handles.");

    internal static readonly DiagnosticDescriptor DisposableRegistryRemoveMustDisposeValue = new(
        id: "SZF0011",
        title: "Removed disposable registry values must be disposed",
        messageFormat: "Removed disposable value from registry '{0}' must be disposed when it is removed",
        category: "SubZeroFramework.Reactive",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Removing an IDisposable from a keyed registry should dispose the removed value immediately to avoid leaks when subscriptions are replaced or removed incrementally.");

    internal static readonly DiagnosticDescriptor AvoidSetProperty = new(
        id: "SZF0012",
        title: "Use ObservableProperty partial properties",
        messageFormat: "Use [ObservableProperty] on a public partial property instead of SetProperty",
        category: "SubZeroFramework.Mvvm",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SubZeroFramework view models should use CommunityToolkit.Mvvm [ObservableProperty] public partial properties instead of manual SetProperty wrappers.");
}