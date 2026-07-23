# Changelog

All notable changes to this repository should be documented in this file.

## [0.1.1] - Unreleased

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
