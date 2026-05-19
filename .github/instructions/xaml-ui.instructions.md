---
name: "XAML UI Guidelines"
description: "Use when editing Uno or WinUI XAML pages, controls, cards, layouts, charts, or visual polish in SubZeroFramework. Covers dashboard-aligned styling, stable card identity, InfoBar usage, and IPC-only telemetry UI rules."
applyTo: "SubZeroFramework/**/*.xaml"
---
# XAML UI Guidelines

- Start with [FunctionalitySpecification.md](../../FunctionalitySpecification.md) for page responsibilities and [SubZeroFramework/ReadMe.md](../../SubZeroFramework/ReadMe.md) for the current client-side status.
- Reread the current XAML file immediately before editing and preserve manual visual tweaks already present.
- Match the dark, flattened, card-based Dashboard and Device Capabilities visual language. Good reference pages:
  - [DashboardPage.xaml](../../SubZeroFramework/Presentation/MenuItems/Dashboard/DashboardPage.xaml)
  - [DeviceCapabilitiesPage.xaml](../../SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesPage.xaml)
  - [SettingsPage.xaml](../../SubZeroFramework/Presentation/MenuItems/Settings/SettingsPage.xaml)
- Prefer summary cards, grouped sections, and clear headings over dense utility-table layouts.
- Keep useful values selectable and copyable when practical.
- Preserve stable item identity in `GridView`, `ListView`, and `ItemsRepeater` surfaces; avoid rebinding fresh arrays on refresh-driven pages.
- Use `InfoBar` for service-health, privilege-prompt, readiness, and action-result messaging instead of ad hoc banners.
- Keep comparable gauge rings visually static by preserving `HoverPushout="0"` and `IsHoverable="False"` on LiveCharts pie or gauge rings.
- New telemetry pages and telemetry-heavy UI must stay on the service and IPC client path; do not reintroduce direct `IFrameworkDataProvider` usage in the Uno UI.
- If companion code-behind or presentation logic is involved, keep page code-behind light and avoid moving bindable state out of the view model unless there is a strong reason.
- For XAML-bound state, `ObservableProperty` setters, direct `OnPropertyChanged`/`PropertyChanged` notifications, and mutations of bound `ObservableCollection` instances must happen on the UI thread. Marshal those updates with `DispatcherQueue.EnqueueAsync(...)` (or an equivalent UI-thread hop) instead of assuming the current callback thread or `ObserveOn(...)` is already safe.
