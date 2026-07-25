# GPU & NPU utilization — implementation plan

Status: **plan only, not started.** Written 2026-07-25 for 0.1.5+.

Evidence quality is marked throughout:
- **[MEASURED]** — verified on the dev machine (Framework 16, Ryzen AI 9 HX 370, Radeon 890M + XDNA2 NPU, Windows).
- **[KNOWN]** — established interface, but re-read the primary doc before coding.
- **[UNVERIFIED]** — could not be confirmed; treat as a research task, not a fact.

A planned web-research pass (vendor SDK licences, package maintenance status, NPU driver ABIs) was cut short by
usage limits. Everything it would have produced is called out as [UNVERIFIED] below and must be closed before
the affected phase starts. **No licence claim about any NuGet package appears in this document** — per project
policy a licence is Unknown until the package publishes one, so those cells are deliberately empty.

---

## 1. Recommendation

| Target | Recommendation | Why |
|---|---|---|
| **Windows GPU** | **DIY** — PDH `GPU Engine` counter set | The exact source Task Manager uses, in the OS, no driver, no dependency. **[MEASURED]** |
| **Windows NPU** | **DIY** — the *same* counter set | The NPU enumerates as its own compute-only adapter. **[MEASURED]** — see §3.1 |
| **Linux GPU (AMD)** | **DIY** — `gpu_busy_percent` sysfs | One file read, one integer, no dependency. **[KNOWN]** |
| **Linux GPU (Intel)** | **DIY** — i915/xe PMU, or DRM fdinfo | No sysfs equivalent to AMD's. Hardest of the four. **[UNVERIFIED]** |
| **Linux NPU** | **Defer** — investigate before promising | Driver ABIs are young and may expose nothing usable. **[UNVERIFIED]** |

**The headline: no NuGet package is needed for any of this.** Every viable source is either an OS API we
already have access to, or a file read. Adding a dependency here would buy us a vendor SDK wrapper we do not
want and a licence question we do not need.

That is not a dismissal of the ecosystem — it follows from what the sources actually are. The one package
that genuinely does this well on Windows (LibreHardwareMonitorLib) is discussed in §2, and the reason to
avoid it is specific and checkable.

---

## 2. Why not a package

| Candidate | What it would give us | Disqualifier |
|---|---|---|
| **LibreHardwareMonitorLib** | GPU utilization, per-vendor, mature | Loads a **signed kernel driver** for low-level hardware access. We already ship a privileged service; adding a ring-0 driver to a fan-control app is a large security and support surface, and it is the component most likely to trip AV/attestation. **[UNVERIFIED: whether the GPU-utilization path specifically requires the driver, or only the CPU/motherboard paths.]** If GPU reads are driver-free this deserves a second look. |
| **Hardware.Info** (already a dependency) | Video controller *inventory* | Enumerates adapters; **does not report utilization**. We already use it for exactly that inventory. **[KNOWN]** |
| **Vanara.PInvoke.*** | Typed P/Invoke for PDH | We need ~3 PDH functions. A binding package for three functions is not worth a dependency, and Vanara is split into many packages. Reasonable fallback if hand-written interop proves fiddly. |
| **Silk.NET** | DXGI/DXCore bindings | DXGI gives adapter enumeration and **memory**, not engine utilization. Useful only for the LUID→name mapping in §3.1, and even there a small D3DKMT/DXGI interop is cheaper than the dependency. |
| Vendor SDKs (AMD ADLX, Intel Level Zero Sysman) | Rich, authoritative per-vendor data | Per-vendor code paths, redistribution and licence questions, and a hard dependency on vendor runtime presence — for a number the OS already exposes generically. **[UNVERIFIED: licence terms.]** |

**Licence status of every package above: not established.** If any is reconsidered, its licence must be read
off nuget.org and fed through the existing build-time licence report first.

---

## 3. What we read, exactly

### 3.1 Windows — PDH `GPU Engine` **[MEASURED on the dev machine]**

Counter: `\GPU Engine(<instance>)\Utilization Percentage`

Instance names encode the process, adapter and engine:

```
pid_12345_luid_0x00000000_0x00018A19_phys_0_eng_0_engtype_3D
```

Measured on the dev machine — **1164 instances**, four adapters:

| LUID | Engine types present | Identification |
|---|---|---|
| `0x000163A4` | 3D, Copy | active (13.06% summed) |
| `0x000189AE` | 3D | idle |
| `0x00018A19` | 3D, Copy, JPEG_Decode_0, OFA_0, Security, VideoDecode, VideoEncode, VR | full GPU engine set |
| `0x00019D9F` | **Compute only** (6 instances) | **the XDNA2 NPU** |

**The NPU finding is the important one.** There is **no separate NPU counter set** — measured: the only
GPU/NPU-ish sets present are `GPU Engine`, `GPU Adapter Memory`, `GPU Local/Non Local Adapter Memory`,
`GPU Process Memory`. The NPU appears in `GPU Engine` as its own adapter exposing only `engtype_Compute`,
because under Windows it is an MCDM compute-only device. So **one code path reads both GPU and NPU** — we
just attribute per LUID. This is almost certainly how Task Manager draws its NPU graph.

Consequences to design around:

- **Instances are per-process and transient.** They appear and vanish with processes; utilization for a
  device is the **sum over its instances** (per engine type, then combined). Do not cache instance lists.
- **LUID is not a name.** Perf counters give no adapter name, and the composition of engine types is *not* a
  reliable identifier — measured: the adapter with the richest engine set was idle while a 3D/Copy adapter
  carried the load. Mapping LUID → "Radeon 890M" / "NPU" needs adapter enumeration
  (D3DKMT `D3DKMTEnumAdapters2` / `D3DKMTQueryAdapterInfo`, or DXGI `IDXGIFactory::EnumAdapters` →
  `DXGI_ADAPTER_DESC.AdapterLuid`). **[UNVERIFIED: whether DXGI enumerates the MCDM NPU adapter at all — DXCore
  may be required for compute-only devices.]** Until that is settled, an adapter with only `engtype_Compute`
  is a defensible heuristic for "NPU", but it is a heuristic and must be labelled as one in code.
- **Cost is real: 1164 instances.** A naive "read every instance every second" is the lshw mistake again.
  See §4.
- **Session 0 / LocalSystem**: the service runs as LocalSystem, which should have counter access.
  **[UNVERIFIED: whether per-process GPU engine instances from interactive-session processes are fully visible
  from session 0.]** This is the single biggest risk to the Windows plan and must be tested first — see §6
  Phase 0.
- API: `System.Diagnostics.PerformanceCounter` is Windows-only and legacy; prefer direct **PDH** interop
  (`PdhOpenQuery`, `PdhAddEnglishCounter` with a wildcard path, `PdhCollectQueryData`,
  `PdhGetFormattedCounterArray`). **Use the English-counter variant** so a localized Windows still resolves
  the counter path.

### 3.2 Linux — AMD `gpu_busy_percent` **[KNOWN]**

```
/sys/class/drm/card<N>/device/gpu_busy_percent     # 0–100 integer, instantaneous
/sys/class/drm/card<N>/device/mem_busy_percent     # memory controller busy
/sys/class/drm/card<N>/device/vendor               # 0x1002 = AMD
```

World-readable, one `read()` of a few bytes, no root needed. Enumerate `card*`, filter by `vendor`, and use
the PCI address under `device/` to distinguish the Framework 16's iGPU from the RX 7700S dGPU module.
**[UNVERIFIED: minimum kernel version, and whether both the integrated and discrete amdgpu instances expose
the file on the kernels our packages target.]**

This is the cheapest, most reliable source in the whole plan and covers the majority of Framework hardware.

### 3.3 Linux — Intel **[UNVERIFIED — the weakest part of this plan]**

No `gpu_busy_percent` equivalent. Two candidate routes:

1. **i915/xe PMU** via `perf_event_open` — what `intel_gpu_top` uses. Requires PMU interop from C#, and
   possibly `perf_event_paranoid` relaxation (our service is root, which likely suffices).
2. **DRM fdinfo** — `/proc/<pid>/fdinfo/<fd>` exposes `drm-engine-<class>` busy nanoseconds per client. Generic
   across drivers, but system-wide utilization means walking every process's fds and summing deltas — the same
   "walk /proc constantly" cost profile that burned us with lshw. Attractive because it is driver-agnostic
   (it may also cover AMD and the NPUs), which is exactly why it deserves proper measurement before adoption.

Both need real research and an Intel Framework to test on. Do not commit to a date.

### 3.4 Linux — NPU **[UNVERIFIED — investigate, do not promise]**

- **AMD XDNA**: the `amdxdna` driver is upstream (~6.14+); devices appear under `/sys/class/accel/accel*`.
  Whether it exports a busy percentage, or implements DRM fdinfo engine stats, is unconfirmed.
- **Intel NPU**: the `ivpu` driver reportedly exposes something like `npu_busy_time_us` in sysfs. Path and
  semantics unconfirmed.

If both turn out to expose only cumulative busy-time counters, that is still enough: sample, delta, divide by
elapsed. But confirm before designing UI around it.

### 3.5 What the number *means*

Windows `Utilization Percentage` is **busy-time**, per engine, and engines run concurrently — summing all
engine types can exceed 100%. Task Manager shows the **maximum across engines** as "the" GPU figure. We should
do the same, and keep per-engine detail available rather than inventing a blended number. An NPU "percent
busy" is time-based occupancy, **not** a measure of how much of the NPU's compute is used — the UI must not
imply otherwise.

---

## 4. Cost and cadence

The lshw incident is the governing precedent: a cheap-looking call at 1 s cost a core.

| Source | Per-sample cost | Tier |
|---|---|---|
| Windows PDH, wildcard over 1164 instances | Non-trivial — must be measured | **Measure first.** Start at the 1 s fast tier only if measured cheap; otherwise 2–5 s. |
| Linux `gpu_busy_percent` | ~1 file read, microseconds | Fast tier (1 s), safely |
| Linux fdinfo walk | Scales with process × fd count | Slow tier at best; likely reject |
| LUID → adapter-name enumeration | Adapter set is static | **Slow tier / once per connection.** Never per sample. |

Concrete rule for Windows: keep the PDH query **open** across samples (open once, `PdhCollectQueryData` per
tick) rather than reopening; that is where the cost usually hides. Add a startup log line with the measured
sample duration so a regression is visible in the field, as we now do for other hot paths.

---

## 5. Integration

The telemetry pipeline already models exactly this shape — a named numeric channel with availability. Follow
the CPU-usage path end to end; anchors below are from the surviving repo-integration research and this
session's work, and should be re-checked while implementing.

1. **`TelemetryChannelId`** (`SubZeroFramework.Core/Models/`) — add `TelemetryEntityKind.Gpu` and
   `TelemetryEntityKind.Npu`, and a `TelemetryMetric.UtilizationPercent`.
2. **`FrameworkDataProvider`** (`SubZeroFramework.Core/Services/FrameworkDataProvider.cs`) — publish via the
   existing `PublishNumericTelemetry` + `SetChannelsAvailability` pattern (see the thermal-sensor loop around
   `:1449-1475`). Respect the fast/slow tier split added after the lshw fix (`StaticInventoryRefreshInterval`).
3. **New OS-specific readers** behind one interface, e.g. `IGpuUtilizationReader`, with
   `WindowsPdhGpuUtilizationReader` and `LinuxSysfsGpuUtilizationReader`, selected by
   `OperatingSystem.IsWindows()/IsLinux()` — the codebase's existing idiom (see the `RefreshVideoControllerList`
   platform guard). Keep them in `SubZeroFramework.Core` so the service consumes them like any other source.
4. **Proto + mapper** — `framework_telemetry.proto` and `TelemetryGrpcMapper`: if the new data rides the
   existing telemetry-channel messages, **no proto change may be needed at all**. Confirm before adding fields;
   reusing the channel model is strongly preferred.
5. **UI** — Device Capabilities already lists video controllers; a utilization readout belongs there, and
   optionally on the Dashboard. Any new quantity must go through `IUnitFormattingService` (project rule).
6. **Fan control** — deliberately out of scope. GPU/NPU utilization as a *fan curve input* (a GPU analogue of
   CPU boost) is an obvious follow-up, but it is a separate feature with its own safety story. Ship monitoring
   first.

---

## 6. Phasing

**Phase 0 — de-risk (half a day, no shipping code).**
Two throwaway probes: (a) does a LocalSystem service see the same `GPU Engine` instances as an interactive
user; (b) what does a wildcard PDH collect actually cost per tick. Both answers change the design. Do this
before anything else.

**Phase 1 — Windows GPU + NPU.** One PDH reader covering both, per-LUID attribution, max-across-engines,
`engtype_Compute`-only heuristic for the NPU, adapter naming via DXGI/DXCore if Phase 0 shows it enumerates.
Biggest user-visible win, and it is the dev machine, so it is testable.

**Phase 2 — Linux AMD GPU.** `gpu_busy_percent`, iGPU + dGPU attribution by PCI address. Cheap, safe, covers
most Framework Linux users. Testable on the reporter's FW16.

**Phase 3 — Linux Intel GPU.** Only after Phase 2 ships and someone can test on Intel hardware. Choose PMU vs
fdinfo based on measured cost.

**Phase 4 — NPUs on Linux.** Research first, then decide. May end as "not exposed by the driver yet", which is
a legitimate outcome to document rather than force.

Each phase degrades independently: a missing source publishes an unavailable channel, and the UI simply does
not show that device. No phase blocks the fan-control feature set.

---

## 7. Risks & open questions

1. **[UNVERIFIED] Session 0 visibility of GPU counters** — if a LocalSystem service cannot see interactive
   processes' GPU engine instances, the whole Windows approach changes (the reading would have to move to the
   client, which is unprivileged and would be an architectural exception). *Phase 0 answers this.*
2. **[UNVERIFIED] LUID → adapter name**, and whether the NPU is enumerable by DXGI or needs DXCore. Until
   resolved, device labels rest on a heuristic.
3. **[UNVERIFIED] LibreHardwareMonitorLib's driver requirement for GPU reads.** If GPU utilization is
   driver-free, the build-vs-buy call for Windows deserves revisiting.
4. **[UNVERIFIED] Every licence** of every package named here.
5. **Hardware we cannot test**: Intel Framework 13, the RX 7700S dGPU module, Framework Desktop. Phases 2–4
   need field testers, as the lshw fix did.
6. **PDH counter cost at scale** — 1164 instances on an idle dev machine; a heavy workload will have more.
7. **Semantics** — busy-time is not "how much of the chip is used". Label the UI honestly; do not present an
   NPU percentage as capacity.
8. **eGPU / hotplug** — adapters appear and disappear. Channel availability must follow, which the existing
   `SetChannelsAvailability` mechanism already models.
