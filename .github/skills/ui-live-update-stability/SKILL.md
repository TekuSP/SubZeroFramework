---
name: ui-live-update-stability
description: 'Keep SubZeroFramework telemetry and inventory UI stable under live updates. Use when editing dashboard, Device Capabilities, or telemetry pages with LiveCharts, stable card collections, Hardware.Info inventory mapping, retained history ordering, or fan capability-driven UI state.'
argument-hint: 'Describe the dashboard, inventory, chart, or live-update stability issue to review.'
---
# UI Live Update Stability

Use this skill when a UI surface is correct at first render but becomes jittery, blink-prone, misordered, or heuristic-heavy once live updates start flowing.

## Read first
- [FunctionalitySpecification.md](../../../FunctionalitySpecification.md)
- [Architecture.md](../../../Architecture.md)
- [Telemetry UI Guide](../../../SubZeroFramework/Docs/TelemetryUiGuide.md)
- [README.md](../../../README.md)

## Use this for
- Device Capabilities card refresh behavior
- dashboard fan, thermal, or sparkline chart updates
- LiveCharts binding structure on Uno or Skia
- Hardware.Info-backed inventory mapping for drives and network adapters
- fan capability metadata and UI status derivation
- tracing a missing inventory or capability value back to the source boundary

## What this workflow protects
- card and chart identity should remain stable under polling and streaming updates
- Hardware.Info fallback data should still flow through the service and gRPC boundary
- UI labels, limits, and status text should come from authoritative capability or snapshot data, not ad hoc client heuristics
- Device Capabilities should stay honest about CPU data quality

## Procedure

### 1. Preserve stable item identity
- Keep persistent mutable card or row models exposed through `ReadOnlyObservableCollection<T>`.
- Do not bind `GridView.ItemsSource` or similar item controls directly to fresh hardware snapshot arrays.
- Update existing card models in place and only add or remove items when the collection shape truly changes.

### 2. Follow the current Hardware.Info inventory path
- Device Capabilities drives and network adapters flow through `HardwareInfoSnapshot.Inventory`.
- `FrameworkDataProvider.ReadHardwareInfoSnapshot()` uses `RefreshDriveList()` and `RefreshNetworkAdapterList(includeBytesPerSec: false, includeNetworkAdapterConfiguration: true, millisecondsDelayBetweenTwoMeasurements: 0)` to avoid expensive bytes-per-second queries.
- `HardwareInfoReply` includes `drives` and `network_adapters` fields.
- Device Capabilities CPU usage visuals may use the service-backed `HardwareInfoSnapshot` / `WatchHardwareInfoHistory(...)` path when the snapshot reports trustworthy `PercentProcessorTime` / `CpuCoreList` data, but keep that scoped to stable CPU package cards and per-core cards rather than a separate top-level CPU dashboard.

### 3. Keep charts long-lived under telemetry updates
- Reuse long-lived `ObservableValue`, `ObservableCollection<DateTimePoint>`, `ISeries[]`, and `Axis[]` instances where the page already follows that pattern.
- Do not replace temperature history collections, gauge values, or rebuild chart series on every telemetry tick.
- The dashboard sparkline pattern should stay model-owned, with `ISeries[]` and `Axis[]` owned by the card model and bound from XAML.

### 4. Sort retained history explicitly before binding
DynamicData `ToCollection()` over retained telemetry history is not safe to treat as chronological order.

Before binding chart points, sort by `ObservedAt` and then `SampleId` so lines do not fold back on themselves.

### 5. Keep fan metadata service-owned
- Fan max RPM belongs in `FanCapabilityState`, computed in `FrameworkDataProvider` and delivered through the read-only capability stream.
- Use `WatchFanCapabilities` and `IFanCapabilityClient` data for fan status text, color, and limits.
- Do not derive chart limits or status strings from UI-only platform heuristics.

### 6. Treat LiveCharts quirks as library behavior first
- The dashboard fan gauge uses `XamlGaugeSeries`.
- If you see an odd inward cap, inspect gauge corner-radius geometry before blaming theme fills.
- If you touch hover behavior on dashboard-style gauges, preserve the repo intent that they remain visually stable on hover.

### 7. Escalate upstream only after tracing the boundary
If a value is missing from the UI, trace it through the service snapshot or gRPC contract first.

If the source itself is missing the data, the local FrameworkDotnet companion repo is at `C:/Users/richa/source/repos/framework-dotnet`.

## Output format
Return a concise summary with:
1. the UI identity or chart-lifecycle rule involved,
2. the authoritative data path used,
3. any ordering or flicker fix applied,
4. whether the issue belongs in SubZeroFramework or upstream in FrameworkDotnet.
