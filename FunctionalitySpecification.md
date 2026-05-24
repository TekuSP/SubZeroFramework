# Functionality Specification

## Purpose

This document defines the intended functionality of each main navigation item in SubZeroFramework.

It is based on the current repository guidance in `.github/copilot-instructions.md` and the visual direction captured in the `Design Ideas` boards.

## Current navigation map

The current shell navigation is intended to contain these user-facing items:

- Dashboard
- Thermal Telemetry
- Power Telemetry
- Fan Curve Profiles
- Device Capabilities
- Warnings and Issues
- Settings
- Project GitHub

## Global behavior rules

- The app should navigate to Dashboard when the service is healthy, the Framework library is available, and the device is recognized as a supported Framework device.
- The app should navigate to Warnings and Issues when the service is unavailable, elevation or driver requirements are not met, or the device is unsupported.
- Healthy-state telemetry and inventory pages should be disabled when the system is not in a safe working state.
- User-selectable display units should apply consistently across cards, copyable inventory values, legends, and chart axes. These preferences are local to the client and must not change service-owned or gRPC contract units.
- The UI should preserve the current dark, flattened, Fluent-inspired visual style used by the dashboard and recent Device Capabilities work.
- Pages should prefer summary cards, clear section headings, and compact visual hierarchy instead of dense tables.
- GridView and ListView card layouts should preserve stable item identity and avoid full rebinding that causes visual blinking.
- Gauge rings should remain visually static on hover.
- Useful text values should be selectable and copyable.
- Service lifecycle guidance should use standard reachable WinUI or Uno surfaces such as `InfoBar` for privilege prompts, readiness messaging, and action results.

## Data and architecture rules

- Framework runtime and control data should come from the service boundary, not directly from the UI.
- Inventory pages should prefer FrameworkDotnet data first and use Hardware.Info only to fill gaps.
- Hardware.Info fallback data should still flow through the service, gRPC contract, and client abstraction rather than direct UI reads.
- New telemetry surfaces should be IPC-only and should use the existing telemetry clients rather than direct `IFrameworkDataProvider` usage.
- Fan control writes must remain gated behind the service authorization boundary.

## Dashboard

### Dashboard goal

The Dashboard is the landing page and the fastest operational overview of the system.

### Dashboard contents

- A system identity or system information card near the top.
- Active cooling cards for visible fans.
- A thermal snapshot section for important sensors.
- A power and battery summary block.
- Service and device health context when relevant.

### Dashboard emphasis

- Current fan RPM.
- Current driving temperature or active control context.
- Recent temperature trends.
- Battery state and the most important power values.
- A fast read of whether the system is healthy and actively managed.

### Dashboard boundaries

- The full thermal analysis page.
- The full power diagnostics page.
- The hardware inventory page.
- The full fan curve editor.

### Dashboard visual direction

- Dark dashboard cards with compact summaries.
- Active cooling cards near the top.
- Thermal snapshot as a large chart section.
- Power and battery as a primary lower section.
- Gauge rings should not push out or darken on hover.
- Recent fan mini-charts should preserve exact gauge maximums while allowing a small amount of history-axis headroom so peaks do not clip.

## Thermal Telemetry

### Thermal Telemetry goal

Thermal Telemetry is the dedicated deep-dive temperature page.

### Thermal Telemetry contents

- A persistent system information card near the top.
- A sensor selection surface with clear toggles for visible series.
- A detailed multi-series temperature chart.
- A clear legend and series state.
- Enough context to compare sensors over time.

### Thermal Telemetry interactions

- Turning individual sensors on and off.
- Comparing multiple sensors at once.
- Clear axis labels and temperature context.
- Relative-time oriented history presentation where that matches the rest of the app.

### Thermal Telemetry data expectations

- This page should be driven end to end by typed temperature telemetry clients such as `ITemperatureTelemetryClient`, with status context from `IFrameworkStatusClient`.
- It should consume current values and at least one DynamicData-backed history stream.
- It should not fall back to direct `IFrameworkDataProvider` usage.

### Thermal Telemetry priority

- This is the first dedicated telemetry page already wired end to end and the reference surface for future telemetry-page behavior.

## Power Telemetry

### Power Telemetry goal

Power Telemetry is the dedicated battery and power diagnostics page.

### Power Telemetry contents

- Battery identity and current state.
- Charge percentage and charge or discharge state.
- Voltage, current or rate, cycles, and related health values.
- Power history charts.
- A component or subsystem power breakdown when available.
- Battery health style summaries and supporting metadata.

### Power Telemetry emphasis

- Whether the battery is charging, discharging, or externally powered.
- The current power draw and recent history.
- Battery health and cycle context.
- Easy-to-scan current values before deeper details.

## Fan Curve Profiles

### Fan Curve Profiles goal

Fan Curve Profiles is the per-fan control and profile editing surface.

### Fan Curve Profiles contents

- Per-fan summary cards at the top.
- The currently selected or active fan editor.
- A curve chart with editable points.
- Fan mode controls such as auto, custom curve, and max.
- The current source sensor and control context.
- Profile-oriented actions such as save, restore, or switching profiles.

### Fan Curve Profiles interactions

- Direct chart point manipulation.
- Adding and removing points.
- Selecting the source sensor or sensor group for a fan.
- Choosing aggregation mode such as average, max, or min.
- Additional delta, CPU, and GPU usage inputs when that logic is enabled.

### Fan Curve Profiles safety rules

- All write operations must remain behind the service fan-control boundary.
- Authorization and safety validation must happen before commands reach the EC write path.
- The UI should make control mode and safety state obvious.

## Device Capabilities

### Device Capabilities goal

Device Capabilities is the inventory and reference page for the machine and exposed platform features.

### Device Capabilities contents

- Device identity and platform information.
- EC version and build information.
- BIOS version and BIOS release date.
- CPU package cards with CPU identity, recent average-usage and frequency history, and package-level hardware details.
- Card-level per-core CPU detail when the updated Hardware.Info source reports trustworthy core snapshots.
- Memory inventory.
- Storage inventory.
- Network inventory.
- Graphics and display inventory, including explicit monitor-to-GPU associations, grouped graphics-card sections, and current monitor mode details when available.
- Runtime status cards for sensors, fans, and batteries.

### Device Capabilities content rules

- The CPU section should always show CPU identity and frequency history, organized by CPU package card where multiple packages exist.
- Recent average CPU usage history and per-core usage cards may be shown when the service-backed Hardware.Info path reports trustworthy `PercentProcessorTime` / `CpuCoreList` data; do not promote this into a separate top-level CPU telemetry page without a fresh revalidation pass.
- Storage should stay at drive level rather than partition level.
- Storage should include total capacity, used space, free space, and progress-bar style summaries.
- Network should show detected adapters without a redundant adapter summary block unless explicitly requested.
- Graphics-card groups should show explicit Adapter labels, keep stable monitor grouping, and number monitor subcards so multi-adapter layouts stay easy to scan.
- Network link speeds that report sentinel or bogus max-value speeds should render as Unknown rather than as a misleading converted bitrate.
- Monitor and network card content should wrap vertically instead of clipping or forcing fixed-height overflow.
- Values that users may want to copy should remain selectable.
- Graphics and monitor cards should show explicit GPU ↔ monitor relationships when the fallback source reports them, group monitors under detected graphics cards, place unlinked monitors into an explicit Unknown graphics card bucket instead of guessing, and prefer the monitor-reported current resolution and refresh rate over adapter-only inference.

### Device Capabilities data rules

- Prefer Framework-provided data first.
- Use Hardware.Info only to fill missing inventory gaps.
- Keep the Hardware.Info fallback path behind service and gRPC boundaries.

### Device Capabilities visual direction

- Match the dashboard card language rather than looking like an old utility table.
- Use stable card models so cards do not blink on refresh.
- Keep the page readable and grouped by category.

## Warnings and Issues

### Warnings and Issues goal

Warnings and Issues is the degraded-state or unsupported-state page.

### Warnings and Issues explanation

- What is wrong.
- Why the app is limited or blocked.
- Which controls are disabled.
- What the user can do next.

### Warnings and Issues contents

- A clear top warning banner or top `InfoBar` status surface.
- A detected system profile summary.
- An explanation of why the system is unsupported or unhealthy.
- A list of disabled controls or unavailable features.
- Service-manager readiness and privilege-prompt guidance when lifecycle recovery is possible.
- Quick action buttons for recovery or diagnostics.
- A result `InfoBar` after lifecycle actions run.

### Warnings and Issues actions

- Restart the service.
- Install the service from a packaged service bundle when one is available.
- Update the installed service from a packaged service bundle when one is available.
- Uninstall the service when a clean reinstall is needed.
- Open or copy diagnostics when that workflow is implemented.
- Direct users to support or GitHub reporting when needed.

### Warnings and Issues safety role

- This page is the safe fallback when the system is not eligible for normal control surfaces.
- Unsupported or unknown devices should remain read-only and should not expose unsafe control flows.

## Settings

### Settings goal

Settings is the control panel for application behavior, service lifecycle actions, and user preferences.

### Settings contents

- Application preferences such as start with Windows and start minimized.
- Default profile selection.
- A Units section for local client display units covering temperature, fan speed, clock frequency, refresh rate, information size, voltage, current, charge capacity, ratio or fraction, length, airflow, network link speed, and power.
- Preference-path visibility plus Save Units, Reset Draft, and Restore Defaults flows for client-local display-unit persistence.
- Service-manager identity, install source summary, readiness guidance, and privilege-prompt messaging.
- Service shutdown, restart, autorun, install, update, uninstall, and reinstall actions when needed.
- Service-owned runtime configuration for telemetry cadence, hardware-info cadence, and fan-command authorization, kept separate from client-local display preferences.
- Feature toggles for app modules or custom fan control features.
- Advanced configuration blocks when they are safe and meaningful.
- Update and about information.

### Settings interactions

- Managing startup behavior.
- Managing profile defaults.
- Managing local display-unit preferences without changing the service's canonical units.
- Managing service lifecycle operations, including shutdown, restart, autorun, install, update, uninstall, and reinstall.
- Reviewing version and build information.
- Accessing update or project links.

## Project GitHub

### Project GitHub goal

Project GitHub is not an internal page. It should open the project repository or project home externally.

### Project GitHub behavior

- Open the repository or project URL.
- Avoid behaving like a normal content page inside the app shell.

## Notes on design references

The design boards currently suggest these broad visual intentions:

- `fan_control_color_scheme_dashboard.png`: dashboard as the summary-first landing page.
- `fan_control_color_scheme_thermal.png`: thermal page as the detailed sensor comparison surface.
- `fan_control_color_scheme_fan_curves.png`: fan-curve page as an editor and profile workflow.
- `fan_control_color_scheme_power.png`: power page as battery and power diagnostics.
- `fan_control_color_scheme_settings.png`: settings as preferences, advanced options, and about information.
- `fan_control_color_scheme_dev_cap.png`: Device Capabilities as a richer inventory and system identity surface.
- `fan_control_color_scheme_error_page_design.png` and `fan_control_unknown_framework.png`: unsupported-device and degraded-state warning surfaces.
- `fan_control_prototype_design.png`: the higher-level product vision that later evolved into the current split navigation.

## Notes on current implementation state

- Dashboard and Device Capabilities already contain the most mature UI direction.
- Settings now includes service-health, package-readiness, privilege-prompt, lifecycle-action functionality, and a working Units section backed by local client preference persistence; the broader app-preferences surface still needs more work.
- Warnings and Issues now acts as the unhealthy-state remediation surface with top-level status messaging, package readiness, privilege guidance, and quick service lifecycle actions.
- Dashboard, Thermal Telemetry, Power, fan cards, cooling hardware views, and Device Capabilities surfaces already honor client-local display-unit conversion and axis labeling.
- Thermal Telemetry now has the first end-to-end dedicated telemetry slice, while Power Telemetry and Fan Curve Profiles still need more of the intended functionality implemented.
- The old broader design idea of a single generic Telemetry or Diagnostics area has effectively been split into Thermal Telemetry, Power Telemetry, Fan Curve Profiles, Device Capabilities, and Warnings and Issues.
