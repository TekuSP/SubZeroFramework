### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
SZF0001 | SubZeroFramework.Mvvm | Warning | Use NotifyPropertyChangedFor instead of direct OnPropertyChanged.
SZF0002 | SubZeroFramework.Mvvm | Warning | Use NotifyCanExecuteChangedFor instead of direct NotifyCanExecuteChanged.
SZF0003 | SubZeroFramework.Mvvm | Warning | Require ObservableProperty on partial properties only.
SZF0004 | SubZeroFramework.Reactive | Warning | Require ObserveOn before Subscribe for observable subscriptions.
SZF0005 | SubZeroFramework.Reactive | Warning | Require DisposeWith after observable subscriptions.
SZF0006 | SubZeroFramework.Reactive | Warning | Require CompositeDisposable ownership in IDisposable types that subscribe to observables.
SZF0007 | SubZeroFramework.Polling | Warning | Keep polling state method-driven and forbid settable polling properties.
SZF0008 | SubZeroFramework.DynamicData | Warning | Preserve current-telemetry identity by marking snapshots unavailable instead of removing them.
