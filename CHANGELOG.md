# Changelog

All notable changes to this repository should be documented in this file.

## [Unreleased] - 2026-05-24

### Added

- Client-local display-unit preferences for temperature, fan speed, clock frequency, refresh rate, information size, voltage, current, charge capacity, ratio or fraction, length, airflow, network bitrate, and power.
- A Units section in Settings with save, reset-draft, and restore-default flows backed by local preference persistence.
- UnitsNet-backed formatting and conversion services for UI cards, inventory surfaces, cooling hardware summaries, and chart axis labelers.

### Changed

- Dashboard, Thermal Telemetry, Power, fan cards, cooling hardware views, and Device Capabilities surfaces now render converted values and unit-aware axis labels while service contracts remain in canonical units.
- Device Capabilities graphics and network presentation now uses explicit Adapter labels, numbered monitor subcards, left-aligned wrap-friendly layouts, and auto-height network cards.
- Bindable display refresh paths now use analyzer-friendly dependent observable-property wiring instead of manual refresh-oriented notifications, keeping the solution warning-clean.

### Fixed

- Desktop fan acoustic-noise displays now normalize to dB(A) and show max acoustic values when reported.
- Sentinel network link speeds now render as Unknown instead of bogus max-value bitrates.
- Dashboard fan mini-chart axes now add headroom so peak history lines do not clip while gauge maximums remain exact.
- Local unit-preference persistence now serializes writes under `Lock`.

### Validation

- Clean full solution build completed successfully.
- Regression tests passed successfully.
