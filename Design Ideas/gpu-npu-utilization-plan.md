# GPU & NPU utilization — implementation plan

Status: **ALL PHASES IMPLEMENTED, 2026-07-26**, shipping in 0.1.5 — Windows GPU + NPU (phase 1), Linux AMD
(2), NVIDIA and Intel GPUs (3), Linux NPUs (4). Nothing on the Linux side has yet run on real Framework
hardware; the parsers are pinned against captured data and the failure paths are covered, but the readings
themselves await field verification.

Phase-4 outcome, which the plan asked to research before committing to:

- **Intel (ivpu): shipped.** `npu_busy_time_us` is a cumulative busy-microsecond counter that costs nothing
  to read and does not resume a suspended NPU. The kernel's own documentation asks for ~1 s sampling, which
  is the telemetry tier's cadence. Semantically it is a queue-non-empty duty cycle, not occupancy.
- **AMD (XDNA): shipped, narrowly.** Per-column utilization arrives through a sensor ioctl and is available
  only on kernels new enough to carry it, with AMD's platform-management driver bound, on Strix/Krackan
  parts. Adversarial verification overturned the research's headline claim here: the query DOES resume the
  NPU (the runtime-PM reference lives in the driver callback, not the ioctl entry point), so the reader is
  gated on the device already being awake and reports a suspended NPU as 0% without touching it.

Implementation deviations from this document:

- **Interop is via `Vanara.PInvoke.Pdh` / `Vanara.PInvoke.SetupAPI`** (MIT, verified on nuget.org) rather than
  hand-written P/Invoke — a standing project rule adopted mid-implementation. Two deliberate raw-call
  exceptions are documented in the readers (Vanara 5.0.5 has no managed counter-array wrapper, and its
  friendly property-read overload would defeat the buffer guards).
- **`Running Time` is `PERF_COUNTER_LARGE_RAWCOUNT` in 100 ns units** — verified by reproducing Windows' own
  Utilization Percentage to the digit. Percentages come from Running Time deltas over `Stopwatch` elapsed
  time; first sample and newly appeared adapters report nothing rather than zeros.
- **Engine type names can contain spaces** (`engtype_video codec engine`, `engtype_compute 0`) and are all
  lowercase on current builds — the instance-name parser handles both, with tests.
- **Counter adapters without a PnP device (WARP / Basic Render Driver) are dropped**, not published under a
  synthesized name — this machine carries one with ~250 instances.
- Channel identity: device instance path via SetupAPI (`DEVPKEY_Gpu_Luid` correlates to counters per session);
  the UI keeps a card greyed when its device goes unavailable.

Originally written 2026-07-25 for 0.1.5+.

Evidence marks:
- **[MEASURED]** — verified on the dev machine (Framework 16, Ryzen AI 9 HX 370: Radeon 890M iGPU + NVIDIA
  RTX 5070 Laptop GPU module + XDNA2 NPU, Windows).
- **[KNOWN]** — established interface; re-read the primary doc while implementing.
- **[UNVERIFIED]** — not confirmed. A research task, not a fact.

## Corrections to the first draft of this document

1. **NVIDIA is a Framework option.** The Framework 16 ships an RTX 5070 graphics module, and the dev machine
   has one — `Win32_VideoController` lists *AMD Radeon(TM) 890M Graphics* **and** *NVIDIA GeForce RTX 5070
   Laptop GPU*. **[MEASURED]** The earlier claim that NVIDIA was eGPU-only was wrong and was fed to the
   research agents, so treat any NVIDIA-shaped gap in their output as unexamined rather than settled.
2. **Cost is not a problem on Windows.** The first draft flagged 1164 PDH instances as a possible repeat of the
   lshw incident. Measured, it is not — see §4.

---

## 1. Recommendation

| Target | Approach | Source |
|---|---|---|
| **Windows GPU (all vendors)** | DIY, native **PDH** with a persistent query | `\GPU Engine(*)\Utilization Percentage` **[MEASURED]** |
| **Windows NPU** | Same query, same code path | the NPU is its own compute-only adapter **[MEASURED]** |
| **Adapter naming (Windows)** | **WmiLight** (already in our closure) | `Win32_VideoController` + `Win32_PnPEntity` **[MEASURED]** |
| **Linux GPU (AMD)** | DIY, one sysfs read | `/sys/class/drm/card*/device/gpu_busy_percent` **[KNOWN]** |
| **Linux GPU (NVIDIA)** | DIY, **NVML P/Invoke** (not `nvidia-smi`) | `libnvidia-ml.so.1` **[KNOWN]** |
| **Linux GPU (Intel)** | Defer — i915/xe PMU or Level Zero Sysman | no sysfs equivalent **[UNVERIFIED]** |
| **Linux NPU (Intel)** | DIY, one sysfs read | `/sys/class/accel/accel*/device/npu_busy_time_us` **[KNOWN]** |
| **Linux NPU (AMD)** | DIY, **ioctl** — sysfs exposes nothing | `DRM_AMDXDNA_QUERY_SENSORS` **[KNOWN]** |

**No new NuGet dependency is needed.** The one package worth using — WmiLight — we already ship.

---

## 2. WmiLight: yes, but for naming, not for sampling

WmiLight arrives transitively through `Hardware.Info.Aot` 110.0.0.1 (`Directory.Packages.props:32`), so its
licence is already in the build-time report and it is AOT-friendly, which matters for the deferred NativeAOT
goal. It is the right tool for **inventory**, and the wrong one for **sampling**:

| Transport | Per sample | Instances |
|---|---|---|
| Native PDH, persistent query, full wildcard | **1.42 ms** | 690 |
| Native PDH, filtered to one adapter LUID | **1.02 ms** | 3 |
| WMI `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine` | **~345 ms** | 690 |

**[MEASURED]**, 10 iterations each. The WMI class exposes exactly the same instance names and values — it is
the same data — but ~240× the cost. At the 1 s tier that is a third of a core, i.e. the lshw mistake with a
different name. (Some of the 345 ms is PowerShell marshalling; WmiLight will be faster. It will not be 1.4 ms.)

So: **PDH for the per-second numbers, WmiLight for the once-per-connection inventory.**

### 2.1 NuGet survey result — verified on nuget.org, 2026-07-25

| Need | Package situation |
|---|---|
| Linux **AMD** utilization | **Nothing exists.** `amdgpu`, `amdsmi`, `adlx`, `gpu_busy_percent`, `drm gpu linux` → 0 packages each |
| Linux **Intel** utilization | **Nothing exists.** `LevelZero.NET` binds compute kernels; it does not bind Sysman `zesEngineGetActivity` |
| Linux **NVIDIA** utilization | `ManagedCuda-Nvml.NETStandard` works (ships a `libnvml.so` shim → `libnvidia-ml.so.1`) but is **frozen at 2018**, carries NVIDIA's proprietary licence, and drags in the CUDA core wrapper |
| **NPU** utilization | **Nothing exists, on any OS, from any package** |
| `LibreHardwareMonitorLib` | **Windows-gated for all three GPU groups** — on Linux it silently returns zero GPUs. No NPU support at all ([issue #1728](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/1728)) |
| `Hardware.Info(.Aot)` (already ours) | `VideoController` has **no utilization property on any OS** — inventory only |

One package is worth watching: **`HardwareInfo.Gpu.Nvidia`** (MIT, actively maintained, net10.0, `LibraryImport`-based) already binds `nvmlDeviceGetUtilizationRates` — but hardcodes `nvml.dll` with no resolver, and its README lists "Linux support" as TODO. A single `NativeLibrary.SetDllImportResolver` mapping to `libnvidia-ml.so.1` separates it from working; upstreaming that PR may cost less than owning the binding.

### 2.2 GPU-T — reference, not dependency

[lseurttyuu/GPU-T](https://github.com/lseurttyuu/GPU-T): MIT, .NET 9, Avalonia, Linux-only, and **a standalone app rather than a library**, so there is nothing to take a dependency on. Verified by cloning and grepping the source, because its README undersells what it does:

| Vendor | What it actually reads | Verdict |
|---|---|---|
| **AMD** | `gpu_busy_percent` + `mem_busy_percent` from sysfs, each gated on `File.Exists` | **Independent confirmation of our approach.** Worth borrowing the availability-probe pattern (`HasGpuLoad` set by probing for the file); there is no code worth copying for a two-line read |
| **NVIDIA** | `nvidia-smi --query-gpu=…utilization.gpu,utilization.memory…` **per sample** | **Do not copy.** A subprocess per poll is precisely the `lshw` mistake. Use NVML P/Invoke instead |
| **Intel** | `LinuxIntelGpuProbe.cs` hardcodes `GpuLoad = 0`; its own comment says integrated GPUs have "no fan, power, VRAM, or **load** metrics" | **Not covered** — matches its README, "[ ] Intel Arc: implementation planned" |

So GPU-T covers AMD and NVIDIA utilization; **Intel utilization is a stub returning zero**. MIT (© 2026 lseurttyuu) permits reuse with attribution — anything vendored would go into the licence report the way the vendored natives already do. The genuinely useful takeaway is its **per-vendor probe architecture**, which maps cleanly onto `IGpuUtilizationReader`.

---

## 3. What we read, exactly

### 3.1 Windows — one query for GPU *and* NPU **[MEASURED]**

`\GPU Engine(*)\Utilization Percentage`, instance names shaped:

```
pid_2908_luid_0x00000000_0x000163A4_phys_0_eng_0_engtype_3D
```

Adapters observed on the dev machine:

| LUID | Engines | Identity |
|---|---|---|
| `0x000163A4` | 3D, Copy | active during the sample |
| `0x000189AE` | 3D | idle |
| `0x00018A19` | 3D, Copy, JPEG_Decode_0, OFA_0, Security, VideoDecode, VideoEncode, VR | full GPU engine set |
| `0x00019D9F` | **Compute only** (3–6 instances) | **the NPU** |

There is **no NPU counter set** — the only GPU/NPU sets present are `GPU Engine`, `GPU Adapter Memory`,
`GPU Local/Non Local Adapter Memory`, `GPU Process Memory`. **[MEASURED]** The NPU appears in `GPU Engine` as
its own adapter because Windows enumerates it as an MCDM compute-only device. One reader covers both.

Rules that fall out of the instance format:

- Instances are **per process** and transient — never cache the instance list; re-read the array each collect.
- A device's utilization is the **sum over its instances per engine type**, then the **max across engine
  types** (engines run concurrently, so summing all of them exceeds 100%). This is what Task Manager shows.
- Use `PdhAddEnglishCounterW`, not the localized variant, so a non-English Windows still resolves the path.
- Keep the query **open** across samples. Reopening it is where the cost hides (`Get-Counter`, which reopens,
  measured 3,015 ms/sample against PDH's 1.42 ms).

### 3.2 Windows — naming, via WmiLight **[MEASURED]**

PDH gives a LUID and no name. Sources for the name:

- **GPUs**: `Win32_VideoController` → `Name`, `PNPDeviceID` (e.g. `PCI\VEN_1002&DEV_150E…`, `PCI\VEN_10DE&DEV_2D58…`).
- **NPU**: NOT in `Win32_VideoController`. It is `Win32_PnPEntity` with **`PNPClass = 'ComputeAccelerator'`** —
  on the dev machine *"NPU Compute Accelerator Device"*, `PCI\VEN_1022&DEV_17F0` (AMD XDNA2).

**LUID → device is SOLVED. [MEASURED]** The device property store carries the LUID directly:

```
DEVPKEY_Gpu_Luid   {60B193CB-5276-4D0F-96FC-F173ABAD3EC6},2
DEVPKEY_Gpu_PhyId  {60B193CB-5276-4D0F-96FC-F173ABAD3EC6},3
```

On the dev machine the NPU device reports `DEVPKEY_Gpu_Luid = 105887 = 0x19D9F`, which is exactly the
`luid_0x00000000_0x00019D9F` seen in the counter instance names. So:

1. `SetupDiGetClassDevs` over the **ComputeAccelerator** class GUID `{F01A9D53-3FF6-48D2-9F97-C8A7004BE10C}`
   (or DXCore: `D3D12_CORE_COMPUTE` && !`D3D12_GRAPHICS`) → NPU adapters; the display class → GPUs.
2. Read `DEVPKEY_Gpu_Luid` + `DEVPKEY_Gpu_PhyId` per device.
3. Match `luid_0x…_0x{luid:X8}_phys_{phyid}_` against the counter instances, and take the friendly name from
   the same device.

This is also Microsoft's own recommended route — with the caveat that they state there is **no officially
published NPU counter-set name and no official API**, and that this replicates Task Manager's internal logic
([MS Q&A](https://learn.microsoft.com/en-us/answers/questions/1700210/how-to-read-and-output-the-npu-utilization)).
Treat the counter layout as undocumented: gate it defensively and degrade to "unavailable" rather than
throwing when a future Windows build changes it.

**Use `Running Time`, not `Utilization Percentage`. [MEASURED]** On the dev machine the NPU's
`Utilization Percentage` cooked to `0.00` while its `Running Time` was clearly accumulating
(`RawValue 11861`, `83059`). Task Manager derives the percentage from the **`Running Time` delta over elapsed
wall-clock**, and so must we:

```
busy% = (Σ RunningTime_now − Σ RunningTime_prev) / elapsedWallClock × 100
```

per adapter+engine type, then max across engine types. This supersedes the simpler
"read Utilization Percentage" in the earlier draft — that field is unreliable for the NPU at least.

### 3.3 Linux **[KNOWN / UNVERIFIED]**

Linux has no single interface equivalent to Windows' `GPU Engine`. Each vendor is its own reader, and
enumeration is per DRM card — which is also how multiple devices are found.

**Enumeration (all vendors):** walk `/sys/class/drm/card*` — `card0`, `card1`, … one per GPU. For each:

```
/sys/class/drm/card<N>/device/vendor        # 0x1002 AMD, 0x8086 Intel, 0x10de NVIDIA
/sys/class/drm/card<N>/device/device        # PCI device id
/sys/class/drm/card<N>/device/uevent        # PCI_SLOT_NAME=0000:c1:00.0  <- stable key
```

The **PCI address** (`PCI_SLOT_NAME`) is the stable identity: `card<N>` numbering can change across boots, so
never key a channel on it. On the dev machine's Linux equivalent that yields two cards — the Radeon 890M iGPU
and the RTX 5070 module — which must appear as two separate readouts.

- **AMD** `[KNOWN]`: `/sys/class/drm/card<N>/device/gpu_busy_percent` — 0–100, one tiny read, no root. Also
  `mem_busy_percent`. Works for both the iGPU and a discrete AMD module; they are simply two cards.
- **NVIDIA** `[KNOWN]`: no sysfs equivalent — **NVML** (`libnvidia-ml.so.1`) is the only supported source,
  present with the proprietary driver and NVIDIA's open kernel modules, **absent under nouveau**. Enumerate
  `nvmlDeviceGetCount` → `nvmlDeviceGetHandleByIndex` → `nvmlDeviceGetUtilizationRates`, and **match NVML
  devices back to DRM cards by PCI address** (`nvmlDeviceGetPciInfo`) rather than trusting index order.
  Degrade to "unavailable" when the library is absent; a driverless system must not error.
  **Do not shell out to `nvidia-smi` per sample** — that is what GPU-T does (§2.2), and a subprocess per poll
  is the mistake `lshw` already taught us.
- **Intel** — coverable, but the most work of the three. There is **no `gpu_busy_percent` equivalent**; three
  candidate routes, in preference order:
  1. **i915/xe PMU via `perf_event_open`** `[KNOWN]` — what `intel_gpu_top` itself uses, and it needs only the
     in-kernel driver, no userspace runtime. Read the PMU type from
     `/sys/bus/event_source/devices/i915/type` (newer `xe` driver exposes `xe_0000_00_02.0`-style names), then
     `perf_event_open` the per-engine `*-busy` events, which count **busy nanoseconds**; percentage is the
     delta over elapsed wall-clock — the same shape as Windows Running Time and Intel's NPU counter. Needs one
     syscall P/Invoke plus a read loop. Our service runs as root, so `perf_event_paranoid` is not a blocker
     (it would be for an unprivileged client — another reason this belongs in the service).
  2. **Level Zero Sysman `zesEngineGetActivity`** `[UNVERIFIED]` — the cleanest API, per-engine activity, but
     requires Intel's `libze_loader.so` compute runtime to be installed, which is not guaranteed on a plain
     desktop install. **No .NET binding exists** (`LevelZero.NET` binds compute kernels, not Sysman), so it
     would be hand-written P/Invoke either way. Reasonable fallback when the PMU route is unavailable.
  3. **DRM fdinfo** — driver-agnostic but requires walking `/proc/*/fdinfo` every sample: the `lshw` cost
     profile. Reject unless measured cheap.

  Note GPU-T does **not** solve this — its Intel probe returns `GpuLoad = 0` (§2.2). There is no prior art to
  copy here; this is the one part we would be writing from the kernel docs.
- **NPU — the two vendors differ sharply**, and this is now researched rather than guessed:
  - **Intel `ivpu`: easy.** `drivers/accel/ivpu/ivpu_sysfs.c` defines `npu_busy_time_us` (`DEVICE_ATTR_RO`),
    microseconds spent executing jobs. Read it, delta it, divide by elapsed — the same shape as the Windows
    Running Time math. Precedent: [nputop](https://github.com/ZoLArk173/nputop) reads
    `/sys/class/accel/accel0/device/npu_busy_time_us` (Linux ≥ 6.11). A `File.ReadAllText` suffices.
  - **AMD `amdxdna`: harder — sysfs is useless.** `amdxdna_sysfs.c` exposes only `device_type`, `vbnv`,
    `fw_version`; upstream `amdxdna_show_fdinfo` prints allocation sizes with **no `drm-engine-*` line**. The
    real interface is `DRM_IOCTL_AMDXDNA_GET_INFO` with `DRM_AMDXDNA_QUERY_SENSORS` →
    `AMDXDNA_SENSOR_TYPE_COLUMN_UTILIZATION` (per-column, fed from `npu_metrics.npu_busy[]`). From .NET that
    means a P/Invoke `ioctl` on `/dev/accel/accelN`. AMD's out-of-tree driver does emit
    `drm-engine-npu-amdxdna`, but [xdna-driver#324](https://github.com/amd/xdna-driver/issues/324) reports
    those values reading zero/incorrect — so do not build on fdinfo.

**Consequence for phasing:** a Framework 16 with the RTX module running Linux needs *two different readers* to
show both of its GPUs. AMD-only (Phase 2) will show the iGPU and silently omit the dGPU, so Phase 3 is not
optional for that configuration — it is the difference between a complete and a misleading picture.

### 3.4 Every source is OPTIONAL — no new hard dependency, on any platform

**Requirement:** nothing here may become a hard install dependency. A user without the NVIDIA driver, without
Intel's compute runtime, or on an older kernel must get a working app that simply does not list that device.
This is the deliberate opposite of the `lshw` decision in 0.1.1 — `lshw` is a tiny, universally packaged tool
we genuinely need for inventory; a proprietary GPU driver stack is neither.

**How the primary routes were chosen with this in mind** — the good news is that most need nothing installed:

| Source | External runtime needed? |
|---|---|
| Windows PDH (`GPU Engine`) | **None** — in-box |
| Windows naming (WmiLight) | **None** — already shipped with `Hardware.Info.Aot` |
| Linux AMD GPU (`gpu_busy_percent`) | **None** — kernel sysfs |
| Linux Intel NPU (`npu_busy_time_us`) | **None** — kernel sysfs (≥ 6.11 + `ivpu`) |
| Linux AMD NPU (`ioctl` on `/dev/accel/accelN`) | **None** — kernel (≥ 6.14 + `amdxdna`); needs root, which the service has |
| Linux Intel GPU (i915/xe PMU) | **None** — kernel PMU via `perf_event_open` |
| Linux NVIDIA (NVML) | **`libnvidia-ml.so.1`** — proprietary driver. **The only genuinely optional userspace dependency** |
| Linux Intel GPU (Level Zero fallback) | `libze_loader.so` — optional, only if we ever add that route |

**Implementation rule:** load NVML (and any other optional native) with `NativeLibrary.TryLoad` +
`SetDllImportResolver` at runtime — **never a plain `DllImport` that binds at first call**. A missing library
must be a *probe returning false*, not a `DllNotFoundException` climbing out of a telemetry tick. Same for
sysfs and ioctl paths: `File.Exists` / open-failure means "this source is unavailable", full stop.

**Packaging rule** (`packaging/linux/build-linux-packages.sh`): GPU/NPU userspace goes in the **weak**
dependency fields, never `Depends`/`Requires`:

- deb → `Suggests:` (not even `Recommends:` — we must never pull a proprietary driver into an install)
- rpm → `Suggests:`
- Arch → `optdepends=('nvidia-utils: NVIDIA GPU utilization')`

Nothing changes for the existing hard dependencies; this only governs the new optional ones.

**UI rule:** an unavailable source publishes an unavailable channel and the device is simply absent from the
list. No error banner, no "install this" nag — the same quiet degradation the fan pages use when a sensor
stops reporting.

### 3.5 Every device is listed separately — never aggregated

A machine can have several GPUs and, in time, several accelerators. The dev machine already has **three**
compute devices (Radeon 890M, RTX 5070, XDNA2 NPU). The rule:

- **One telemetry channel per physical device**, per metric. No blended "GPU usage" number — a 4% iGPU and a
  97% dGPU do not average into anything meaningful.
- **Stable identity across restarts and hotplug.** The channel key must come from the device, not its position:
  **PCI address** on Linux (`PCI_SLOT_NAME`), and on Windows the `PNPDeviceID` resolved for the adapter
  (falling back to the LUID only while §3.2's mapping is unproven — LUIDs are *not* stable across reboots, so a
  LUID-keyed channel must be treated as session-scoped).
- **Ordering is presentation, not identity.** Sort for display (integrated first, then discrete, then
  accelerators) but never let sort position feed the key.
- **Devices appear and disappear.** A dGPU that powers down, an eGPU unplugged, a driver reload — each publishes
  an unavailable channel rather than vanishing silently or freezing at its last value. This is exactly the
  behavior the fan-sensor work in 0.1.4 settled on: availability is status, not a reason to rewrite data.
- **The UI lists all of them**, each with its own name and readout, the same way the thermal page lists every
  sensor. An NPU row sits alongside the GPU rows and is labelled as an NPU, because its percentage means
  something different (§3.4).

### 3.6 What the number means

Windows `Utilization Percentage` is **busy-time per engine**. An NPU percentage is time-occupancy, **not** how
much of the NPU's compute is in use. The UI must not imply capacity. Keep per-engine detail available rather
than blending engines into one invented figure.

---

## 4. Cost and cadence

| Source | Cost | Tier |
|---|---|---|
| Windows PDH, persistent query, wildcard | **1.42 ms** [MEASURED] | fast tier (1 s) — 0.14% of a core |
| Linux `gpu_busy_percent` | one small file read | fast tier (1 s) |
| WmiLight adapter inventory | ~hundreds of ms | **slow tier / once per connection** |
| Linux fdinfo walk | scales with process × fd count | measure; likely reject |

Filtering PDH per-LUID saves ~0.4 ms and costs a wildcard match per adapter — **not worth it**. Collect once,
attribute in managed code.

Log the measured sample duration once at startup, as the hardware-info path now does, so a field regression is
visible without a profiler.

---

## 5. Integration

The telemetry pipeline already models exactly this. Follow the thermal-sensor path end to end.

1. **`TelemetryChannelId`** (`SubZeroFramework.Core/Models/`) — add `TelemetryEntityKind.Gpu` and `.Npu`, and
   `TelemetryMetric.UtilizationPercent`.
2. **New readers** in `SubZeroFramework.Core/Services/`, one interface, OS-selected the way the codebase
   already does it (`OperatingSystem.IsWindows()/IsLinux()`, as in the `RefreshVideoControllerList` guard):
   - `IGpuUtilizationReader` → `{ IReadOnlyList<GpuUtilizationSample> Sample(); }`
   - `WindowsPdhGpuUtilizationReader` — persistent PDH query, `LibraryImport` P/Invoke (AOT-friendly).
   - `LinuxSysfsGpuUtilizationReader` — enumerate `card*`, read `gpu_busy_percent`.
   - `IGpuInventoryReader` (slow tier) — WmiLight on Windows, sysfs on Linux; supplies names.
3. **`FrameworkDataProvider`** — publish through the existing `PublishNumericTelemetry` +
   `SetChannelsAvailability` (thermal loop, `:1449-1475`). Sampling goes in the FAST tier; inventory/naming in
   the SLOW tier next to the other static inventory (`StaticInventoryRefreshInterval`).
4. **Proto / mapper** — if this rides the existing telemetry-channel messages, **no proto change is needed**.
   Confirm before adding fields; reusing the channel model is strongly preferred.
5. **UI** — Device Capabilities already lists video controllers; utilization belongs there, plus optionally a
   Dashboard tile. All quantities through `IUnitFormattingService`.
6. **Out of scope**: GPU/NPU utilization as a *fan curve input* (a GPU analogue of CPU boost). Obvious
   follow-up, separate feature, own safety story. Ship monitoring first.

---

## 6. Phasing

**Phase 0 — DONE, PASSED (2026-07-25). [MEASURED]**

Ran `Design Ideas/phase0-gpu-counter-probe.ps1` elevated. Session 0 sees **exactly** what the interactive
session sees:

| | interactive (session 1) | SYSTEM (session 0) |
|---|---|---|
| counter paths | 1446 | **1446** |
| engine instances | 723 | **723** |
| adapters | `0x163A4` ×784, `0x189AE` ×500, `0x18A19` ×156, `0x19D9F` ×6 | **identical** |
| PDH collect | 1.82 ms | **1.70 ms** |

Identical adapter breakdown **including the NPU** (`0x19D9F`, 6 instances), and the cost holds under SYSTEM.
**The reader lives in the service as planned; no architectural exception needed.**

(Path count is 2× the instance count because the counter set carries two counters — `Utilization Percentage`
and `Running Time` — per instance. 723 is the real engine-instance number.)

**Phase 0 (original text) — one probe, before any shipping code.**
Confirm a **session-0 LocalSystem** service sees the same `GPU Engine` instances an interactive user does. Not
yet verified — the attempt needed elevation. Run this from an **elevated** shell:

```powershell
schtasks /create /tn SZProbe /tr "powershell -NoProfile -Command \"(Get-Counter -ListSet 'GPU Engine').PathsWithInstances.Count | Out-File C:\ProgramData\szprobe.txt\"" /sc once /st 00:00 /ru SYSTEM /rl HIGHEST /f
schtasks /run /tn SZProbe ; timeout /t 8 ; type C:\ProgramData\szprobe.txt ; schtasks /delete /tn SZProbe /f
```

Expect ~690. **A much smaller number, or zero, means the reader cannot live in the service** and the design
changes — that is why this comes first.

**Phase 1 — Windows GPU + NPU. DONE (2026-07-26).** PDH reader, per-LUID attribution, max-across-engines,
SetupAPI naming (Vanara, not WmiLight — see corrections) with the compute-only heuristic for the NPU. Covers
AMD, NVIDIA and Intel in one path. Verified on the dev machine: AMD Radeon 890M and NVIDIA RTX 5070 under
Graphics, the compute-only accelerator under Neural processor, both charting live history.

UI as shipped: each GPU's utilization lives in its **adapter detail** (big value + 30 s sparkline, the CPU
per-core card), joined to the inventory adapter by normalized display name; a measured GPU that matches no
adapter falls back to a strip at the top of the category, so a failed join never hides telemetry. The Neural
processor category mirrors the CPU category (count tile, PROCESSORS picker, per-device detail). Category rail
entries disable themselves at count 0, which is what a Linux service without X/Wayland will show for Graphics
until Phase 2 lands.

**Phase 2 — Linux AMD GPU.** `gpu_busy_percent`, per-card enumeration keyed by PCI address. Cheap, safe, covers
the integrated GPU every Framework has.

**Phase 3 — Linux NVIDIA (NVML), then Intel.** Not optional for a Framework 16 with the RTX module: after
Phase 2 that machine shows one GPU and quietly omits the other, which is worse than showing none. NVML needs a
"library absent" path so a driverless or Nouveau system degrades instead of erroring.

**Phase 4 — NPUs on Linux.** Research first. "The driver does not expose it yet" is a legitimate outcome to
document rather than force.

Each phase degrades independently: a missing source publishes an unavailable channel and the device simply
does not appear.

---

## 7. Risks & open questions

1. ~~Session-0 counter visibility~~ — **RESOLVED, passed.** See Phase 0 above.
2. ~~LUID → adapter name~~ — **RESOLVED** via `DEVPKEY_Gpu_Luid`; see §3.2.
3. **[UNVERIFIED] Whether DXCore or SetupDi is the better enumeration entry point** — both reach
   `DEVPKEY_Gpu_Luid`; pick during implementation. The NPU is confirmed reachable via the ComputeAccelerator
   device class either way.
4. **[UNVERIFIED] Linux NVIDIA/Intel/NPU paths** in their entirety.
5. **Hardware we cannot test**: Intel Framework 13, Framework Desktop, AMD dGPU module. Phases 2–4 need field
   testers, as the lshw fix did.
6. **Hotplug** — eGPUs and adapters come and go; channel availability must follow. `SetChannelsAvailability`
   already models this.
7. **Semantics** — busy-time is not capacity. Label honestly, especially for the NPU.
8. **PDH instance churn** — 690 instances on an idle machine; a heavy workload has more. Cost is linear and
   measured, but re-measure if the reader ever moves off a persistent query.
