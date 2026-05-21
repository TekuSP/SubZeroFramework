---
name: "Tool-First Workflow"
description: "Use when planning, researching, debugging, implementing, validating, or editing code in SubZeroFramework. Prefer available tools over unsupported guesswork, and use the sequential-thinking tool for multi-step, ambiguous, or architectural tasks before implementing."
---
# Tool-First Workflow

- Prefer available tools whenever they materially improve correctness, context gathering, validation, or code modification.
- Do not skip tools just to answer faster when the task benefits from workspace search, file reads, edits, diagnostics, tests, build tasks, web research, or other structured tool output.
- For multi-step, ambiguous, architectural, or cross-cutting tasks, use the sequential-thinking tool before implementing so assumptions, branches, and revisions are made explicit.
- Prefer workspace tasks, build tools, diagnostics, search tools, and structured documentation sources over ad hoc shell usage.
- Use direct conversational answers without tools only when the task is genuinely simple, self-contained, and does not need workspace context, validation, or file changes.
- Keep tool usage purposeful: use the smallest set of tools that gives reliable context or verification, not random tool churn.
- Pair this instruction with [research-and-tooling.instructions.md](./research-and-tooling.instructions.md) when the task needs authoritative sources or build and validation decisions.

### NuGet package usage
- `Grpc.Net.Client`, `Grpc.AspNetCore`, `Google.Protobuf`, `Grpc.Core.Api`, `Grpc.Tools`: enable the strongly-typed gRPC IPC contract, client and server transports, and protobuf code generation for the service boundary.
- `System.Reactive`: provides the reactive primitives and scheduling used by telemetry streams, `ObserveOn`, and stream sharing across the UI.
- `DynamicData`: powers the dynamic change-set model for telemetry caches, fan state collections, current values, and history series.
- `FrameworkDotnet`: is our native Framework EC/firmware wrapper library used in the service and core provider for device snapshots, fan commands, thermal/power telemetry, and EC control. Its source lives in `C:\Users\richa\source\repos\framework-dotnet`, so missing features can be added there.
- `Hardware.Info.Aot`: supplies hardware inventory details in the UNO app for the Device Capabilities page, including RAM modules, manufacturers, serial numbers, and system component metadata.
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
  - Avoid Windows WMI startup delay by excluding heavy queries where the page does not need them; reserve `includePercentProcessorTime=true` for the service-backed Device Capabilities CPU snapshot/history path, and prefer `includeBytesPersec=false` for network inventory.
  - The current service-backed Hardware.Info CPU path has been revalidated for Device Capabilities CPU package usage charts and per-core cards. Keep those CPU usage visuals scoped to the stable Device Capabilities cards, and do not promote them into a separate top-level CPU dashboard without another revalidation pass.
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
