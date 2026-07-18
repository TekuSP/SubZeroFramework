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
| Startup & alerts | 🟡 Reworked 2026-07-18/19: **"Start with system boot"** (renamed from "Start with Windows") — now backed by the **AutoLaunch** NuGet library (`AutoLaunchStartupRegistrationService`, MIT, zero deps on modern .NET): HKCU Run key on Windows (**verified end-to-end**, incl. seamless migration of the pre-library Run entry via matching value name), freedesktop autostart on Linux, LaunchAgent on macOS for free. **"Start minimized" removed** (no tray icon — a hidden launch had no way back; old JSON key ignored). Autorun service: wired to `enable/disable-autorun` on both platforms, enabled once the service is registered — now exercisable in dev via the Debug-only packaged-helper fallback (see P0-1 note; the Warnings recovery page's "Install service" button verified enabled with it). **Thermal alerts: DISABLED for MVP** (toggle inert, "Coming soon" — see P0-4 for the delivery findings and the D-Bus/legacy revival directions). |
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
   - ✅ **Fatal-exit audit — DONE 2026-07-18:** `FrameworkFatalExitHandler` (restore fans via
     `StopTelemetryLoops`, then `Environment.Exit(1)`; no-ops while the host is already stopping so clean
     stops never read as failures). Wired into the curve worker's two Rx stream-fault handlers (previously
     log-only — a faulted stream left actuation dead while an EC override could still be applied) and a
     host-crash catch in `Program.Main` (log critical → restore → return 1). Unit-tested incl. the
     restore-before-terminate ordering (`FrameworkFatalExitHandlerTests`). Recovery engagement end-to-end
     (SCM actually restarting on the non-zero exit) is validated by #2's induced-failure soak.
   - ✅ `HostOptions.ShutdownTimeout` — set explicitly to **90 s** (was default 30 s) matching the systemd
     unit's `TimeoutStopSec=90`; on .NET 8+ `WindowsServiceLifetime` requests the same additional stop time
     from the SCM. Fan restore itself is sub-second; the headroom covers a contended EC.
   - Make the packaged helper discoverable from the installed app (decide + document the release folder
     layout so `FrameworkServiceControlInfo.PackagedHelperAvailable` turns true outside CI artifacts).
     *2026-07-18:* a **Debug-only** fallback now also discovers the sibling `SubZeroFramework.Service`
     build output from a dev checkout, so install → autorun → update → uninstall is exercisable from an
     F5 build; Release builds still require the packaged layout / config override.
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
   either named-pipe transport with client impersonation or socket-ACL ownership checks. ✅ Documented as
   the shipped posture in `SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md` (2026-07-18) — this item
   is now fully closed.
4. ✅ **Thermal alerts — DISABLED for the MVP (user decision 2026-07-19).** The Settings toggle renders
   inert ("Coming soon"), `ThermalAlertMonitor` is neither registered nor started, and any previously
   persisted opt-in reads as off. Investigation trail (2026-07-18): WinAppSDK 2.3.1 fixed the old
   `Register()` failure (#6071) — registration verifiably succeeds (AUMID + `NotificationGUID` recreated
   on launch) — but **`Show()` is silently dropped** for the self-contained unpackaged app: no toast, no
   Action Center entry, and Windows never creates the per-app
   `HKCU\...\CurrentVersion\Notifications\Settings` entry that first delivery produces. Adding a
   `DisplayName` to the AUMID didn't help; global toasts on; no DND. `ToastNotificationManagerCompat` was
   rejected (archived package; critical NU1904 advisory via `System.Drawing.Common 4.7.0`). Revival
   directions (documented in `ThermalAlertMonitor` remarks): **Linux — `org.freedesktop.Notifications`
   `Notify` over the session D-Bus** (every desktop implements it, no toolkit dependency); Windows —
   resolve the self-contained WinAppSDK delivery or fall back to legacy `Windows.UI.Notifications` with
   our own AUMID registration (working prototype in git history, 2026-07-18).
5. ✅ **Versioning + release presentation — DONE 2026-07-18 (user decision: `0.1.0`, single shared
   version).** `<Version>0.1.0</Version>` in `Directory.Build.props` stamps UI, service, and Core alike
   (verified in the built assemblies); `ApplicationDisplayVersion` tracks it; About reads it via
   `AssemblyInformationalVersion`. CI already stamps `0.1.<run-number>` (`VERSION_PREFIX: '0.1'` in ci.yml —
   keep it in sync with the props major.minor). `CHANGELOG.md` rewritten with the full 0.1.0 MVP section.
   Update strategy: reinstall over the top (installer re-points the service via `--service-management
   update`); no in-app updater for MVP.
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
- **CPU boost UI (2026-07-18)**: card on the Fan Curve Profiles detail pane between "Applies to" and Mode,
  visible **only for Custom curve** (applied or editing — `IsCustomBodyVisible`) since the modifier has no
  effect in other modes. Toggle + 5–100 slider; strength text goes through `IUnitFormattingService.FormatRatio`
  so the Display-units ratio preference applies. Staged like fan links (`FanBoostSectionModel` mirrors
  `FanLinkSectionModel`): staged per fan, flushed on Apply AFTER the mode/curve commit (so no preview hold is
  open when `SetFanUsageModifier` runs), discarded on Revert. `ManualDutyDisplay` also routed through
  FormatRatio in passing.
- **Custom-curve activation staging (2026-07-18, user-reported)**: switching a non-curve-driven fan into
  Custom curve now stages immediately (`IsCustomActivationStaged`) — pending pill + Preview/Revert light up
  with the loaded curve, no need to move a point first. Revert of a staged activation exits the editor back
  to the applied mode.
- **Adversarial review pass (2026-07-18)** confirmed and fixed: (1) stale CPU readings — the worker now
  rejects hardware-info snapshots that are unavailable or >10 s old, so a failed/stopped poll decays the
  boost instead of freezing it; (2) the legacy `SetFanCustomCurve` commit path hand-built its persisted
  options and **replaced the fan's whole config entry, wiping stored curve profiles, fan link, and modifier
  from disk** (reachable today via Discard test) — it now persists via `BuildFanControlOptions` like every
  other command; (3) `SetFanUsageModifier` during an open preview hold would have committed the volatile
  preview and disarmed the revert watchdog — it now rejects with FailedPrecondition
  (`FanPreviewWatchdog.HasOpenHold`), matching the Stage → Preview → Apply model; (4) read-modify-write
  races in `FrameworkFanControlStateStore` (e.g. worker `RecordAppliedDuty` vs a command) — all
  lookup→mutate→publish sequences now serialize under one lock; (5) the worker rounds target duty to the
  whole percent the EC takes *before* the 1% change threshold, so idle CPU jitter cannot cause write churn;
  (6) a sustained missing usage source with modifiers configured now logs a warning instead of being
  silently inert.

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
   *Added 2026-07-18 (review finding, pre-existing):* any persisting command triggers a `reloadOnChange`
   config reload whose `ApplyConfiguredStates` overlay re-applies persisted state to **all** fans, which can
   clobber another fan's live volatile preview mid-session. Affects every persist path, not just the new
   modifier; fix direction: skip fans with an open preview hold during the overlay.
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
