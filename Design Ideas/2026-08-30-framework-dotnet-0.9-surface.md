# framework-dotnet 0.9.302 Surface — Implementation Plan

> **For agentic workers:** implement task-by-task, in order. Steps use checkbox (`- [ ]`) syntax for
> tracking. Build and test after every task; commit at the end of each.

**Goal:** Surface the 53 new public types in FrameworkDotnet 0.9.302 through SubZeroFramework's *existing*
pages — no new pages, no new nav entries.

**Architecture:** Every value follows the established five-hop pipeline: a read in
`FrameworkDataProvider` (Core, holds the `IFrameworkEcConnection`) → a Core model record → a proto message →
a service-side mapper in `TelemetryGrpcMapper` → a client-side mapper → a page ViewModel → XAML. Cheap reads
join an existing poll; expensive reads (Smart Battery I2C, per-peripheral firmware) get their own on-demand
unary RPC and are never polled.

**Tech Stack:** .NET 10, Uno Platform / WinUI 3, CommunityToolkit.Mvvm, DynamicData + System.Reactive,
gRPC/protobuf, UnitsNet, LiveCharts2, NUnit.

**Spec:** this document (the feature list was agreed in conversation on 2026-08-30; Settings-page items —
hibernate delay, PS/2 emulation, touchpad haptics/click force, touchscreen enable, fingerprint LED,
caps-lock remap, standalone mode — are explicitly **out of scope**).

## Global Constraints

- `FrameworkDotnet` is pinned at **0.9.302** in `Directory.Packages.props`. Do not bump it.
- **Every quantity goes through `IUnitFormattingService`, both directions** — including slider min/max/value.
  Never format a temperature, speed, voltage, current or power with `ToString` in a ViewModel.
- **No manual backing fields.** Use auto-properties, `[ObservableProperty]` partial properties, or `field`.
- **No revision counters.** Store derived values and assign them in a `RefreshDerivedState` method.
- **Every scrollable area gets `controls:ScrollHint.IsEnabled="True"`**, added when the scroller is created.
- **Never create `Brush` objects in ViewModel field initializers or static fields** — UI-thread affinity;
  build them at bind time via `AppThemeBrushes.Get`.
- **Use Vanara.PInvoke for any Win32**; never hand-write `DllImport`.
- Pills are a fixed `Height` plus `CornerRadius = Height / 2`. Never `CornerRadius="999"` without a height.
- Do **not** restyle the title bar or the navigation rail.
- Headings stand alone — no descriptor subtitle beside a title.
- `dotnet build` must end **0 Warnings, 0 Errors**. `dotnet test --filter "TestCategory!=Hardware"` green.
- The proto is shared: **any proto change means the service and the app must be rebuilt together.**

## Pipeline reference (read once, applies to every task)

| Hop | File |
| --- | --- |
| EC read | `SubZeroFramework.Core/Services/FrameworkDataProvider.cs` (holds `IFrameworkEcConnection`, `EnsureConnection()`) |
| Core model | `SubZeroFramework.Core/Models/*.cs` |
| Provider contract | `SubZeroFramework.Core/Services/IFrameworkDataProvider.cs` |
| Wire | `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto` |
| Service → wire | `SubZeroFramework.Service/Services/TelemetryGrpcMapper.cs` |
| RPC host | `SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs` |
| Wire → client | `SubZeroFramework/Services/Grpc*Client.cs` |
| Page VM | `SubZeroFramework/Presentation/MenuItems/<Page>/<Page>Model.cs` |

New EC surfaces hang off the connection: `connection.Diagnostics`, `.Thermal`, `.Battery`,
`.PowerDelivery`, `.PowerManagement`, `.Gpio`, `.Input`. Peripherals are a standalone
`new FrameworkPeripherals()` — **no EC connection required**, so peripheral reads must not be gated on
connection availability.

---

## Phase 1 — EC diagnostics and real throttle detection

### Task 1: `EcDiagnosticsSnapshot` Core model

**Files:**
- Create: `SubZeroFramework.Core/Models/EcDiagnosticsSnapshot.cs`
- Test: `SubZeroFramework.Tests/EcDiagnosticsSnapshotTests.cs`

**Interfaces:**
- Produces: `EcDiagnosticsSnapshot` with `bool SoftThrottled`, `bool HardThrottled`,
  `string CurrentImage`, `uint ResetFlags`, `string ResetReason`, `bool HasPanicRecord`,
  `bool LidOpen`, `bool WriteProtectDisabled`, `DateTimeOffset ObservedAt`, and
  `static EcDiagnosticsSnapshot Unavailable { get; }`.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Unavailable_ReportsNothingThrottledAndNoPanic()
{
    var snapshot = EcDiagnosticsSnapshot.Unavailable;

    Assert.Multiple(() =>
    {
        Assert.That(snapshot.SoftThrottled, Is.False);
        Assert.That(snapshot.HardThrottled, Is.False);
        Assert.That(snapshot.HasPanicRecord, Is.False);
        Assert.That(snapshot.IsAvailable, Is.False);
    });
}

/// <summary>
/// Hard throttling is the more severe state and must not be reported as merely soft.
/// </summary>
[Test]
public void ThrottleSeverity_PrefersHardOverSoft()
{
    var snapshot = new EcDiagnosticsSnapshot { SoftThrottled = true, HardThrottled = true, IsAvailable = true };

    Assert.That(snapshot.ThrottleSeverity, Is.EqualTo(EcThrottleSeverity.Hard));
}

[Test]
public void ThrottleSeverity_WithNeitherFlag_IsNone()
    => Assert.That(new EcDiagnosticsSnapshot { IsAvailable = true }.ThrottleSeverity, Is.EqualTo(EcThrottleSeverity.None));
```

- [ ] **Step 2: Run and confirm it fails** — `dotnet test SubZeroFramework.Tests --filter EcDiagnosticsSnapshot`
      Expected: does not compile, `EcDiagnosticsSnapshot` undefined.

- [ ] **Step 3: Implement the model**

```csharp
namespace SubZeroFramework.Models;

/// <summary>How hard the processor is being held back, as the EC itself reports it.</summary>
/// <remarks>
/// Distinct from an inferred performance ratio: the EC states this, so it is not confounded by a workload
/// that simply asked for less.
/// </remarks>
public enum EcThrottleSeverity
{
    None = 0,
    Soft = 1,
    Hard = 2,
}

/// <summary>Cheap, pollable health readings taken straight from the embedded controller.</summary>
public sealed record EcDiagnosticsSnapshot
{
    /// <summary>Nothing could be read — an unavailable EC, not a healthy one.</summary>
    public static EcDiagnosticsSnapshot Unavailable { get; } = new();

    public bool IsAvailable { get; init; }
    public bool SoftThrottled { get; init; }
    public bool HardThrottled { get; init; }
    public string CurrentImage { get; init; } = string.Empty;
    public uint ResetFlags { get; init; }
    public string ResetReason { get; init; } = string.Empty;
    public bool HasPanicRecord { get; init; }
    public bool LidOpen { get; init; }
    public bool WriteProtectDisabled { get; init; }
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>Hard wins: it is the more severe state and the one worth acting on.</summary>
    public EcThrottleSeverity ThrottleSeverity => HardThrottled
        ? EcThrottleSeverity.Hard
        : SoftThrottled ? EcThrottleSeverity.Soft : EcThrottleSeverity.None;
}
```

- [ ] **Step 4: Run tests** — expect PASS.
- [ ] **Step 5: Commit** — `feat(ec): add EcDiagnosticsSnapshot for EC-reported throttle and health`

### Task 2: Provider reads EC diagnostics on the existing poll

**Files:**
- Modify: `SubZeroFramework.Core/Services/IFrameworkDataProvider.cs` (add `EcDiagnosticsSnapshot GetLatestEcDiagnostics();`)
- Modify: `SubZeroFramework.Core/Services/FrameworkDataProvider.cs` (read inside `RefreshAsync`, near `EnrichConnectionStatus`)
- Modify: `SubZeroFramework.Tests/StubFrameworkDataProvider.cs` (settable `EcDiagnostics` property)

**Interfaces:**
- Consumes: `EcDiagnosticsSnapshot` from Task 1.
- Produces: `IFrameworkDataProvider.GetLatestEcDiagnostics()`.

- [ ] **Step 1:** Add to `IFrameworkDataProvider`:

```csharp
/// <summary>
/// The last EC health reading taken on the telemetry poll. Never null; reports
/// <see cref="EcDiagnosticsSnapshot.Unavailable"/> before the first successful read.
/// </summary>
EcDiagnosticsSnapshot GetLatestEcDiagnostics();
```

- [ ] **Step 2:** In `FrameworkDataProvider`, add a field and a read helper. Each sub-read is individually
      guarded: an EC that supports throttle status may still refuse panic info, and one refusal must not
      cost the others.

```csharp
private EcDiagnosticsSnapshot _ecDiagnostics = EcDiagnosticsSnapshot.Unavailable;

public EcDiagnosticsSnapshot GetLatestEcDiagnostics() => Volatile.Read(ref _ecDiagnostics);

private void RefreshEcDiagnostics(IFrameworkEcConnection connection)
{
    var throttle = TryRead(() => connection.Diagnostics.GetApThrottleStatus());
    var systemInfo = TryRead(() => connection.Diagnostics.GetSystemInfo());
    var panic = TryRead(() => connection.Diagnostics.GetPanicInfo());
    var switches = TryRead(() => connection.Diagnostics.GetSwitches());

    if (throttle is null && systemInfo is null && panic is null && switches is null)
    {
        Volatile.Write(ref _ecDiagnostics, EcDiagnosticsSnapshot.Unavailable);
        return;
    }

    Volatile.Write(ref _ecDiagnostics, new EcDiagnosticsSnapshot
    {
        IsAvailable = true,
        SoftThrottled = throttle?.SoftThrottled ?? false,
        HardThrottled = throttle?.HardThrottled ?? false,
        CurrentImage = systemInfo?.CurrentImage.ToString() ?? string.Empty,
        ResetFlags = systemInfo is null ? 0u : (uint)systemInfo.ResetFlags,
        ResetReason = systemInfo?.ResetFlags.ToString() ?? string.Empty,
        HasPanicRecord = panic?.IsValid ?? false,
        LidOpen = switches?.LidOpen ?? false,
        WriteProtectDisabled = switches?.WriteProtectDisabled ?? false,
        ObservedAt = DateTimeOffset.UtcNow,
    });
}

/// <summary>
/// Runs one EC read, returning null instead of throwing.
/// </summary>
/// <remarks>
/// Firmware coverage is uneven — a board can answer one diagnostics command and reject the next with
/// NotSupported. Letting a refusal propagate would drop four readings to punish one.
/// </remarks>
private static T? TryRead<T>(Func<T> read) where T : class
{
    try
    {
        return read();
    }
    catch (Exception)
    {
        return null;
    }
}
```

- [ ] **Step 3:** Call `RefreshEcDiagnostics(connection);` from `RefreshAsync` where the connection is already
      in hand (alongside `EnrichConnectionStatus`).
- [ ] **Step 4:** Build the service — `dotnet build SubZeroFramework.Service` — expect 0 errors, 0 warnings.
- [ ] **Step 5: Commit** — `feat(ec): poll EC throttle, reset and panic state`

### Task 3: Adaptive uses the EC's throttle signal

**Files:**
- Modify: `SubZeroFramework.Core/Models/ControlTelemetrySample.cs` (add `EcThrottleSeverity? EcThrottle`)
- Modify: `SubZeroFramework.Core/Services/Control/AdaptiveFanController.cs` (`UpdateThrottleLatch`, ~line 415)
- Modify: `SubZeroFramework.Core/Services/FrameworkDataProvider.cs` (`GetLatestControlTelemetry`)
- Test: `SubZeroFramework.Tests/AdaptiveFanControllerTests.cs`

**Interfaces:**
- Consumes: `EcThrottleSeverity` (Task 1), `GetLatestEcDiagnostics()` (Task 2).

The current detector infers throttling from `CpuPerformanceRatio < ThrottlePerformanceRatioThreshold` — a
Windows counter that also falls for power limits, parked cores and a workload that merely went idle. The EC
states it directly. The EC signal is **authoritative when present**; the ratio stays as the fallback for
firmware that does not answer.

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>
/// The EC saying "not throttled" must beat a performance ratio that merely fell because the work stopped.
/// </summary>
[Test]
public void Evaluate_WhenEcReportsNoThrottle_IgnoresALowPerformanceRatio()
{
    var controller = new AdaptiveFanController();
    var telemetry = Telemetry(performanceRatio: 0.2d, ecThrottle: EcThrottleSeverity.None);

    var decision = StepMany(controller, Calibrated(), Settings(), temperature: 90d, telemetry, ticks: 10);

    Assert.That(decision.IsThrottleLatched, Is.False);
}

[Test]
public void Evaluate_WhenEcReportsHardThrottle_LatchesImmediately()
{
    var controller = new AdaptiveFanController();
    var telemetry = Telemetry(performanceRatio: 1.0d, ecThrottle: EcThrottleSeverity.Hard);

    var decision = StepMany(controller, Calibrated(), Settings(), temperature: 90d, telemetry, ticks: 10);

    Assert.Multiple(() =>
    {
        Assert.That(decision.IsThrottleLatched, Is.True);
        Assert.That(decision.ThrottleEscalationDutyPercent, Is.GreaterThan(0d));
    });
}

/// <summary>
/// Soft throttling is a smaller emergency than hard, and must not command the same escalation.
/// </summary>
[Test]
public void Evaluate_SoftThrottleEscalatesLessThanHard()
{
    var soft = StepMany(new AdaptiveFanController(), Calibrated(), Settings(), 90d,
        Telemetry(1.0d, EcThrottleSeverity.Soft), ticks: 10);
    var hard = StepMany(new AdaptiveFanController(), Calibrated(), Settings(), 90d,
        Telemetry(1.0d, EcThrottleSeverity.Hard), ticks: 10);

    Assert.That(soft.ThrottleEscalationDutyPercent, Is.LessThan(hard.ThrottleEscalationDutyPercent));
}

/// <summary>Firmware that cannot answer must still get the old behaviour, not no behaviour.</summary>
[Test]
public void Evaluate_WhenEcThrottleIsUnknown_FallsBackToThePerformanceRatio()
{
    var decision = StepMany(new AdaptiveFanController(), Calibrated(), Settings(), 90d,
        Telemetry(0.2d, ecThrottle: null), ticks: 10);

    Assert.That(decision.IsThrottleLatched, Is.True);
}
```

- [ ] **Step 2: Run and confirm failure** — `Telemetry(...)` helper and `EcThrottle` do not exist.
- [ ] **Step 3:** Add `EcThrottleSeverity? EcThrottle { get; init; }` to `ControlTelemetrySample`, populate it
      in `GetLatestControlTelemetry` from `GetLatestEcDiagnostics()`, add the `Telemetry`/`StepMany` test
      helpers, then replace the detection head of `UpdateThrottleLatch`:

```csharp
// The EC is asked FIRST and believed when it answers. The performance ratio below is a proxy: it falls for
// power limits, parked cores and a workload that simply stopped, none of which is a thermal emergency, and
// escalating the fan for those is noise the user cannot explain. The proxy stays only for firmware that
// does not report throttling at all.
var isThrottlingNow = controlTelemetry?.EcThrottle switch
{
    EcThrottleSeverity.Hard or EcThrottleSeverity.Soft => true,
    EcThrottleSeverity.None => false,
    _ => controlTelemetry?.CpuPerformanceRatio is double ratio
        && double.IsFinite(ratio)
        && ratio < ThrottlePerformanceRatioThreshold,
};
```

      and scale the escalation by severity where `ThrottleEscalationDutyPercent` is produced:

```csharp
// Soft throttling is the firmware trimming clocks; hard throttling is it protecting the silicon. Answering
// both with the same escalation either over-reacts to the first or under-reacts to the second.
var severityScale = controlTelemetry?.EcThrottle == EcThrottleSeverity.Soft ? SoftThrottleEscalationScale : 1d;
```

      with `private const double SoftThrottleEscalationScale = 0.5d;`.
- [ ] **Step 4: Run tests** — expect PASS.
- [ ] **Step 5: Commit** — `feat(adaptive): drive throttle escalation from the EC, not a performance ratio`

### Task 4: EC health on the wire and in Warnings & Issues

**Files:**
- Modify: `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto`
- Modify: `SubZeroFramework.Service/Services/TelemetryGrpcMapper.cs`
- Modify: `SubZeroFramework.Service/Services/FrameworkStatusGrpcService.cs`
- Modify: `SubZeroFramework/Services/GrpcFrameworkStatusClient.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/WarningsIssues/WarningIssuesModel.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/WarningsIssues/WarningIssuesPage.xaml`

The Warnings page is currently a service-health page. This adds a second card below it — **"Embedded
controller"** — listing only conditions that are actually true, with an empty state when the EC is healthy.

- [ ] **Step 1:** Add to the proto, inside `FrameworkStatusReply`, using the next free field numbers:

```protobuf
// EC-reported health. Absent when no EC read has succeeded.
message EcDiagnosticsMessage {
  bool soft_throttled = 1;
  bool hard_throttled = 2;
  string current_image = 3;   // FrameworkEcCurrentImage name ("Ro" / "Rw" / "Unknown").
  uint32 reset_flags = 4;     // FrameworkEcResetFlag bits.
  string reset_reason = 5;    // Human-readable flag list.
  bool has_panic_record = 6;
  bool lid_open = 7;
  bool write_protect_disabled = 8;
}
```

- [ ] **Step 2:** Map both directions (`TelemetryGrpcMapper` → wire, `GrpcFrameworkStatusClient` → Core).
- [ ] **Step 3:** In `WarningIssuesModel`, add a `EcHealthItems` list built by a `RefreshDerivedState` method
      (no revision counters), each item a record with title, detail and severity:

```csharp
/// <summary>One EC-reported condition worth telling the user about.</summary>
/// <param name="Severity">Drives the icon and accent; matches the page's existing InfoBarSeverity vocabulary.</param>
public sealed record EcHealthItem(string Title, string Detail, InfoBarSeverity Severity);
```

      Items, added only when true:
      - Hard throttle → Error, *"The processor is being held back to protect the hardware."*
      - Soft throttle → Warning, *"The processor is running below its full clocks."*
      - `HasPanicRecord` → Warning, *"The embedded controller recorded a panic. It restarted itself."*
      - `WriteProtectDisabled` → Informational, *"Firmware write protection is off."*
      - `CurrentImage == "Ro"` → Informational, *"The controller is running its read-only image."*
- [ ] **Step 4:** Add the card to the XAML with an `ItemsRepeater`, an empty state reading
      *"Nothing to report"*, and `controls:ScrollHint.IsEnabled="True"` on the page scroller if not present.
- [ ] **Step 5:** Build both heads, run tests, **commit** — `feat(warnings): report EC throttle, panic and image state`

---

## Phase 2 — Firmware sensor names and thermal thresholds

### Task 5: `ThermalSensorMetadata` Core model and cached provider read

**Files:**
- Create: `SubZeroFramework.Core/Models/ThermalSensorMetadata.cs`
- Modify: `SubZeroFramework.Core/Services/IFrameworkDataProvider.cs`
- Modify: `SubZeroFramework.Core/Services/FrameworkDataProvider.cs`
- Test: `SubZeroFramework.Tests/ThermalSensorMetadataTests.cs`

**Interfaces:**
- Produces: `ThermalSensorMetadata` (`int SensorIndex`, `string FirmwareName`, `string MappedName`,
  `string SensorType`, `Temperature? Warn/High/Halt/FanOff/FanMax`, `bool HasThresholds`) and
  `IReadOnlyList<ThermalSensorMetadata> GetThermalSensorMetadata()`.

Sensor names and thresholds are **static for the life of the connection** — the library even exposes
`ClearSensorNameCache()`. Read them **once**, on the first successful poll, and cache; re-read only when the
connection is re-opened.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// The firmware's own name wins when it has one. The mapped enum is a fallback for boards that return
/// nothing, and "Temp 3" is the last resort — a position, not a name.
/// </summary>
[Test]
public void DisplayName_PrefersFirmwareNameOverMappedName()
{
    var metadata = new ThermalSensorMetadata { SensorIndex = 3, FirmwareName = "APU_SoC", MappedName = "Generic" };

    Assert.That(metadata.DisplayName, Is.EqualTo("APU_SoC"));
}

[Test]
public void DisplayName_WithoutAFirmwareName_UsesTheMappedName()
    => Assert.That(new ThermalSensorMetadata { SensorIndex = 1, MappedName = "F75303Cpu" }.DisplayName, Is.EqualTo("F75303Cpu"));

[Test]
public void DisplayName_WithNeither_FallsBackToThePosition()
    => Assert.That(new ThermalSensorMetadata { SensorIndex = 2 }.DisplayName, Is.EqualTo("Temp 2"));

[Test]
public void HasThresholds_IsFalseWhenTheFirmwareReportedNone()
    => Assert.That(new ThermalSensorMetadata { SensorIndex = 0 }.HasThresholds, Is.False);

[Test]
public void HasThresholds_IsTrueWhenAnyThresholdIsPresent()
    => Assert.That(new ThermalSensorMetadata { SensorIndex = 0, HighCelsius = 95d }.HasThresholds, Is.True);
```

- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3:** Implement the model (thresholds stored as `double?` Celsius — canonical units; the
      *display* conversion is the ViewModel's job through `IUnitFormattingService`), then the cached read:

```csharp
private IReadOnlyList<ThermalSensorMetadata> _thermalSensorMetadata = [];

public IReadOnlyList<ThermalSensorMetadata> GetThermalSensorMetadata() => Volatile.Read(ref _thermalSensorMetadata);

/// <summary>
/// Reads every sensor's firmware name and thresholds ONCE per connection.
/// </summary>
/// <remarks>
/// These do not change while the machine is running, and each one is a separate EC round trip. Polling them
/// would add a dozen transactions per tick to learn nothing new.
/// </remarks>
private void RefreshThermalSensorMetadata(IFrameworkEcConnection connection, int sensorCount)
{
    if (_thermalSensorMetadata.Count == sensorCount && sensorCount > 0)
    {
        return;
    }

    var metadata = new List<ThermalSensorMetadata>(sensorCount);
    for (var index = 0; index < sensorCount; index++)
    {
        var name = TryRead(() => connection.Thermal.GetSensorName((byte)index));
        var thresholds = TryRead(() => connection.Thermal.GetThresholds((byte)index));
        metadata.Add(new ThermalSensorMetadata
        {
            SensorIndex = index,
            FirmwareName = name?.FirmwareName ?? string.Empty,
            MappedName = name?.MappedName.ToString() ?? string.Empty,
            SensorType = name?.SensorType.ToString() ?? string.Empty,
            WarnCelsius = thresholds?.Warn?.DegreesCelsius,
            HighCelsius = thresholds?.High?.DegreesCelsius,
            HaltCelsius = thresholds?.Halt?.DegreesCelsius,
            FanOffCelsius = thresholds?.FanOff?.DegreesCelsius,
            FanMaxCelsius = thresholds?.FanMax?.DegreesCelsius,
        });
    }

    Volatile.Write(ref _thermalSensorMetadata, metadata);
}
```

      Clear the cache (`_thermalSensorMetadata = []`) wherever `_connection` is set to null, so a reconnect
      re-reads.
- [ ] **Step 4: Run tests** — expect PASS.
- [ ] **Step 5: Commit** — `feat(thermal): read firmware sensor names and thresholds once per connection`

### Task 6: Sensor metadata on the wire, and firmware names in the UI

**Files:**
- Modify: `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto`
- Modify: `SubZeroFramework.Service/Services/TelemetryGrpcMapper.cs`
- Modify: `SubZeroFramework/Services/GrpcTelemetryClient.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/ThermalTelemetry/ThermalTelemetryModel.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/FanCurveProfiles/FanCurveProfilesModel.cs` (sensor chips)

- [ ] **Step 1:** Add the message and carry it on the existing `TelemetryChannel` stream:

```protobuf
// Static, per-sensor firmware metadata. Read once per connection; never polled.
message ThermalSensorMetadataMessage {
  int32 sensor_index = 1;
  string firmware_name = 2;
  string mapped_name = 3;
  string sensor_type = 4;
  optional double warn_celsius = 5;
  optional double high_celsius = 6;
  optional double halt_celsius = 7;
  optional double fan_off_celsius = 8;
  optional double fan_max_celsius = 9;
}
```

- [ ] **Step 2:** Map both directions.
- [ ] **Step 3:** Use `DisplayName` for the sensor chips on the Thermal page and in the fan editor's
      "Driving temperature" chip row, falling back to today's label when metadata is absent.
- [ ] **Step 4:** Build both heads; run the app and confirm a chip reads the firmware name.
- [ ] **Step 5: Commit** — `feat(thermal): label sensors with their firmware names`

### Task 7: Threshold reference lines and an Adaptive target bound

**Files:**
- Modify: `SubZeroFramework/Presentation/MenuItems/ThermalTelemetry/ThermalTelemetryPage.xaml`
- Modify: `SubZeroFramework/Presentation/MenuItems/ThermalTelemetry/ThermalTelemetryModel.cs`
- Modify: `SubZeroFramework/Controls/FanCurveProfiles/Models/Modes/FanAdaptiveModeModel.cs`
- Test: `SubZeroFramework.Tests/AdaptiveTargetBoundTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// A target above the firmware's warning point is a target the machine will never be allowed to hold.
/// </summary>
[Test]
public void TargetCeiling_IsTheFirmwareWarnPoint_WhenTheDrivingSensorReportsOne()
{
    var model = NewAdaptiveModel(sensorMetadata: new ThermalSensorMetadata { SensorIndex = 0, WarnCelsius = 95d });

    Assert.That(model.TargetCeilingCelsius, Is.EqualTo(95d).Within(1e-9d));
}

[Test]
public void TargetCeiling_WithoutFirmwareThresholds_KeepsTheDefaultMaximum()
{
    var model = NewAdaptiveModel(sensorMetadata: null);

    Assert.That(model.TargetCeilingCelsius, Is.EqualTo(AdaptiveFanSettings.MaximumTargetTemperatureCelsius).Within(1e-9d));
}
```

- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3:** Add `TargetCeilingCelsius`, clamp the target slider's maximum to it (converted through
      `IUnitFormattingService` for display — the slider's min/max/value all go through it), and add a caption
      naming the bound. On the Thermal chart, add horizontal reference lines for Warn/High/Halt using
      LiveCharts `Sections`, labelled through the unit service.
- [ ] **Step 4: Run tests; build; confirm the chart lines render in both themes.**
- [ ] **Step 5: Commit** — `feat(thermal): show firmware warn/high/halt and bound the Adaptive target`

---

## Phase 3 — Per-port USB-C power contract

### Task 8: Negotiated contract on `PowerDeliveryPortSnapshot`

**Files:**
- Modify: `SubZeroFramework.Core/Models/PowerDeliveryPortSnapshot.cs`
- Modify: `SubZeroFramework.Core/Services/FrameworkDataProvider.cs` (`BuildPowerDeliverySnapshot`, ~line 2402)
- Modify: `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto` (`PowerDeliveryPortState`, next free number is 21)
- Modify: `SubZeroFramework.Service/Services/TelemetryGrpcMapper.cs`
- Modify: `SubZeroFramework/Services/GrpcTelemetryClient.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/PowerTelemetry/PowerTelemetryPage.xaml`
- Test: `SubZeroFramework.Tests/PowerDeliveryPortSnapshotTests.cs`

The existing fields are **static board capability** (`MaxChargeWatts`, `SupportsCharging`) plus **live
state** (`VoltageVolts`, `CurrentAmperes`). `GetPowerInfo(port)` adds the third thing: the **negotiated
contract** — what this cable and charger actually agreed on. All three together answer "why is this only
charging at 45 W".

New fields: `bool SupportsDualRole`, `string UsbPowerRole`, `string ChargingType`,
`double? NegotiatedMaximumVoltageVolts`, `double? NegotiatedMaximumCurrentAmperes`,
`double? NegotiatedMaximumPowerWatts`, `double? CurrentLimitAmperes`.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// The headline number a user wants is what the port NEGOTIATED, falling back to what the board can do.
/// </summary>
[Test]
public void EffectiveMaximumPowerWatts_PrefersTheNegotiatedContract()
{
    var port = NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 45d);

    Assert.That(port.EffectiveMaximumPowerWatts, Is.EqualTo(45d).Within(1e-9d));
}

[Test]
public void EffectiveMaximumPowerWatts_WithoutAContract_UsesTheBoardCapability()
{
    var port = NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: null);

    Assert.That(port.EffectiveMaximumPowerWatts, Is.EqualTo(100d).Within(1e-9d));
}

/// <summary>
/// A port negotiating far below what the board supports is the interesting case — a weak charger or a
/// cable that cannot carry the contract — and it is worth flagging rather than leaving to arithmetic.
/// </summary>
[Test]
public void IsNegotiatingBelowCapability_IsTrueWhenTheContractIsMaterillyLower()
{
    Assert.That(NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 45d).IsNegotiatingBelowCapability, Is.True);
    Assert.That(NewPort(maxChargeWatts: 100, negotiatedMaximumPowerWatts: 100d).IsNegotiatingBelowCapability, Is.False);
}
```

- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3:** Add the fields and the two derived members, then read per port in
      `BuildPowerDeliverySnapshot`, guarded — a non-PD slot answers with a refusal, and that must degrade to
      nulls, not fail the whole snapshot:

```csharp
// Per port, and individually guarded: a slot with no PD controller behind it refuses this command, and on
// this chassis some slots always will. A refusal means "no contract to report", not "the read failed".
var powerInfo = TryRead(() => connection.PowerDelivery.GetPowerInfo(slot.SlotIndex));
```

- [ ] **Step 4: Run tests; build both heads.**
- [ ] **Step 5:** Extend the **USB-C Power Delivery** card: per port show charging type, role (with a
      dual-role marker), and `negotiated / capability` watts, with the shortfall called out when
      `IsNegotiatingBelowCapability`. Every quantity through `IUnitFormattingService`.
- [ ] **Step 6: Commit** — `feat(power): show the negotiated USB-C contract beside board capability`

---

## Phase 4 — Smart battery health, on demand

### Task 9: `SmartBatterySnapshot` Core model

**Files:**
- Create: `SubZeroFramework.Core/Models/SmartBatterySnapshot.cs`
- Test: `SubZeroFramework.Tests/SmartBatterySnapshotTests.cs`

**Interfaces:**
- Produces: `SmartBatterySnapshot` with pack identity (`SerialNumber`, `ManufactureDate`, `DeviceName`,
  `ManufacturerName`, `Chemistry`), live values (`TemperatureCelsius`, `VoltageVolts`, `CurrentAmperes`,
  `CycleCount`, `RelativeStateOfChargePercent`), four `CellVoltageVolts_1..4`, requested charge
  (`ChargingVoltageVolts`, `ChargingCurrentAmperes`), `bool IsUnsealed`, and
  `double? StateOfHealthEnergyWattHours`.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// Cell imbalance is the early sign of a pack failing, and the number that matters is the SPREAD.
/// </summary>
[Test]
public void CellImbalanceVolts_IsTheSpreadBetweenTheHighestAndLowestCell()
{
    var snapshot = NewPack(3.95d, 3.95d, 3.72d, 3.96d);

    Assert.That(snapshot.CellImbalanceVolts, Is.EqualTo(0.24d).Within(1e-6d));
}

/// <summary>A pack reporting no cell voltages at all must not report a fabricated perfect balance.</summary>
[Test]
public void CellImbalanceVolts_WithNoCellReadings_IsNull()
    => Assert.That(NewPack(0d, 0d, 0d, 0d).CellImbalanceVolts, Is.Null);

[Test]
public void AgeInDays_IsNullWithoutAManufactureDate()
    => Assert.That(NewPack(3.9d, 3.9d, 3.9d, 3.9d) with { ManufactureDate = null }, Has.Property("AgeInDays").Null);
```

- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3: Implement**, including:

```csharp
/// <summary>
/// The spread between the highest and lowest cell. Null when the pack reported no cell voltages.
/// </summary>
/// <remarks>
/// Reported as a spread rather than four numbers because the spread is the diagnosis. A pack whose cells
/// have drifted apart is failing regardless of what the total says, and the total is what every other
/// readout in the app already shows.
/// </remarks>
public double? CellImbalanceVolts
{
    get
    {
        double[] cells = [CellVoltageVolts_1, CellVoltageVolts_2, CellVoltageVolts_3, CellVoltageVolts_4];
        var live = cells.Where(static volts => volts > 0d).ToArray();
        return live.Length < 2 ? null : live.Max() - live.Min();
    }
}
```

- [ ] **Step 4: Run tests** — expect PASS.
- [ ] **Step 5: Commit** — `feat(power): add SmartBatterySnapshot with cell-imbalance and pack age`

### Task 10: On-demand `GetSmartBattery` RPC

**Files:**
- Modify: `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto`
- Modify: `SubZeroFramework.Service/Services/FrameworkTelemetryGrpcService.cs`
- Modify: `SubZeroFramework.Core/Services/IFrameworkDataProvider.cs` + `FrameworkDataProvider.cs`
- Modify: `SubZeroFramework/Services/GrpcTelemetryClient.cs`
- Test: `SubZeroFramework.Tests/SmartBatteryRpcTests.cs`

**This one must never be polled.** The library documents it as many I2C round trips. It is a unary RPC,
triggered by the user, rate-limited in the service.

- [ ] **Step 1:** Add the RPC and message:

```protobuf
// On demand ONLY. Costs many I2C round trips to the pack; never place it on a timer.
rpc GetSmartBattery (GetSmartBatteryRequest) returns (SmartBatteryReply);
```

- [ ] **Step 2:** Implement `Task<SmartBatterySnapshot?> ReadSmartBatteryAsync(CancellationToken)` on the
      provider, with a minimum interval between reads:

```csharp
/// <summary>
/// The soonest another pack read is allowed.
/// </summary>
/// <remarks>
/// The read is slow and holds the I2C passthrough while it runs, so a user leaning on a refresh button
/// could stall ordinary telemetry behind it. Repeats inside the window return the cached answer.
/// </remarks>
private static readonly TimeSpan SmartBatteryMinimumInterval = TimeSpan.FromSeconds(15);
```

- [ ] **Step 3:** Write the test that a second call inside the window returns the cached snapshot without a
      second EC read (assert on a call counter in `StubFrameworkDataProvider`).
- [ ] **Step 4: Run tests; build both heads.**
- [ ] **Step 5: Commit** — `feat(power): add an on-demand smart battery read`

### Task 11: Battery health section on Power Telemetry

**Files:**
- Modify: `SubZeroFramework/Presentation/MenuItems/PowerTelemetry/PowerTelemetryModel.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/PowerTelemetry/PowerTelemetryPage.xaml`

- [ ] **Step 1:** Add an expander below the existing **Battery** card titled **"Pack health"**, collapsed by
      default, with a refresh button bound to an `AsyncRelayCommand`. Show a `ProgressRing` while reading and
      an `InfoBar` (Error) on failure — never both at once.
- [ ] **Step 2:** Content: four cell-voltage bars with the imbalance called out; pack age from
      `ManufactureDate` beside cycle count; pack-reported state of health beside the existing "Wear since
      new"; and "requesting X V / Y A" from the charging registers. All values through
      `IUnitFormattingService`.
- [ ] **Step 3:** Empty state when the pack is sealed: *"This pack does not publish its detailed health
      registers."* — phrase it as what the user can expect, not as a system failure.
- [ ] **Step 4:** `controls:ScrollHint.IsEnabled="True"` on the section scroller.
- [ ] **Step 5: Commit** — `feat(power): add a pack health section with cell balance and age`

---

## Phase 5 — Firmware versions

### Task 12: `FirmwareInventorySnapshot` and its on-demand read

**Files:**
- Create: `SubZeroFramework.Core/Models/FirmwareInventorySnapshot.cs`
- Modify: `SubZeroFramework.Core/Services/IFrameworkDataProvider.cs` + `FrameworkDataProvider.cs`
- Modify: `SubZeroFramework.GrpcContracts/Protos/framework_telemetry.proto`
- Modify: `SubZeroFramework.Service/Services/HardwareInfoGrpcService.cs`
- Modify: `SubZeroFramework/Services/GrpcHardwareInfoClient.cs`
- Test: `SubZeroFramework.Tests/FirmwareInventorySnapshotTests.cs`

**Interfaces:**
- Produces: `FirmwareInventorySnapshot` with `IReadOnlyList<FirmwareComponent> Cameras/InputModules/UsbHubs/AudioCards`,
  `IReadOnlyList<FirmwareComponent> PowerDeliveryControllers`, `string RetimerVersion`,
  and `IReadOnlyList<NvmeFirmware> NvmeDrives`, where
  `FirmwareComponent(int SlotIndex, string ProductName, string Version, ushort VendorId, ushort ProductId)`
  and `NvmeFirmware(string DevicePath, string ModelNumber, string FirmwareVersion)`.

Peripheral reads go through `new FrameworkPeripherals()` and need **no EC connection** — do not gate them on
one. PD controller and retimer versions do need the connection.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>Slots the firmware reports as absent are not components and must not be listed.</summary>
[Test]
public void FromPeripheralVersions_SkipsAbsentSlots()
{
    var snapshot = FirmwareInventorySnapshot.FromPeripheralVersions(
        cameras: [Present(0, "RGB Camera", 1, 2, 3), Absent(1)],
        inputModules: [], usbHubs: [], audioCards: []);

    Assert.That(snapshot.Cameras, Has.Count.EqualTo(1));
    Assert.That(snapshot.Cameras[0].ProductName, Is.EqualTo("RGB Camera"));
}

[Test]
public void FirmwareComponent_VersionIsMajorMinorSubMinor()
    => Assert.That(FirmwareComponent.FormatVersion(1, 2, 3), Is.EqualTo("1.2.3"));
```

- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3: Implement**, carried on the existing `HardwareInfoReply` (Device Capabilities already
      consumes it), reading peripherals unconditionally and PD versions only with a connection.
- [ ] **Step 4: Run tests; build both heads.**
- [ ] **Step 5: Commit** — `feat(devicecaps): collect peripheral, PD and NVMe firmware versions`

### Task 13: Firmware versions in Device Capabilities and Modules

**Files:**
- Modify: `SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesModel.cs`
- Modify: `SubZeroFramework/Presentation/MenuItems/DeviceCapabilities/DeviceCapabilitiesStorageDriveDetailView.xaml`
- Modify: `SubZeroFramework/Presentation/MenuItems/Modules/ModulesFw16View.xaml` (+ the Fw13/Fw13Pro/Fw12 views)

- [ ] **Step 1:** Add **Firmware** to the storage-drive detail view from `NvmeDrives`, matched on device path.
- [ ] **Step 2:** Add a **Firmware** section to Device Capabilities listing cameras, USB hubs, audio cards,
      PD controllers and the retimer — each row product name + version, omitting empty groups entirely
      rather than rendering empty headers.
- [ ] **Step 3:** On the Modules pages, show the input-module version on each occupied slot, matched by slot
      index against `InputModules`.
- [ ] **Step 4:** Build; confirm every page renders with the service running and degrades to no section when
      firmware data is absent.
- [ ] **Step 5: Commit** — `feat(modules): show input module and peripheral firmware versions`

---

## Task 14: Documentation and release notes

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `README.md`
- Modify: `docs/FunctionalitySpecification.md`

- [ ] **Step 1:** Add an `### Added` block under `## [0.2.2]` covering each phase in user-facing language.
- [ ] **Step 2:** Note in the CHANGELOG that the proto changed, so **service and app must be rebuilt together**.
- [ ] **Step 3:** Build everything, run the full suite, **commit**.

---

## Self-review

**Spec coverage.** Throttle → Tasks 1–4. Thresholds → 5–7. Sensor names → 5–6. PD power info → 8. Smart
battery → 9–11. Panic + system info → 1–4. Peripheral/PD/NVMe versions → 12–13. Settings items — absent by
design.

**Placeholders.** None: every step names files, types and code.

**Type consistency.** `EcDiagnosticsSnapshot`/`EcThrottleSeverity` (1) are consumed by 2–4.
`ThermalSensorMetadata` (5) by 6–7. `TryRead<T>` is introduced in Task 2 and reused in 5 and 8 — Task 5 and
Task 8 must not redefine it. `PowerDeliveryPortSnapshot` gains fields in 8 only.
`FirmwareComponent`/`NvmeFirmware` (12) are consumed by 13.

**Known risk.** Hardware-dependent: several commands may return NotSupported on any given board. Every read
is individually guarded and degrades to an absent section rather than an error.
