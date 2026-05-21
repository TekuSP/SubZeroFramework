# SubZeroFramework Copilot Skills

## Purpose
This document captures repository-specific skills, architecture patterns, and problem areas for AI copilots working on SubZeroFramework.

Use `WorkToBeDone.md` as the execution roadmap and priority list.

Use `FunctionalitySpecification.md` as the source of truth for intended menu-item and page behavior.

Use `Architecture.md` as the source of truth for the service/client split, privilege boundary, process lifecycle, IPC ownership, and multi-instance assumptions.

## Key areas of expertise

### Important steps
- Avoid using PowerShell unless MCP tools (such as Microsoft Knowledge Search or Nuget Package Search) are completely unavailable or fail to provide the required documentation, code samples, or best practices for the tasks at hand.
- Always refer to `WorkToBeDone.md` for the current list of required improvements and align your work with those items.
- Refer to `FunctionalitySpecification.md` when working on navigation, page responsibilities, or user-facing surface behavior.
- Refer to `Architecture.md` when working on service/client boundaries, privileges, shutdown behavior, IPC ownership, or multi-instance behavior.
- When modifying service or core service-boundary code, prefer structured DI-backed logging with `ILogger<T>` at lifecycle boundaries, mutating commands, direct stream writes, publish points, authorization rejections, and exceptional shutdown/restore paths.
- If you need source codes, preferably use the GitHub web interface to navigate and search the codebase, as it provides better context and understanding of the code structure. Use the file paths and class names mentioned in this document to locate relevant code sections.
If possible use ObservableProperty with ObservableObject, leveraging C# partial classes to reduce boilerplate and ensure change notifications are properly raised for UI updates. This is especially important for view models and any state that the UI binds to.
- Prefer `[NotifyPropertyChangedFor]` and `[NotifyCanExecuteChangedFor]` on `[ObservableProperty]` dependencies instead of manual `OnPropertyChanged(...)` or `NotifyCanExecuteChanged()` calls; repo analyzers `SZF0001` and `SZF0002` enforce this.
- Prefer `[ObservableProperty]` public partial properties over manual `SetProperty(...)` wrappers for bindable state; repo analyzer `SZF0012` enforces this.
- For inventory surfaces, prefer FrameworkDotnet data first and only use Hardware.Info to fill gaps, keeping that fallback flow behind the existing service/gRPC/client boundary.
- For service lifecycle work, keep install/update/shutdown/restart/autorun management out of gRPC. Prefer the packaged service executable and `FrameworkServiceManagementCli` so the client stays unelevated and the action still works when the service is offline.
- For service-owned runtime settings such as polling cadence or writable fan-command authorization, prefer `FrameworkServiceConfigurationGrpcService` / `IFrameworkServiceConfigurationClient` plus the persistent service-owned configuration overlay instead of client-local settings. Keep lifecycle operations out of that gRPC surface.
- Preserve stable item identity in GridView/ListView card layouts. Prefer persistent mutable card/view-model instances exposed via `ReadOnlyObservableCollection<T>` over rebinding fresh arrays every refresh, otherwise cards blink and pointer/layout state resets.
- Keep shared recent-history window labels and separator-step defaults in `PresentationDefaults`; `TimeChartAxisHelper` should only translate those policies into axis limits and separators.
- Re-read any existing XAML page immediately before editing it. The user often makes small manual visual tweaks between turns, and those should be preserved.
- When working on telemetry streams, ensure that you are properly marshalling back to the UI thread using `ObserveOn(SynchronizationContext.Current)` or `ObserveOn(DispatcherQueue.Current)` as appropriate for the platform. This will prevent threading issues and ensure that UI updates happen smoothly.
- When working on IObservable use `DisposeWith` and `CompositeDisposable` to manage subscriptions and ensure they are cleaned up properly to avoid memory leaks or unintended side effects.
- When modifying or adding new telemetry streams, consider the implications of stream sharing and backpressure. Use `RefCountedObservableCache` or similar patterns to avoid creating multiple gRPC subscriptions for the same data, and implement explicit throttling or buffering strategies if consumers may fall behind.

### Building
- You build via tasks "build-service", "build-windows", which compiles the service and UNO app.
- Do not build via dotnet build or Visual Studio directly, as they may not execute all necessary steps for the service and UNO app correctly.

### Testing
- You run service and shared regression tests via task "test-service".
- Prefer "test-service" over calling dotnet test directly when validating service, core, contract, or related test changes.

### 1. Service / IPC architecture
- The app uses a background service (`SubZeroFramework.Service`) to isolate Framework EC access from the UI.
- IPC is implemented with gRPC over a Unix domain socket.
- The client app is UNO WinUI3 / Linux SKIA and must never directly call Framework EC on Linux.
- Service work should preserve observable operational logging for startup/shutdown, command handling, stream open/close, and snapshot/state publish paths so failures can be diagnosed without attaching a debugger.
- Code style: one type per file for records, classes, structs, and enums; keep each HardwareInfo model type in its own source file.
- There is a strong contract assembly in `SubZeroFramework.GrpcContracts`.
- `FrameworkGrpcSocketSecurity` validates socket location, path, symlinks, and Linux permissions.
- `FrameworkStatusGrpcService` exposes status and service health.
- `FrameworkServiceConfigurationGrpcService` exposes service-owned runtime configuration (currently polling cadence and fan-command authorization) with read/watch/update semantics.
- `FrameworkTelemetryGrpcService` exposes telemetry and fan control state streams.
- `FrameworkFanControlGrpcService` exposes explicit write commands and enforces authorization.

### 2. Reactive telemetry and DynamicData
- Telemetry is exposed as `IObservable<IChangeSet<...>>` streams.
- `DynamicData` caches are used heavily for fan capabilities, fan states, channels, current values, and history.
- `RetainedSnapshotStream<T>` provides snapshot history plus current values.
- Shared stream reuse is important to avoid duplicate gRPC subscriptions.
- `RefCountedObservableCache` is used for telemetry series streams.
- `GrpcChangeSetWriter` converts DynamicData change sets into gRPC streaming batches.
- `ObservableChannelBridge` provides bounded buffering and backpressure semantics.

### 3. UI patterns
- DI container is configured in `SubZeroFramework/App.xaml.cs`.
- Page code-behind must keep `ViewModel` as a simple CLR property, not a `DependencyProperty`. Follow the lightweight pattern used by `PowerTelemetryPage`, `ThermalTelemetryPage`, and `WarningIssuesPage`: a simple `ViewModel` property in code-behind, with all additional bindable state moved into the view model and consumed via `x:Bind`. The same pattern is acceptable in lightweight UserControls, but any `SZF0009` suppression for the direct `PropertyChanged` invocation must include a real justification that explains the compiled-`x:Bind` / non-`DependencyProperty` rationale.
- UI view models use `SynchronizationContext` or `DispatcherQueue` to observe on UI thread.
- `ReadOnlyObservableCollection<T>` is the preferred binding target for DynamicData outputs.
- Device Capabilities is the current reference pattern for inventory pages: dashboard-aligned cards, copyable value text, stable mutable card/view models, and Framework-first with Hardware.Info fallback data sourcing through IPC.
- The dashboard and header surface service health and telemetry state.
- `MainModel`, `HeaderModel`, and dashboard view models are the primary reactive consumers.
- Prefer `IFrameworkStatusClient`, `IFrameworkTelemetryClient`, `IFanCapabilityClient`, `IFanControlStateClient`, and higher-level telemetry clients instead of raw data providers.
- `SettingsModel` should combine lifecycle state from `IFrameworkServiceControlClient` with service-owned runtime configuration from `IFrameworkServiceConfigurationClient`. Autorun is lifecycle state; polling cadence and writable fan-command authorization are service-owned config.
- Left navigation is icon-led and supports a compact app-shell visual style.
- Pages should emphasize summary cards, status badges, and nested telemetry cards rather than dense tables.
- Use a visually distinct fallback/error card for unsupported hardware and safe-mode states.
- Maintain a persistent system info/profile card near the top of dashboard and telemetry pages.
- Preserve the current dashboard style: dark flattened Fluent-inspired cards, top-level system info, active cooling cards, thermal snapshot cards, and a large history chart section.
- Dashboard gauge rings are intentionally non-hoverable. Preserve `HoverPushout="0"` and `IsHoverable="False"` on comparable LiveCharts pie gauges so they do not jump or darken on hover.
- Provide a consistent interaction pattern for fan-curve editing: per-fan mode buttons, chart point manipulation, and clear instructions.
- Fan curve pages should allow associating sensors to each fan, selecting aggregation mode (average, max, min), and enabling delta/CPU/GPU usage inputs for decision logic.
- Device Capabilities CPU should always show CPU identity plus stable CPU-package cards. Each package may own recent average-usage and frequency charts, and per-core usage cards may be surfaced when the service-backed `Hardware.Info.Aot` path reports trustworthy `PercentProcessorTime` / `CpuCoreList` data. Keep this scoped to Device Capabilities rather than expanding it into a separate top-level CPU telemetry surface without another revalidation pass.
- Storage inventory should stay at drive level, not partition level, with total and per-drive used/free summaries and progress bars.
- Network inventory should show detected adapter cards without a redundant adapter summary block unless explicitly requested.
- Telemetry pages should support multi-series chart legend toggles and clearly labeled axis/context.

### NuGet package usage
- `Grpc.Net.Client`, `Grpc.AspNetCore`, `Google.Protobuf`, `Grpc.Core.Api`, `Grpc.Tools`: enable the strongly-typed gRPC IPC contract, client and server transports, and protobuf code generation for the service boundary.
- `System.Reactive`: provides the reactive primitives and scheduling used by telemetry streams, `ObserveOn`, and stream sharing across the UI.
- `DynamicData`: powers the dynamic change-set model for telemetry caches, fan state collections, current values, and history series.
- `FrameworkDotnet`: is our native Framework EC/firmware wrapper library used in the service and core provider for device snapshots, fan commands, thermal/power telemetry, and EC control. Its source lives in `C:\Users\richa\source\repos\framework-dotnet`, so missing features can be added there.
- `Hardware.Info.Aot`: supplies hardware inventory details in the UNO app for the Device Capabilities page, including RAM modules, manufacturers, serial numbers, monitor current mode details, GPU ↔ monitor associations through `VideoController.MonitorList`, and Device Capabilities CPU package/core usage snapshots through `PercentProcessorTime` / `CpuCoreList` when enabled.
- For current inventory surfaces, prefer targeted refreshes over `RefreshAll()` and keep storage/network inventory flowing through the service snapshot rather than direct UI reads.
- `LiveChartsCore.SkiaSharpView.Uno.WinUI`: used for rendering line charts and telemetry graphs in the UNO UI.
- `Material.Icons`: provides icon glyphs for UI chrome, status, and navigation.
- `CommunityToolkit.WinUI.Extensions`: supplies additional WinUI/Uno UI helpers and extension APIs used in app UI wiring.
- `Microsoft.Extensions.Hosting.Systemd`, `Microsoft.Extensions.Hosting.WindowsServices`: enable the service to run and integrate cleanly as a systemd service on Linux and Windows service on Windows.
- `Microsoft.Extensions.Logging.Abstractions`: provides logging abstractions used in core and service components.
- `Microsoft.Extensions.Options`: supports configuration binding in tests and service startup.
- Test dependencies (`Microsoft.NET.Test.Sdk`, `NUnit`, `NUnit3TestAdapter`): support the repo's regression and unit tests.

### Reference docs
- Hardware.Info: `https://github.com/Jinjinov/Hardware.Info`
  - Use `IHardwareInfo`, call `RefreshAll()` or targeted methods like `RefreshCPUList`, `RefreshMemoryList`, `RefreshBatteryList`, `RefreshMotherboardList`.
  - For storage/network inventory, use `RefreshDriveList()` and `RefreshNetworkAdapterList(includeBytesPerSec: false, includeNetworkAdapterConfiguration: true, millisecondsDelayBetweenTwoMeasurements: 0)`.
  - Prefer `RefreshVideoControllerList(refreshMonitorList: true)` when you need explicit monitor ↔ GPU associations via `VideoController.MonitorList` and monitor current mode data.
  - Avoid Windows WMI startup delay by excluding heavy queries where the page does not need them; `includePercentProcessorTime=true` is now reserved for the Device Capabilities CPU snapshot/history path that drives package usage charts and per-core cards, and `includeBytesPersec=false` remains preferred for network inventory.
  - Keep Hardware.Info CPU usage visuals scoped to those Device Capabilities package/core cards rather than a broader standalone CPU dashboard without a fresh revalidation pass.
  - Prefer `Hardware.Info.Aot` in Uno/WASM/AOT contexts when available.
- LiveCharts UNO WinUI: `https://livecharts.dev/docs/unowinui/2.0.0/Overview.Installation`
  - Install `LiveChartsCore.SkiaSharpView.Uno.WinUI` and configure `LiveCharts.Configure(c => c.AddSkiaSharp().AddDefaultMappers().AddDefaultTheme().UseDefaults())`.
  - Use XAML-friendly types like `lvc:CartesianChart`, `lvc:XamlLineSeries`, `SeriesSource`, and `SeriesTemplate` for clean MVVM binding.
  - For gauge-style `PieChart` rings that should stay visually static, preserve `HoverPushout="0"` and `IsHoverable="False"` on the `XamlPieSeries`.
  - Prefer relative-time labels such as `30s`, `25s`, `5s`, and `now` for compact history cards when matching the current dashboard/device-capability history style.
- Material Design Icons: `https://pictogrammers.com/library/mdi/`
  - Choose glyph names from the MDI library for status icons, cards, actions, and navigation.
- Reactive: `https://introtorx.com/chapters/disposables` and other pages from intro to Rx
  - Use `IObservable<T>`, `Subscribe`, `ObserveOn`, `Select`, `Where`, `CombineLatest`, and other operators to compose telemetry streams.
  - Manage subscriptions with `CompositeDisposable` and `DisposeWith` to ensure proper cleanup.
- DynamicData: `https://github.com/reactivemarbles/DynamicData`
  - Use `SourceCache<T, K>` or `SourceList<T>` as the source.
  - Call `.Connect()` and compose `Filter`, `Transform`, `Sort`, `Bind(out collection)`, `DisposeMany`, `ExpireAfter`, `AsObservableCache`, `AsObservableList`.
  - Use change-set operators to keep UI collections in sync without manual collection maintenance.

### Page/tab behavior
- Dashboard: full landing page with system identity, active cooling cards, thermal snapshot cards, history charts, power/battery summary, and service/health status. Fan and thermal gauge rings should remain visually stable on hover.
- Thermal Telemetry: detailed temperature history with sensor selection, series toggles, legends, and comparison across multiple sensors.
- Power Telemetry: battery and power system diagnostics, including charge, voltage, current, power source state, cycles, and history charts.
- Fan Curve Profiles: per-fan editor and profile manager, allowing sensor-to-fan associations, aggregation mode choices, delta/CPU/GPU usage options, and profile save/restore controls.
- Device Capabilities: dashboard-aligned inventory page with device identity, EC version/build, BIOS release date, CPU package cards with recent average-usage and frequency history plus per-core usage cards when available, memory/storage/network/graphics/display inventory, grouped graphics-card sections with explicit GPU ↔ monitor associations and an Unknown graphics card bucket for unlinked displays, runtime sensor/fan/battery status cards, copyable value text, and drive-level usage summaries. Prefer Framework data first, then Hardware.Info through IPC.
- Warnings / Issues: error and warning surface with quick remediation buttons—restart service, install/update/uninstall service when a packaged bundle is available, privilege-prompt guidance, and other corrective actions.
- Settings: service health, service-manager identity, shutdown/restart/autorun/install/update/reinstall/uninstall controls, privilege guidance, and service-owned runtime configuration (polling cadence and fan-command authorization) over gRPC. Keep client-only preferences separate from this service-owned config surface.

### 4. UI/UX design guidance
- Preserve the existing dark, flattened Fluent-inspired dashboard style with layered panels and subtle depth.
- Use accent color sparingly for critical status, active controls, and selected series.
- Ensure unsupported or limited modes use a strong warning palette and descriptive action buttons.
- Keep layout spacing consistent with cards and sections grouped logically by task.
- Present active cooling, thermal snapshot, and power/battery data as primary dashboard blocks.
- Provide diagnostics and settings in separate pages with clear headings and grouped controls.
- Avoid overloading screens with too many visible sensors; let users toggle visible series.
- Make health indicators immediately readable: OK/ERROR badge states, available counts, and summaries.

### 5. Fan control and safety domain
- Fan-control commands are gated through `FrameworkFanControlAuthorizationService`.
- Service config can enable or disable fan-command access via `FrameworkServiceOptions`.
- `FrameworkFanControlStateStore` tracks manual/auto/custom modes and exposes state for clients.
- `FanControlStateSnapshot` includes custom curve points, aggregation mode, and driving sensors.
- Actual EC writes are performed by `FrameworkDataProvider` and are only reached via the service.
- Safety restore happens during polling stop and dispose.
- `SetFanRpm`, `SetFanDuty`, and `RestoreAutoFanControl` are the current command primitives.

## WorkToBeDone alignment
This repo has a living list of required improvements in `WorkToBeDone.md`. Future copilots should use it as the primary roadmap.

### Service architecture
- The service boundary is already correct, and the UNO app should stay on typed IPC clients rather than reintroducing in-process provider usage.
- Ensure any new telemetry surface is IPC-only, not direct `IFrameworkDataProvider`.

### IPC
- Local-only endpoint restrictions are in place; enforce them on both client and server.
- The current gRPC transport uses explicit status and command timeouts, but long-lived streams need careful cancellation handling.
- Service-side caller validation is partially in place; the missing item is true portable caller identity validation.
- Telemetry stream integration remains a priority: end-to-end UI wiring plus explicit throttling/backpressure policy.

### Windows and Linux service operations
- Packaged publish scripts now exist for Windows and Linux service bundles, and CI stages them under `service-package/windows` and `service-package/linux` next to the app artifacts.
- Service install/update/shutdown/restart/autorun work now goes through the published service executable in `--service-management` mode so the client remains unelevated.
- There is still no production logging guidance for Event Log, files, or journald.
- Root execution on Linux still needs real systemd validation.

### Fan safety
- Manual fan control tracking is incomplete; add explicit state tracking for active manual control.
- Ensure restore-to-auto is robust in all controlled stop paths and during shutdown.
- Define failure semantics when restore-to-auto fails.
- Add guardrails against duplicate restore attempts.

### UI integration
- The UNO app no longer uses direct `IFrameworkDataProvider` fallback paths; keep UI work on typed IPC clients.
- Visible service-health and installation/elevation guidance now exist in header/settings/warnings, and Settings now owns the service-backed polling/auth configuration flow; logs and copy-diagnostics actions are still missing.
- Decide which views use distinct status transitions versus live telemetry cadence.
- Thermal Telemetry is now the first end-to-end dedicated telemetry surface; Device Capabilities remains the reference inventory page, while Power Telemetry and Fan Curve Profiles are the larger remaining UI slices.

### Testing
- Expand integration coverage for gRPC contract validation and reconnect behaviors.
- Add tests for telemetry history window validation, stream startup, cancellation, and long-lived stream semantics.
- Service configuration store/manager and autorun parsing now have regression coverage; remaining gaps are broader startup/binding, watch-stream reconnect, and cross-platform deployment tests.
- Add Windows/Linux deployment and service startup tests where feasible.

## Known weak points and review focus
- `HasCallerIdentityValidation` is not implemented; local caller validation is incomplete.
- Backpressure handling in `GrpcChangeSetWriter` / `ObservableChannelBridge` can terminate streams if consumers fall behind.
- Shared observable retry/resilience semantics are weak; underlying gRPC stream errors may kill all subscribers.
- Synchronous blocking in polling stop is risky for shutdown.
- Custom fan curve command support is not fully implemented end-to-end.
- The service needs better shutdown/recovery semantics and fan safety failure handling.
- Telemetry UI is still partial beyond Thermal Telemetry; Power Telemetry and Fan Curve Profiles still need broader end-to-end telemetry follow-through.
- Device Capabilities CPU now uses service-backed package usage/frequency charts and per-core usage cards when the updated Hardware.Info path reports trustworthy `PercentProcessorTime` / `CpuCoreList` data, but broader standalone CPU dashboards remain intentionally out of scope until a future revalidation pass.
- Configuration watch/reconnect coverage and broader startup/binding coverage still need more tests.

## Priorities for future copilots
1. IPC hardening first
   - validate socket ownership and permissions
   - reject symlinks and unexpected endpoint paths
   - clarify Windows vs Linux caller validation capabilities
2. Telemetry stream resilience
   - share status/current-value streams
   - avoid duplicate gRPC subscriptions
   - add explicit throttling/backpressure behavior and retry semantics
3. Broaden dedicated telemetry surfaces
  - keep Thermal Telemetry as the reference IPC-only telemetry page pattern
  - extend similar current-value and history behavior to the remaining telemetry-heavy pages without reintroducing direct provider usage
4. Fan-control safety
   - preserve the command boundary
   - implement explicit manual override tracking and restore semantics
5. Deployment and service lifecycle
  - finish real-world validation and documentation of the packaged Windows/Linux service install and update flows
  - document and test service startup, restart, update, and failure modes

## Recommended work habits
- Always preserve the service boundary between UI and native Framework access.
- Keep telemetry read-only flows separated from command flows.
- Prefer explicit validation of socket endpoint path and transport security.
- Avoid adding direct `FrameworkDataProvider` references into UI code; use gRPC client abstractions instead.
- When modifying reactive streams, verify UI thread marshalling and collection updates.
- Consult `WorkToBeDone.md` before adding new work; align changes to the checklist.

## Useful files and classes
- `SubZeroFramework.Service/Program.cs`
- `SubZeroFramework.Service/FrameworkServiceManagementCli.cs`
- `SubZeroFramework.Service/Services/FrameworkServiceConfigurationGrpcService.cs`
- `SubZeroFramework.Service/Services/FrameworkServiceConfigurationManager.cs`
- `SubZeroFramework.Service/Services/FrameworkServiceConfigurationStore.cs`
- `SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs`
- `SubZeroFramework.Service/Services/FrameworkFanControlGrpcService.cs`
- `SubZeroFramework.Service/Services/FrameworkFanControlStateStore.cs`
- `SubZeroFramework.Core/Services/FrameworkDataProvider.cs`
- `SubZeroFramework.Core/Services/FrameworkServiceAutorunStateParser.cs`
- `SubZeroFramework.Service/Services/HardwareInfoGrpcMapper.cs`
- `SubZeroFramework.Services/FrameworkGrpcChannelFactory.cs`
- `SubZeroFramework.Services/FrameworkGrpcSocketSecurity.cs`
- `SubZeroFramework/Services/IFrameworkServiceConfigurationClient.cs`
- `SubZeroFramework/Services/GrpcFrameworkServiceConfigurationClient.cs`
- `SubZeroFramework/Services/LocalFrameworkServiceControlClient.cs`
- `SubZeroFramework/Services/GrpcHardwareInfoClient.cs`
- `SubZeroFramework.Services/RefCountedObservableCache.cs`
- `SubZeroFramework/WorkToBeDone.md`
- `SubZeroFramework/Docs/TelemetryUiGuide.md`
- `SubZeroFramework.Service/Services/GrpcChangeSetWriter.cs`
- `SubZeroFramework.Service/Services/ObservableChannelBridge.cs`
- `SubZeroFramework/Presentation/MainModel.cs`
- `SubZeroFramework/Presentation/Header/SubZeroHeaderModel.cs`
- `SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesModel.cs`
- `SubZeroFramework/Controls/DeviceCapabilities/Models/DeviceCapabilitiesCpuPackageCardModel.cs`
- `SubZeroFramework/Controls/DeviceCapabilities/Models/DeviceCapabilitiesCpuCoreItemModel.cs`
- `SubZeroFramework/Controls/DeviceCapabilities/Models/DeviceCapabilitiesGraphicsCardGroupModel.cs`
- `SubZeroFramework/Presentation/MenuItems/Settings/SettingsModel.cs`
- `SubZeroFramework/Presentation/MenuItems/WarningsIssues/WarningIssuesModel.cs`
- `SubZeroFramework/Presentation/PresentationDefaults.cs`
- `SubZeroFramework/Presentation/TimeChartAxisHelper.cs`
- `SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesPage.xaml`
- `SubZeroFramework/Presentation/MenuItems/Dashboard/DashboardPage.xaml`
- `SubZeroFramework.Service/Scripts/package-windows-service.ps1`
- `SubZeroFramework.Service/Scripts/package-linux-service.sh`
- `.github/workflows/ci.yml`

## Short guidance for future copilots
- Understand the root cause: Linux requires root for Framework driver access, so service isolation is mandatory.
- Do not treat the service as a generic network service; it is local-only and must be hardened accordingly.
- Focus first on IPC security, telemetry stream resilience, and fan-control safety.
- Use the existing `WorkToBeDone.md` checklist to align changes with project intent.
- When in doubt, preserve the split between service-hosted EC logic and UNO UI client logic.
