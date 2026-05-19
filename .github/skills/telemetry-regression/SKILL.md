---
name: telemetry-regression
description: 'Validate telemetry changes in SubZeroFramework. Use for telemetry regressions, reconnect behavior, stream startup or cancellation, history-window validation, DynamicData identity issues, backpressure, shared stream reuse, and IPC-only telemetry UI work.'
argument-hint: 'Describe the telemetry change, bug, or validation slice to check.'
---
# Telemetry Regression

Use this skill when a change can affect status or telemetry behavior anywhere in the SubZeroFramework pipeline: provider, service, gRPC contracts, clients, view models, or telemetry-heavy UI.

## Read first
- [WorkToBeDone.md](../../../WorkToBeDone.md)
- [Architecture.md](../../../Architecture.md)
- [IPC Authorization and UI Cadence Guide](../../../SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md)
- [Telemetry UI Guide](../../../SubZeroFramework/Docs/TelemetryUiGuide.md)
- [SubZeroFramework.Service/README.md](../../../SubZeroFramework.Service/README.md)

## Use this for
- reconnect bugs after service restart
- long-lived stream startup or cancellation issues
- history window validation and retained-series behavior
- DynamicData availability, identity, or cache-shape regressions
- duplicate gRPC subscription or stream-sharing problems
- backpressure and batched-delivery issues
- telemetry UI updates that must stay on the service and IPC path
- thermal, power, fan, or generic telemetry model and mapping changes

## What this workflow protects
- the Uno client must not fall back to direct `IFrameworkDataProvider` usage for telemetry pages
- telemetry remains read-only across the IPC boundary unless an explicit command surface is being touched
- polling control stays explicit through `SetPolling(...)`, `StartPolling()`, and `StopPolling()` rather than new ad hoc loops or mutable property-style control paths
- stream sharing and cache reuse stay intentional instead of one-subscription-per-view-model
- previously seen telemetry entities stay stable when appropriate and should flip `IsAvailable` to `false` instead of disappearing when identity stability matters
- chart data remains sorted and bound through long-lived collections instead of fresh arrays or rebuilt series on each tick
- transition-driven UI state stays separate from live telemetry cadence

## Procedure

### 1. Classify the change surface
Decide which layer or layers are affected before editing anything.

- **Provider and source-of-truth shaping**
  - [`../../../SubZeroFramework.Core/Services/FrameworkDataProvider.cs`](../../../SubZeroFramework.Core/Services/FrameworkDataProvider.cs)
  - [`../../../SubZeroFramework.Service/FrameworkTelemetryWorker.cs`](../../../SubZeroFramework.Service/FrameworkTelemetryWorker.cs)
- **Service streaming and mapping**
  - [`../../../SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs`](../../../SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs)
  - [`../../../SubZeroFramework.Service/Services/TelemetryGrpcMapper.cs`](../../../SubZeroFramework.Service/Services/TelemetryGrpcMapper.cs)
  - [`../../../SubZeroFramework.Service/Services/GrpcChangeSetWriter.cs`](../../../SubZeroFramework.Service/Services/GrpcChangeSetWriter.cs)
  - [`../../../SubZeroFramework.Service/Services/ObservableChannelBridge.cs`](../../../SubZeroFramework.Service/Services/ObservableChannelBridge.cs)
- **Client stream sharing and projections**
  - [`../../../SubZeroFramework/Services/GrpcFrameworkTelemetryClient.cs`](../../../SubZeroFramework/Services/GrpcFrameworkTelemetryClient.cs)
  - [`../../../SubZeroFramework/Services/TemperatureTelemetryClient.cs`](../../../SubZeroFramework/Services/TemperatureTelemetryClient.cs)
  - [`../../../SubZeroFramework/Services/BatteryTelemetryClient.cs`](../../../SubZeroFramework/Services/BatteryTelemetryClient.cs)
  - [`../../../SubZeroFramework/Services/FanTelemetryClient.cs`](../../../SubZeroFramework/Services/FanTelemetryClient.cs)
  - [`../../../SubZeroFramework/Services/RefCountedObservableCache.cs`](../../../SubZeroFramework/Services/RefCountedObservableCache.cs)
- **Telemetry presentation and UI identity**
  - [`../../../SubZeroFramework/Presentation/MenuItems/ThermalTelemetry/ThermalTelemetryModel.cs`](../../../SubZeroFramework/Presentation/MenuItems/ThermalTelemetry/ThermalTelemetryModel.cs)
  - [`../../../SubZeroFramework/Presentation/MenuItems/PowerTelemetry/PowerTelemetryModel.cs`](../../../SubZeroFramework/Presentation/MenuItems/PowerTelemetry/PowerTelemetryModel.cs)
  - [`../../../SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesModel.cs`](../../../SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesModel.cs)
  - [`../../../SubZeroFramework/Controls/Thermal/Models/ThermalSensorModel.cs`](../../../SubZeroFramework/Controls/Thermal/Models/ThermalSensorModel.cs)

### 2. Restate the expected telemetry behavior
Before changing code, write down what should happen for the scenario under test.

Check these questions:
- Should this surface react to **status transitions** or to **live telemetry cadence**?
- When telemetry disappears, should the item be removed, or should it stay visible and flip `IsAvailable` to `false`?
- Does the history window have explicit minimum and maximum validation?
- Should multiple consumers share one stream, or is a per-key stream expected?
- If the service restarts, should the consumer reconnect and rebuild from service state?

Use `WorkToBeDone.md` to align the expected behavior with open telemetry and reconnect work, especially around stream startup semantics, cancellation, and reconnect coverage.

### 3. Inspect the risk-specific hotspots
Pick the checks that match the change.

- **Reconnect or restart behavior**
  - verify clients rebuild from service streams after disconnects
  - inspect retry loops and reconnect delay behavior in the client stream layer
- **Polling behavior**
  - preserve explicit polling control instead of introducing new background polling ownership in the UI or duplicate service loops
  - verify interval changes still follow the established stop -> set -> start pattern where applicable
- **History or series behavior**
  - verify history-window validation stays within `TelemetryHistoryLimits`
  - verify series identity is shared by `(channelId, historyWindow)` when that is the intended path
- **Backpressure or batching behavior**
  - inspect `GrpcChangeSetWriter` and `ObservableChannelBridge`
  - check whether bounded buffering or overload handling could drop or terminate streams unexpectedly
- **UI identity or chart glitches**
  - keep stable `ReadOnlyObservableCollection<T>` projections and mutable card or row models
  - do not bind charts to freshly rebuilt point collections or recreate series every telemetry tick
  - sort retained points by `ObservedAt` and `SampleId` before chart binding when order is not guaranteed by the source cache
- **IPC-only telemetry UI rule**
  - telemetry pages should use typed telemetry clients, not direct `IFrameworkDataProvider` fallback paths in the Uno UI

### 4. Add or update regression coverage
Prefer tests that target the changed semantics instead of broad unrelated coverage.

Start from these anchors:
- [`../../../SubZeroFramework.Tests/FrameworkTelemetryWorkerTests.cs`](../../../SubZeroFramework.Tests/FrameworkTelemetryWorkerTests.cs)
- [`../../../SubZeroFramework.Tests/FrameworkGrpcSocketSecurityTests.cs`](../../../SubZeroFramework.Tests/FrameworkGrpcSocketSecurityTests.cs)

Add focused tests when relevant for:
- service startup configuring polling and refresh correctly
- service stop or cancellation shutting streams or polling down cleanly
- reconnect behavior after a service restart or transient stream fault
- contract or mapper changes affecting telemetry parsing
- history-window validation and out-of-range requests
- availability transitions where prior entities should remain stable and flip to unavailable instead of disappearing

If no focused automated coverage exists yet, call that out explicitly in the final report instead of pretending the gap is closed.

### 5. Validate with workspace tasks
Prefer workspace tasks over ad hoc shell commands.

- Run `test-service` for service, core, contracts, or shared telemetry-path changes.
- Run `build-service` for fast compile validation when the change is service-only.
- Run `build-windows` when the client, view models, or XAML telemetry surfaces changed.
- Run `build-linux` when the change touches shared transport, cross-platform behavior, or platform-sensitive telemetry code.

If the change is XAML-heavy, reread the current XAML immediately before editing and preserve any manual visual tweaks already in the file.

### 6. Perform the completion checks
Do not finish until you can answer these clearly.

- Does telemetry still flow through the intended path: provider -> service/gRPC -> client abstraction -> view model -> page?
- Did the change accidentally widen command behavior, privilege expectations, or direct EC access?
- Are reconnect, cancellation, and history-window semantics still explicit and defensible?
- Are UI collections and charts stable under live updates?
- Are any remaining gaps in status reconnect tests, long-lived stream tests, or telemetry contract validation called out clearly?

## Decision points
- If the change affects contracts or mapper output, trace the data end to end and mention compatibility risk.
- If the change is UI-only, verify the page still follows the repo UI rules: simple code-behind `ViewModel`, stable collections, and IPC-only telemetry sourcing.
- If the change touches fan or command-adjacent UI, ensure read-only telemetry work did not loosen the fan-control authorization boundary.
- If the change touches stream sharing, verify the code does not create one underlying stream per consumer when sharing is expected.

## Output format
Return a concise summary with:
1. the telemetry layers touched,
2. the expected behavior that was validated,
3. the focused tests and builds run,
4. any unresolved gaps or follow-up regression coverage still needed.
