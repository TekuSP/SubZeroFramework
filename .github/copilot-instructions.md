# SubZeroFramework Copilot Skills

## Purpose
This document captures repository-specific skills, architecture patterns, and problem areas for AI copilots working on SubZeroFramework.

## Key areas of expertise

### Important steps
- Avoid using PowerShell unless MCP tools (such as Microsoft Knowledge Search or Nuget Package Search) are completely unavailable or fail to provide the required documentation, code samples, or best practices for the tasks at hand.
- Always refer to `WorkToBeDone.md` for the current list of required improvements and align your work with those items.
- If you need source codes, preferably use the GitHub web interface to navigate and search the codebase, as it provides better context and understanding of the code structure. Use the file paths and class names mentioned in this document to locate relevant code sections.
If possible use ObservableProperty with ObservableObject, leveraging C# partial classes to reduce boilerplate and ensure change notifications are properly raised for UI updates. This is especially important for view models and any state that the UI binds to.
- When working on telemetry streams, ensure that you are properly marshalling back to the UI thread using `ObserveOn(SynchronizationContext.Current)` or `ObserveOn(DispatcherQueue.Current)` as appropriate for the platform. This will prevent threading issues and ensure that UI updates happen smoothly.
- When working on IObservable use `DisposeWith` and `CompositeDisposable` to manage subscriptions and ensure they are cleaned up properly to avoid memory leaks or unintended side effects.
- When modifying or adding new telemetry streams, consider the implications of stream sharing and backpressure. Use `RefCountedObservableCache` or similar patterns to avoid creating multiple gRPC subscriptions for the same data, and implement explicit throttling or buffering strategies if consumers may fall behind.

### Building
- You build via tasks "build-service", "build-windows", which compiles the service and UNO app.
- Do not build via dotnet build or Visual Studio directly, as they may not execute all necessary steps for the service and UNO app correctly.

### 1. Service / IPC architecture
- The app uses a background service (`SubZeroFramework.Service`) to isolate Framework EC access from the UI.
- IPC is implemented with gRPC over a Unix domain socket.
- The client app is UNO WinUI3 / Linux SKIA and must never directly call Framework EC on Linux.
- Code style: one type per file for records, classes, structs, and enums; keep each HardwareInfo model type in its own source file.
- There is a strong contract assembly in `SubZeroFramework.GrpcContracts`.
- `FrameworkGrpcSocketSecurity` validates socket location, path, symlinks, and Linux permissions.
- `FrameworkStatusGrpcService` exposes status and service health.
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
- UI view models use `SynchronizationContext` or `DispatcherQueue` to observe on UI thread.
- `ReadOnlyObservableCollection<T>` is the preferred binding target for DynamicData outputs.
- The dashboard and header surface service health and telemetry state.
- `MainModel`, `HeaderModel`, and dashboard view models are the primary reactive consumers.
- Prefer `IFrameworkStatusClient`, `IFrameworkTelemetryClient`, `IFanCapabilityClient`, `IFanControlStateClient`, and higher-level telemetry clients instead of raw data providers.
- Left navigation is icon-led and supports a compact app-shell visual style.
- Pages should emphasize summary cards, status badges, and nested telemetry cards rather than dense tables.
- Use a visually distinct fallback/error card for unsupported hardware and safe-mode states.
- Maintain a persistent system info/profile card near the top of dashboard and telemetry pages.
- Preserve the current dashboard style: dark flattened Fluent-inspired cards, top-level system info, active cooling cards, thermal snapshot cards, and a large history chart section.
- Provide a consistent interaction pattern for fan-curve editing: per-fan mode buttons, chart point manipulation, and clear instructions.
- Fan curve pages should allow associating sensors to each fan, selecting aggregation mode (average, max, min), and enabling delta/CPU/GPU usage inputs for decision logic.
- Telemetry pages should support multi-series chart legend toggles and clearly labeled axis/context.

### NuGet package usage
- `Grpc.Net.Client`, `Grpc.AspNetCore`, `Google.Protobuf`, `Grpc.Core.Api`, `Grpc.Tools`: enable the strongly-typed gRPC IPC contract, client and server transports, and protobuf code generation for the service boundary.
- `System.Reactive`: provides the reactive primitives and scheduling used by telemetry streams, `ObserveOn`, and stream sharing across the UI.
- `DynamicData`: powers the dynamic change-set model for telemetry caches, fan state collections, current values, and history series.
- `FrameworkDotnet`: is our native Framework EC/firmware wrapper library used in the service and core provider for device snapshots, fan commands, thermal/power telemetry, and EC control. Its source lives in `C:\Users\richa\source\repos\framework-dotnet`, so missing features can be added there.
- `Hardware.Info.Aot`: supplies hardware inventory details in the UNO app for the Device Capabilities page, including RAM modules, manufacturers, serial numbers, and system component metadata.
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
  - Avoid Windows WMI startup delay by excluding heavy queries; use `includePercentProcessorTime=false` or `includeBytesPersec=false` where applicable.
  - Prefer `Hardware.Info.Aot` in Uno/WASM/AOT contexts when available.
- LiveCharts UNO WinUI: `https://livecharts.dev/docs/unowinui/2.0.0/Overview.Installation`
  - Install `LiveChartsCore.SkiaSharpView.Uno.WinUI` and configure `LiveCharts.Configure(c => c.AddSkiaSharp().AddDefaultMappers().AddDefaultTheme().UseDefaults())`.
  - Use XAML-friendly types like `lvc:CartesianChart`, `lvc:XamlLineSeries`, `SeriesSource`, and `SeriesTemplate` for clean MVVM binding.
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
- Dashboard: full landing page with system identity, active cooling cards, thermal snapshot cards, history charts, power/battery summary, and service/health status.
- Thermal Telemetry: detailed temperature history with sensor selection, series toggles, legends, and comparison across multiple sensors.
- Power Telemetry: battery and power system diagnostics, including charge, voltage, current, power source state, cycles, and history charts.
- Fan Curve Profiles: per-fan editor and profile manager, allowing sensor-to-fan associations, aggregation mode choices, delta/CPU/GPU usage options, and profile save/restore controls.
- Device Capabilities: detected hardware inventory, HardwareInfo-derived details (RAM modules, manufacturer, serial number, etc.) presented with expandable sections to reduce clutter, supported firmware features, available module cards, and future framework card module support when the translation layer exposes them.
- Warnings / Issues: error and warning surface with quick remediation buttons—restart service, rescan device, install/uninstall/reinstall service, and other corrective actions.
- Settings: service install/uninstall/reinstall, user preferences such as preferred units, feature toggles for modules and custom fan curves, and app behavior options.

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
- The service boundary is already correct, but the UNO app should avoid fallback to in-process provider usage except for development/testing.
- Ensure any new telemetry surface is IPC-only, not direct `IFrameworkDataProvider`.

### IPC
- Local-only endpoint restrictions are in place; enforce them on both client and server.
- The current gRPC transport uses explicit status and command timeouts, but long-lived streams need careful cancellation handling.
- Service-side caller validation is partially in place; the missing item is true portable caller identity validation.
- Telemetry stream integration remains a priority: end-to-end UI wiring plus explicit throttling/backpressure policy.

### Windows and Linux service operations
- The code lacks install/publish scripts for Windows `sc.exe create` and Linux systemd.
- There is no documented service recovery or logging guidance for production.
- Root execution on Linux must be validated under real systemd.

### Fan safety
- Manual fan control tracking is incomplete; add explicit state tracking for active manual control.
- Ensure restore-to-auto is robust in all controlled stop paths and during shutdown.
- Define failure semantics when restore-to-auto fails.
- Add guardrails against duplicate restore attempts.

### UI integration
- Replace any remaining direct `IFrameworkDataProvider` usage with IPC clients.
- Add visible service-health indicator, logs/diagnostics actions, and installation/elevation guidance.
- Decide which views use distinct status transitions versus live telemetry cadence.

### Testing
- Expand integration coverage for gRPC contract validation and reconnect behaviors.
- Add tests for telemetry history window validation, stream startup, cancellation, and long-lived stream semantics.
- Add Windows/Linux deployment and service startup tests where feasible.

## Known weak points and review focus
- `HasCallerIdentityValidation` is not implemented; local caller validation is incomplete.
- Backpressure handling in `GrpcChangeSetWriter` / `ObservableChannelBridge` can terminate streams if consumers fall behind.
- Shared observable retry/resilience semantics are weak; underlying gRPC stream errors may kill all subscribers.
- Synchronous blocking in polling stop is risky for shutdown.
- Custom fan curve command support is not fully implemented end-to-end.
- The service needs better shutdown/recovery semantics and fan safety failure handling.
- Telemetry UI is still partially wired; getting the first end-to-end thermal surface is a near-term goal.
- Configuration binding and option response coverage need broader tests.

## Priorities for future copilots
1. IPC hardening first
   - validate socket ownership and permissions
   - reject symlinks and unexpected endpoint paths
   - clarify Windows vs Linux caller validation capabilities
2. Telemetry stream resilience
   - share status/current-value streams
   - avoid duplicate gRPC subscriptions
   - add explicit throttling/backpressure behavior and retry semantics
3. First end-to-end telemetry surface
   - pick a page (thermal preferred)
   - wire current values and at least one history series using `IFrameworkTelemetryClient`
4. Fan-control safety
   - preserve the command boundary
   - implement explicit manual override tracking and restore semantics
5. Deployment and service lifecycle
   - add publish/install automation for Windows and Linux
   - document and test service startup, restart, and failure modes

## Recommended work habits
- Always preserve the service boundary between UI and native Framework access.
- Keep telemetry read-only flows separated from command flows.
- Prefer explicit validation of socket endpoint path and transport security.
- Avoid adding direct `FrameworkDataProvider` references into UI code; use gRPC client abstractions instead.
- When modifying reactive streams, verify UI thread marshalling and collection updates.
- Consult `WorkToBeDone.md` before adding new work; align changes to the checklist.

## Useful files and classes
- `SubZeroFramework.Service/Program.cs`
- `SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs`
- `SubZeroFramework.Service/Services/FrameworkFanControlGrpcService.cs`
- `SubZeroFramework.Service/Services/FrameworkFanControlStateStore.cs`
- `SubZeroFramework.Core/Services/FrameworkDataProvider.cs`
- `SubZeroFramework.Services/FrameworkGrpcChannelFactory.cs`
- `SubZeroFramework.Services/FrameworkGrpcSocketSecurity.cs`
- `SubZeroFramework.Services/RefCountedObservableCache.cs`
- `SubZeroFramework/WorkToBeDone.md`
- `SubZeroFramework/Docs/TelemetryUiGuide.md`
- `SubZeroFramework.Service/Services/GrpcChangeSetWriter.cs`
- `SubZeroFramework.Service/Services/ObservableChannelBridge.cs`
- `SubZeroFramework/Presentation/MainModel.cs`
- `SubZeroFramework/Presentation/Header/SubZeroHeaderModel.cs`

## Short guidance for future copilots
- Understand the root cause: Linux requires root for Framework driver access, so service isolation is mandatory.
- Do not treat the service as a generic network service; it is local-only and must be hardened accordingly.
- Focus first on IPC security, telemetry stream resilience, and fan-control safety.
- Use the existing `WorkToBeDone.md` checklist to align changes with project intent.
- When in doubt, preserve the split between service-hosted EC logic and UNO UI client logic.
