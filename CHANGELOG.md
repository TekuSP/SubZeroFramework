# Changelog

All notable changes to this repository should be documented in this file.

## [0.1.5] - Unreleased

## [0.1.4] - 2026-07-25 (released as v0.1.4)

Sensor-availability release: what happens to a fan curve when the hardware it watches goes dark.

### Added

- **"Treat missing readings as 0°" per curve profile.** A selected driving sensor that loses power counts as
  cold instead of dropping out — for sensors that go quiet because whatever they measure is switched off, like
  a sleeping GPU. With Maximum aggregation a dark sensor then simply stops mattering; with the other modes it
  pulls the driving temperature down, so the editor warns about that combination.
- **The curve chart shows where fan control actually is.** A red ✕ rides the curve at the live driving
  temperature and the duty the service last applied. Because that duty already includes the CPU boost, the
  marker can sit above the drawn curve — the gap is the boost. It disappears whenever no driving temperature
  can be read, rather than freezing at a position the fan is no longer holding.
- **Windows installer offers a clean install.** A new wizard page (before the install location) asks whether
  to keep existing fan settings; ticking it deletes the service's saved configuration instead of upgrading
  onto it. Off by default, and available unattended as `msiexec /i … CLEANINSTALL=1`. Per-user preferences
  (display units, startup, alerts) are not touched — the installer only owns machine-wide service data.

### Fixed

- **A sleeping GPU rewrote the fan's saved profile.** When a driving sensor became unavailable the editor
  deselected it and adopted whichever sensor happened to be first (Mainboard), and the next Apply persisted
  that — the curve silently stopped following the GPU for good. Availability is runtime status, not user data:
  a selection now survives its sensors going dark, comes back when they return, and is only auto-seeded for a
  slot that has no selection at all.
- **A powered-down sensor could under-cool the machine.** The service read every selected sensor's raw value
  without checking whether it was reporting, so a dark sensor's 0 °C was folded into the aggregate — halving
  an Average against a hot CPU. Sensor state is now honored, and the client and service share one
  implementation of the reduction (`FanDrivingTemperature`), so the predicted duty matches what the fan does.
- **"Falls back to its firmware-safe curve" is now true.** When no driving sensor of a curve-driven fan could
  be read, the service simply skipped the fan — leaving the embedded controller holding the last duty the
  curve asked for, with nothing observing the heat. It now hands that fan back to firmware fan control (once
  per episode, retried if the handover fails) and resumes the curve automatically when a sensor reports again.
  The stored profile is never altered by the fallback.
- **Opening Fan Control showed the Auto panel for a fan that was not on Auto.** The mode selector correctly
  read "Custom curve" while the body below it described automatic control. The mode body is a navigation
  sub-region whose navigator does not exist yet on first entry; the sync gave up in that case and nothing ever
  retried, because a fan already running a curve never raises a mode change to retrigger it.
- **The sensor list changed length when the GPU powered down.** Its four sensors do not report the same way —
  three go "not powered" while the die sensor goes "not present" — and a not-present sensor was deleted from
  the selector outright, so eight chips became seven and a selected sensor disappeared from view. A sensor
  that has never reported is still omitted, but one already on screen now stays put and dims.
- **An unpowered sensor could not be picked as a driving sensor.** Its chip was disabled, so a GPU sensor was
  unselectable exactly when you wanted to choose it (with the GPU idle) — and a selected sensor that went dark
  could not be deselected either. Chips now stay interactive and simply dim while they have no reading.
- **Windows: the taskbar showed a generic window icon.** An unpackaged WinUI 3 window does not inherit the
  icon embedded in the executable, so the app icon appeared in Explorer but not on the taskbar or in Alt+Tab.

## [0.1.3] - 2026-07-25 (released as v0.1.3)

### Added

- **Settings → Service: "Reset fan settings to factory defaults".** Returns every fan to the controller's
  automatic mode and deletes all saved fan settings in one confirmed action — curve profiles in every slot,
  the active profile per fan, "Applies to" links, CPU boost, and manual/max overrides — including entries for
  fan indices the hardware no longer reports, which no per-fan action could reach. Display units, startup and
  alert settings are untouched.

### Fixed

- **Curve points could be dragged off the chart and stranded there.** The editor let a point be parked
  outside the plotted temperature window (dragging past the plot edge keeps producing coordinates out in the
  axis margin), where it was invisible and could never be grabbed again — the curve became uneditable. Points
  now snap into a band inside the visible window, and curves already holding stranded points are pulled back
  when they load. Pressing empty chart space also picks the new point straight up, so press-drag places a
  point in one motion.
- **A curve no longer flatlines above its last point.** The hidden anchor closing every curve at the top of
  the temperature range inherited the last point's duty, so a curve ending at 0 % (or any low duty) held that
  speed all the way to 130 °C instead of climbing. It is now pinned to full speed, matching the idle anchor at
  the cold end: every curve ramps from idle to maximum across the range, however early the user's own points
  stop. The client's drawn curve, its predicted-duty readout, and the service's actuation now share ONE
  implementation of that rule (`FanCurveDomain`), so a preview cannot promise a speed the fan does not deliver.

## [0.1.2] - 2026-07-23 (released as v0.1.2-alpha)

### Fixed

- **Linux: constant CPU spikes from `lshw`.** Static hardware inventory (RAM modules, drives,
  motherboard, BIOS, network adapters, OS identity) was refreshed on the hardware-info poll — every
  second by default — and on Linux the memory and drive lists each spawn a full `lshw` device-tree
  probe, so making `lshw` a dependency in 0.1.1 turned that into two heavy probes per second. Static
  inventory now refreshes every 10 minutes (still catching USB drives and network changes), while CPU
  usage (the fan-boost input) and memory free/used keep the fast cadence.

## [0.1.1] - 2026-07-23 (released as v0.1.1-alpha)

First post-release fixes, driven by field reports.

### Fixed

- **Framework 16: an unreporting expansion bay locked the whole app into recovery mode**
  ([#51](https://github.com/TekuSP/SubZeroFramework/issues/51)). Bay configurations whose EC answers
  "Unavailable" put that message into the service's global error state on every poll, which the app
  treats as unhealthy — while fans and thermals were reading perfectly. The bay is now presented as an
  **empty bay** (distinct from "could not read"), the app works normally, and the condition is logged
  once per connection instead of a stack trace every poll.
- **Missing hardware-probe tools no longer spam the journal.** A tool Hardware.Info shells out to but
  which is not installed (e.g. `lshw`) produced two warning+stack-trace entries per poll; it now logs a
  single actionable line naming the tool and what installing it enables.

### Changed

- **Linux packages now depend on `lshw`** (deb/rpm/Arch): the service uses it for the memory and
  storage inventory in Device Capabilities. Tarball installs should install it manually.
- Added [docs/INSTALL.md](docs/INSTALL.md) with per-platform install/uninstall commands, linked from
  the README.

## [0.1.0] - 2026-07-22 (first public MVP, released as v0.1.0-alpha)

First public release. Ships the redesigned app with fan control (Auto / Manual / Max / Custom curve with
staging + live preview safety holds), live thermal/power telemetry, device capabilities, client-local
display units, and a hardened background service with installers for Windows and Linux.

Deliberately gated for the MVP: the Modules tab is disabled (FFI slot-reporting gaps), Dashboard cooling
profile presets render grayed (not supported yet), and the Dashboard is a read-only overview — fan control
lives on Fan Curve Profiles.

### Added

- **Fan Curve Profiles** page: per-fan curve profile slots (up to 5), driving-sensor selection with
  aggregation modes, follow links ("Applies to" fan groups), Stage → Preview → Apply flow with a
  service-side preview watchdog that reverts uncommitted previews if the client disappears.
- **Service-side curve drive**: the background service actuates stored curves against live temperatures
  (identical interpolation to the client preview), restores persisted Manual/Max overrides after restart,
  and returns fans to automatic EC control on every shutdown path.
- **CPU boost (usage modifier)**: optional per-fan exponential feed-forward — up to a configured extra duty
  on top of the curve as CPU load rises, smoothed fast-attack/slow-decay so fans ramp before heat reaches
  the sensors without oscillating. Configurable from the Fan Curve Profiles page in Custom curve mode.
- **Redesigned pages**: Dashboard (live overview), Thermal Telemetry, Power Telemetry (incl. battery charge
  limits and USB-C PD ports), Device Capabilities, Settings (Service / Display units / Startup & alerts /
  Licenses / About), and Warnings & Issues (recovery hero covering all service states).
- Client-local **display-unit preferences** for 13 quantities (temperature, fan speed, ratio, power, …)
  applied instantly app-wide and persisted per-user; all UI values route through UnitsNet formatting.
- **Start with system boot**: launch-at-sign-in registration on Windows (Run key) and Linux (freedesktop
  autostart), backed by the cross-platform AutoLaunch library.
- **Settings → Licenses**: a build-time license report covering the full transitive NuGet dependency
  closure of the app, Core, GrpcContracts and Service projects, plus the native components vendored
  inside FrameworkDotnet. Each entry carries the package's own embedded license file where it ships one,
  a canonical SPDX text where the package declares an identifier we hold a template for, and
  "Unknown license terms" otherwise — nothing is ever guessed.
- **Thermal alerts**: opt-in desktop notification when a sensor crosses the critical band (85 °C), with
  per-sensor hysteresis and cooldown and a "Send test notification" button. Delivery via the
  DesktopNotificationsFixed library — native toasts on Windows, `org.freedesktop.Notifications` (D-Bus)
  on Linux.
- **Service lifecycle management**: `--service-management` CLI (install / update / uninstall / restart /
  autorun) used by both the in-app Settings/Warnings actions and the installers; SCM restart-on-failure
  configured on install.
- **Packaging & CI**: Windows MSI installer (WiX, x64/arm64) that lays down the app + packaged service
  and registers the service declaratively (auto-start, restart-on-failure, stop-before-upgrade,
  deregister-on-uninstall); Linux `.deb`/`.rpm`/tarball/AUR packages with systemd enable-on-install;
  unit tests gate all publish jobs; a Windows startup smoke test runs on the artifact.

### Changed

- Fan control commands are fail-closed behind explicit service configuration
  (`FrameworkService:AllowFanControlCommands`) over a local-only Unix domain socket with endpoint
  hardening; the shipped caller-identity posture is documented in `Docs/IpcAuthorizationAndUiCadence.md`.
- Service host hardening: fatal worker faults restore fans to automatic control and terminate with a
  non-zero exit code so SCM/systemd recovery restarts the service (a clean .NET host stop would otherwise
  never trigger recovery); shutdown timeout raised to 90 s to guarantee the fan restore completes.
- UI + service + installer share a single version stamped from one property (`Directory.Build.props`).

### Fixed

- The curve worker's interpolated duties are rounded to the whole percent the EC accepts — previously every
  fractional write failed silently and stored curves were reported active but never actuated.
- The legacy custom-curve commit path no longer wipes a fan's stored profile slots, link, and CPU boost
  from the persisted configuration (it now persists the full control state like every other command).
- Setting a CPU boost during a live preview is rejected instead of silently committing the uncommitted
  preview and disarming its safety revert.
- Stale CPU readings (failed or stopped hardware polling) decay the CPU boost instead of freezing it at the
  last value; a sustained missing usage source with modifiers configured logs a warning.
- Switching a fan into Custom curve now stages immediately (pending pill + Preview) without requiring a
  curve-point edit first; discarding a staged activation exits the editor cleanly.
- Windows toast registration works for the self-contained unpackaged app on WindowsAppSDK 2.3.1 (the 1.x
  `Register()` failure is gone).

### From the earlier unreleased log (2026-05-24, folded into 0.1.0)

- Client-local display-unit preferences and the Units section in Settings.
- UnitsNet-backed formatting/conversion across cards, inventory surfaces, cooling summaries, chart axes.
- Desktop fan acoustic-noise normalization to dB(A); sentinel network link speeds render as Unknown;
  dashboard mini-chart axis headroom; serialized unit-preference writes.
