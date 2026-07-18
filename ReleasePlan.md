# Release Plan — first public release (MVP)

Consolidated 2026-07-03 from `WorkToBeDone.md`, `Design Ideas/redesign-implementation-plan.md`,
`Design Ideas/framework-port-capabilities.md`, and the pre-release gating decisions made on 2026-07-03.
This is now the single planning document for getting a viable release out; the source documents remain as
historical trackers and reference.

## MVP scope decision (what the release ships)

| Surface | State in MVP |
|---|---|
| Dashboard | ✅ Ships — **read-only live overview** (fan cards with mode indicator incl. Curve, thermal bars, power card). Cooling-profile presets render **grayed / inert** (feature-flagged off via `DashboardModel.CoolingProfilesEnabled`). |
| Thermal Telemetry | ✅ Ships (redesigned, done) |
| Power Telemetry | ✅ Ships (redesigned, done; incl. charge limits, PD ports) |
| Fan Curve Profiles | ✅ Ships — the one and only fan **control** surface (Auto/Manual/Max/Custom curve, staging + preview safety holds) |
| Device Capabilities | ✅ Ships (redesigned, done) |
| Modules | ❌ **Tab disabled** (`MainModel.IsModulesEnabled = false`) until the FFI slot-reporting gaps are closed |
| Settings | ✅ Ships (see support matrix below) |
| Warnings & Issues | ✅ Ships (redesigned recovery hero, all 9 state variants) |

## Settings support matrix (current honest state)

| Pane | Support |
|---|---|
| Service | 🟡 Reachability banner + Recheck: **works**. Restart/Shut down: **works when the service is registered**. Install/Update/Uninstall: wired but **gated on packaged-helper discovery** — in a dev checkout the packaged executable is not discoverable, so they stay disabled (see P0-1). Runtime configuration (polling intervals, allow-fan-control) Apply/Save/Reset: **works**. |
| Display units | ✅ **Fully works.** All 13 UnitsNet quantities, live samples, applies instantly app-wide, persists client-only to `%LOCALAPPDATA%\SubZeroFramework\display-unit-preferences.json`. |
| Startup & alerts | 🟡 Start with Windows (HKCU Run key): **works**. Start minimized: **works**. Autorun service: **works only when the service is registered** (disabled otherwise — correct gating). Thermal alerts: toggle + monitor **work**; the Windows toast itself currently fails to register in the unpackaged dev run (`Microsoft.WindowsAppRuntime.Insights.Resource.dll` missing) and falls back to log-only — needs release-layout validation (P0-4). |
| Licenses | ✅ **Fully works.** Build target extracts all 39 shipped packages with real license texts. |
| About | 🟡 SubZero version, live EC build + framework-dotnet version: **work**. framework-system-ffi-extensions / framework-system rows show "Bundled with framework-dotnet" until the library embeds component versions (P1-6). |

## P0 — release blockers

1. **Service install / update / uninstall / reinstall end-to-end.**
   *Researched 2026-07-03:* the architecture already matches Microsoft's documented best practice —
   `Microsoft.Extensions.Hosting.WindowsServices` (`AddWindowsService` + `ServiceName`) handles the
   **run-as-a-service side only** (SCM start/stop handshake → graceful `IHostLifetime` shutdown, Event Log
   logging default); it deliberately does NOT register/deregister services. Registration is SCM tooling,
   and our `--service-management` CLI already does exactly what the MS docs prescribe (`sc.exe create` /
   `config` / `failure …restart/5000×3` / `delete` on Windows; systemd unit + `systemctl` on Linux). So
   this item is validation + gaps, not new plumbing:
   - **Fatal-exit audit (found 2026-07-03, feeds #2):** since .NET 6 an unhandled `BackgroundService`
     exception stops the host **cleanly (exit 0)** — SCM treats that as a normal stop and our configured
     restart-on-failure never triggers. Per MS guidance, fatal worker paths must call
     `Environment.Exit(non-zero)` (after fan-restore) so recovery kicks in. The service currently has no
     such path; add it to `FrameworkShutdownCoordinator`/workers.
   - Audit `HostOptions.ShutdownTimeout` (default 30 s) vs. worst-case fan restore-to-auto on stop; the
     SCM wait is also ~30 s (`WindowsServiceLifetime` derives from `ServiceBase` — request additional time
     if restore can exceed it).
   - Make the packaged helper discoverable from the installed app (decide + document the release folder
     layout so `FrameworkServiceControlInfo.PackagedHelperAvailable` turns true outside CI artifacts).
   - Validate install → status turns healthy → update → uninstall → recovery page, on a clean machine, via
     both Settings → Service and the Warnings recovery page (UAC prompts included).
   - Validate service account requirements for EC access on Windows (LocalSystem expected OK; verify).
   - Decide shutdown ordering / preshutdown handling before update restart (carried ⏳).
2. **Service stability under real hosting.** During dev the console-hosted service died silently several
   times (signature of external termination of the dev host, not a crash). Before release: run as an
   installed Windows service for an extended soak; with the #1 fatal-exit fix, verify SCM restart +
   client reconnect actually engage on induced failure.
3. **Caller identity validation — DECIDED 2026-07-03: MVP ships with `HasCallerIdentityValidation=false`.**
   Rationale: transport is local-only Unix domain socket with expected-path validation, symlink/reparse
   protection, permission checks, and machine-scoped socket location; fan-control RPCs are fail-closed
   behind explicit service configuration; the UI already surfaces the authorization message and the
   Warnings "validation limited" state. Post-MVP hardening options: `SO_PEERCRED` on Linux; on Windows
   either named-pipe transport with client impersonation or socket-ACL ownership checks. Document this in
   `SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md` as the shipped posture.
4. **Thermal-alert toast — direction decided 2026-07-03: use `ToastNotificationManagerCompat`, not
   `AppNotificationManager`.** Research: Uno's own `Windows.UI.Notifications.ToastNotificationManager` is
   *not implemented* on Skia/desktop targets, so there is no Uno-provided path; community wrappers
   (ToastNotification.Uno) delegate to the Windows Community Toolkit. `AppNotificationManager.Register()`
   fails for **self-contained unpackaged** apps exactly as we hit (WindowsAppSDK issue #6071, missing
   `Microsoft.WindowsAppRuntime.Insights.Resource.dll`) unless the WinAppSDK runtime is installed
   machine-wide or the app gains package identity. `ToastNotificationManagerCompat`
   (CommunityToolkit.WinUI.Notifications) explicitly supports Win32 non-MSIX apps with no shortcut/COM
   registration and fits our fire-and-forget alerts. Swap `ThermalAlertMonitor`'s Windows-target toast
   path to Compat; desktop (Skia) target stays log-only for MVP (libnotify/D-Bus later). Longer-term
   alternative if richer notifications are ever needed: package identity via "packaging with external
   location" (sparse package, requires signing).
5. **Versioning + release presentation.** Set a real app version (About currently shows the assembly
   default `1.0`), align service/UI versions, update `CHANGELOG.md`, and document the update strategy
   (carried 🟡 from Packaging).
6. **Manual deployment checklist** for Windows (Linux can trail): install, autorun, update, uninstall,
   recovery, fan-safety restore on stop/shutdown (checklist exists at
   `SubZeroFramework/Docs/FanSafetyShutdownChecklist.md` — fold service lifecycle into it) (carried ⏳).
7. **Final release QA sweep** with everything gated as decided: Modules tab disabled, cooling profiles
   grayed, dashboard read-only; 0 warnings/0 errors both targets; all tests green; fresh-machine smoke run.

## Distribution & packaging (researched 2026-07-03)

**Windows (MVP) — implemented 2026-07-03: Inno Setup installer built in CI** (`packaging/windows/
subzeroframework.iss`, compiled per-arch in the Windows matrix job, artifact "SubZero Framework - Windows
{x64,arm64} Installer"). Design:
- Installs the exact layout the app's helper discovery expects: UI at `{app}` (Program Files), packaged
  service at `{app}\service-package\windows\SubZeroFramework.Service.exe` — so
  `PackagedHelperAvailable=true` and the in-app lifecycle keeps working post-install.
- Service registration goes through the service's **own `--service-management` CLI** (install-or-update →
  `enable-autorun` → `restart`), i.e. the same tested code path as the in-app buttons; the SCM `binPath`
  points at the stable Program Files copy (a bare ZIP deploy would leave the service pointing at wherever
  the user extracted — the installer exists for correctness, not just convenience). Uninstall runs
  `--service-management uninstall` before file removal. The "register service" step is a checked-by-default
  installer task; opting out leaves the Settings/Warnings install flow as fallback.
- MSIX was rejected for MVP: Win32 services in MSIX need restricted capabilities + mandatory signing;
  revisit only if package identity becomes necessary (it would also fix AppNotificationManager).
- Remaining: clean-machine validation (P0-1) and **code signing** — unsigned installers trip SmartScreen;
  decide cert vs. documented "More info → Run anyway" for the first releases.

**Linux packaging — implemented 2026-07-03 in CI** (`packaging/linux/build-linux-packages.sh` + the
`package_arch_binary` job): per-arch (x64 + arm64) `.deb` ×2 (UI, service w/ systemd enable-on-install),
`.rpm` ×2, combined tarball, and the AUR format (PKGBUILD + .SRCINFO + .install) plus binary
`.pkg.tar.zst` built via makepkg (aarch64 label via CARCH override — pure binary repack, no compilation).
Packages install to `/usr/lib/subzeroframework/{ui,service}` with the unit in `/usr/lib/systemd/system/`;
the in-app install flow should detect package-managed installs and defer (tracked above). First CI run is
the live validation of both pipelines.

**Linux — yes, both .deb and an Arch package are practical**, with two self-contained publishes (UI =
`net10.0-desktop`, service = worker). Tooling researched 2026-07-03:
- **Service `.deb`/`.rpm` via `dotnet-releaser`** (chosen): its `[service] publish = true` +
  `[service.systemd]` config installs the app **as a systemd service from the package**, with a generated
  unit that already carries `Restart = always`, `RestartSec = 1`, `StartLimitBurst = 4/60s` — i.e. the
  Linux twin of our Windows `sc.exe failure` recovery config for free. Options: `arguments`, `user` +
  `create_user` (leave `user` unset ⇒ runs as **root**, which EC access requires), and raw unit overrides
  via `[service.systemd.sections.Unit]` (e.g. `After = "network.target"`). Executable lands at
  `/usr/local/bin/<app>`. It also does deb + rpm + tar for linux-x64/arm64, changelog generation, and
  GitHub release publishing — it can become the whole Linux release driver.
  ```toml
  [msbuild]
  project = "SubZeroFramework.Service/SubZeroFramework.Service.csproj"
  [service]
  publish = true
  [service.systemd.sections.Unit]
  After = "network.target"
  ```
- **UI `.deb`:** dotnet-releaser targets console apps and generates no `.desktop` entry, so package the Uno
  UI separately — `dotnet-deb` or a trivial `dpkg-deb` CI step (publish output + `.desktop` + icon).
  Use a separate releaser/packaging config per app (a shared `[service]` would otherwise apply to every
  packable project). Prototype both packages in CI before committing to the split.
- **Arch:** no dotnet tool emits `.pkg.tar.zst`. Two viable paths, in order of preference:
  1. **AUR `-bin` PKGBUILD** (~40 lines) whose `source=` is the **tar.gz that dotnet-releaser already
     publishes to the GitHub release** — install under `/usr/lib/subzeroframework/`, `/usr/bin` symlink,
     unit into `/usr/lib/systemd/system/`, `.desktop`, `options=(staticlibs)`. Optionally run `makepkg` in
     a CI Arch container to attach a binary `.pkg.tar.zst` to releases.
  2. **`debtap` (deb → pacman) as the "at worst" fallback** — it works, and because our publishes are
     self-contained its main weakness (Debian→Arch dependency-name translation is heuristic, not 100%)
     barely applies. But even debtap's author recommends a real PKGBUILD, and converted packages are not
     acceptable for AUR distribution — so debtap is a user-side escape hatch to document, not our release
     pipeline.
- Do **not** `PublishTrimmed` the Uno UI (reflection/XAML breakage risk); trimming the worker is optional.
- Prerequisite either way: P1 Linux systemd E2E validation (root EC access) must pass first.

## Service-side fan curve drive + CPU usage modifier (landed 2026-07-18)

- The curve worker (`FrameworkFanCurveControlWorker`) drives stored curves server-side against live
  driving-sensor temperatures (interpolation identical to the client preview). **Fixed 2026-07-18:** the
  EC duty register only accepts whole percents and `FrameworkEcConnection.SetFanDuty` throws on fractional
  values, so every worker write of an interpolated (fractional) duty silently failed at Warning level —
  curves were reported active but never actuated. `FrameworkDataProvider.SetFanDutyAsync` now rounds at
  the single choke point before the EC write.
- **Per-fan CPU usage modifier** (user spec: strength float, NaN = disabled): duty points added on top of
  the curve duty, `strength × (e^(4·usage) − 1)/(e^4 − 1)` (`FanUsageModifierMath` in Core), so fans ramp
  before heat reaches the sensors. Usage comes from the service's existing 1 s Hardware.Info poll, smoothed
  fast-attack / slow-decay (5 s half-life, `FanUsageSmoothingFilter`). Set via `SetFanUsageModifier` RPC
  (NaN on the wire = disable), persisted in `service-settings.json`, survives mode switches, streamed back
  as `cpu_usage_modifier_strength` (proto `optional`, absent = disabled). Followers inherit the leader's
  already-boosted duty. Client surface: `IFrameworkFanControlClient.SetUsageModifierAsync`. Live-verified
  2026-07-18: idle 69% → 100% within ~3 s of pinning all cores → exponential decay back over ~15 s.
- **GPU usage modifier: deliberately not implemented** (user decision 2026-07-18). Hardware.Info has no GPU
  utilization; adding it later needs a source (Windows WDDM "GPU Engine" perf counters / Linux amdgpu
  `gpu_busy_percent`) plus a deliberate proto/API extension. No dormant GPU fields were added.
- **No UI yet** for the modifier — pending decision on placement/behavior on the Fan Curve Profiles page.

## P1 — shortly after MVP

1. **Cooling profiles** (flip `CoolingProfilesEnabled`): preset semantics are implemented (Silent→Auto,
   Balanced→45%, Performance→65%, Turbo→Max, Custom derived); needs UX polish + decision on interaction
   with per-fan custom curves before enabling.
2. **Modules page** (re-enable tab): blocked on framework-dotnet FFI follow-ups recorded in the redesign
   plan §5 — report all 6 FW16 slots (only PD slots stream today), USB-topology→slot mapping, Microphone
   identity/privacy entry. Also confirm FW13 vs FW13 Pro routing assumption (IntelCoreUltra ⇒ Pro).
3. **FFI port capabilities** (`Design Ideas/framework-port-capabilities.md`): static USB-C port caps +
   non-PD port fixes in the Rust FFI (user rebuilds/publishes framework-dotnet).
4. **Fan Control page visual pass** to its PDF (functionally complete; redesign plan §9).
5. **Remaining integration tests** (carried ⏳): status reconnect after service restart, long-lived stream
   startup/cancellation, telemetry contract parsing/history-window validation, broader startup/config
   binding coverage. *CI note 2026-07-03:* the pipeline now gates all publish/package jobs on the unit
   suite (Windows + Linux matrix) and runs a Windows **startup smoke test** (launch published app on a
   clean runner → window created → UI thread responsive; recovery page is the expected state without
   hardware). A real UI-automation suite (FlaUI is the right tool for WinUI 3; Uno.UITest targets
   WASM/mobile and WinAppDriver is unmaintained) stays post-MVP — runner flakiness + no Framework EC on
   runners limit its value versus on-device manual QA.
6. **framework-dotnet metadata versions**: embed `AssemblyMetadata("FrameworkSystemFfiExtensionsVersion")`
   and `("FrameworkSystemVersion")` so About shows real native component versions.
7. **Diagnostics UX**: open-logs / copy-diagnostics actions; production logging guidance (Event Log /
   file) (carried ⏳).
8. **Fan safety hardening** (carried ⏳/🟡): restore-to-auto from unhandled-termination paths, operator
   policy when restore fails, multi-instance command coordination semantics, watchdog/heartbeat decision.
9. **Linux service path** (carried ⏳/🟡): systemd end-to-end validation (root/EC access), journald
   guidance, unit hardening options.
10. **Explicit Rx throttling policy** for UI subscriptions where still missing (carried 🟡). Note the
    2026-07-03 lesson: never subscribe long history windows or issue unary EC calls during startup-page VM
    construction — the Dashboard now renders live values only.

## Superseded / dropped items (do not carry forward)

- ~~Service-owned display-unit preferences (`FrameworkUserPreferencesService`)~~ — **reversed 2026-07-03**:
  units are client-only now; the backend user-preferences vertical was removed.
- ~~Relocate `user-preferences.json` via service~~ — obsolete with client-only units. Relocating
  `service-settings.json` remains possible over the existing gRPC surface but has no UI in the redesigned
  Settings; treat as low-priority backlog, single shared relocate flow if ever revived.
- Dashboard quick-toggles card, per-fan duty stepper/boost on Dashboard — removed by design decision
  2026-07-03 (control lives on Fan Curve Profiles; Dashboard is read-only).
