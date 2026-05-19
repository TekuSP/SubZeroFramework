---
name: observable-patterns
description: 'Preserve SubZeroFramework reactive, analyzer, and polling conventions. Use when editing Rx subscriptions, DynamicData projections, custom Roslyn analyzers, WinUI bindable state, telemetry current-state caches, or FrameworkDataProvider polling control.'
argument-hint: 'Describe the reactive, analyzer, polling, or bindable-state change to review.'
---
# Observable Patterns

Use this skill when a change touches subscriptions, polling ownership, analyzer-enforced patterns, or current-state cache semantics.

## Read first
- [Architecture.md](../../../Architecture.md)
- [WorkToBeDone.md](../../../WorkToBeDone.md)
- [`SubZeroFramework.Analyzers`](../../../SubZeroFramework.Analyzers)
- [`SubZeroFramework.Core/Services/FrameworkDataProvider.cs`](../../../SubZeroFramework.Core/Services/FrameworkDataProvider.cs)

## Use this for
- Rx `Subscribe(...)` changes and disposal ownership
- DynamicData current-state cache behavior
- polling control or polling lifecycle changes
- custom analyzer implementation or analyzer-driven fixes
- WinUI 3 `Page` or `UserControl` bindable state decisions
- telemetry model organization under `Models`

## What this workflow protects
- analyzer rules in `SubZeroFramework.Analyzers` are repo-wide through `Directory.Build.props`
- subscriptions should stay scheduler-aware and disposable
- polling stays owned by `FrameworkDataProvider`, not a hosted telemetry `BackgroundService`
- current-state caches keep stable identities by flipping `IsAvailable` instead of removing items
- WinUI code-behind stays compatible with WinRT binding constraints

## Procedure

### 1. Check the active analyzer rules before changing the pattern
Relevant rules for this workflow:
- `SZF0004`: `Subscribe` must include `ObserveOn`
- `SZF0005`: `Subscribe` must include `DisposeWith`
- `SZF0006`: `IDisposable` types with top-level `Subscribe` usage should own a `CompositeDisposable`
- `SZF0007`: polling state must stay method-driven with getter-only polling properties
- `SZF0008`: current-telemetry change handlers must mark entities unavailable instead of removing them from caches

### 2. Apply the subscription ownership rules deliberately
- Add `ObserveOn(...)` before `Subscribe(...)` when the flow updates UI-facing or scheduler-sensitive state.
- Dispose subscriptions with `DisposeWith(...)`.
- If a type owns top-level subscriptions, it should also own a `CompositeDisposable`.
- Exception: lambda-scoped `Observable.Create(...)` bridge subscriptions are intentionally excluded from `SZF0006` to avoid false positives in gRPC bridge clients.

### 3. Preserve the repo polling model
- `FrameworkDataProvider` owns the polling task directly.
- Do not introduce a hosted `BackgroundService` for telemetry polling unless lifecycle requirements change.
- Keep control method-based through `SetPolling(...)`, `StartPolling()`, and `StopPolling()`.
- Do not replace that model with mutable property setters.

### 4. Keep current-state caches stable
When a known fan, thermal, or power entity disappears from current-state data, keep it in the cache and flip `IsAvailable` to `false`.

Do not propagate DynamicData `Remove` for those current-state streams unless the identity really should disappear, because that tears down dashboard identity and history subscriptions.

### 5. Use the safe WinUI bindable-state pattern
In WinUI 3 code-behind for `Page` or `UserControl`, avoid CommunityToolkit `[INotifyPropertyChanged]` for bindable page or control state because it can trigger `MVVMTK0049` in WinRT scenarios.

Prefer dependency properties instead of direct `PropertyChanged` event invocation for bindable code-behind state.

### 6. Keep analyzers and models repo-friendly
- Analyzer implementations should avoid `Compilation.GetSemanticModel()` during analysis; prefer symbol-start plus syntax or operation callbacks using the provided semantic model to avoid `RS1030`.
- Keep telemetry model types in single-purpose files under `Models` instead of combining multiple enums and records into one file.

## Output format
Return a concise summary with:
1. the analyzer or reactive rules involved,
2. the subscription or polling semantics preserved,
3. any cache-identity or WinUI binding risk addressed,
4. the focused validation still worth running.
