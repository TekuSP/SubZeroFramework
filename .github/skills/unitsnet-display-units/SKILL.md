---
name: unitsnet-display-units
description: 'Route every user-facing quantity in SubZeroFramework through UnitsNet display-unit preferences. Use when adding any value display, slider, input, chart axis, or notification text that shows temperature, fan speed, power, or any other physical quantity — or when adding a new unit option to the Display units settings page.'
argument-hint: 'Describe the value display, input control, or unit option being added.'
---
# UnitsNet Display Units

Every user-facing quantity in SubZero respects the user's unit choice from Settings → Display units.
No surface may hardcode a unit — not value text, not chart axes, not notification bodies, and not
input controls.

## The rules

1. **Canonical units internally, display units at the edge.** State, persistence, gRPC contracts, and
   view-model logic always use the canonical unit (Celsius, RPM, watts, volts, percent, millimeters,
   CFM, bits/s, bytes). Conversion happens only at presentation time through `IUnitFormattingService`.
2. **Formatting**: use `Format<Quantity>(...)` (value + suffix) or `Format<Quantity>Value(...)` (bare
   value, when the control renders its own suffix — e.g. `BatteryChargeRingView` appends `%` itself).
3. **Input controls convert BOTH directions.** A slider or number box editing a quantity must present
   its **Minimum, Maximum, AND Value** in the display unit — a °F user gets a Fahrenheit scale, not a
   Celsius scale with a °F label. The view model exposes `…DisplayValue` / `…DisplayMinimum` /
   `…DisplayMaximum` properties converted via `Convert<Quantity>(...)`, and the value setter converts
   back with the inverse (`ConvertTemperatureToCelsius`, etc.), guarded by a suppression flag against
   round-trip feedback loops. Reference implementation: the thermal-alert warning-temperature slider in
   `SettingsModel` (`ThermalAlertThresholdDisplayValue` et al.).
4. **Unit-aware strings are recomputed, not live getters**, so a unit-preference change is picked up on
   the next refresh (see `BoostStrengthDisplay`, `ThermalAlertThresholdDisplay`).
5. **A chart plots in ONE unit space — series, axis limits, and labeler must all agree.** LiveCharts axis
   `MinLimit`/`MaxLimit` and the `Labeler` operate in the SAME coordinate space as the series values. Two
   valid, self-consistent designs:
   - **Canonical space:** series stays canonical (e.g. Celsius curve points); axis limits are canonical
     constants; the labeler CONVERTS each tick for display (`FormatTemperatureAxisLabel`, which expects a
     Celsius value). Reference: the Fan Curve editor curve chart (`MinLimit="10" MaxLimit="125"`, Celsius).
   - **Display space:** series is pre-converted via `Convert<Quantity>(...)`; axis limits are ALSO
     converted (`ConvertTemperature(0)`/`ConvertTemperature(100)` — never hardcode a canonical bound like
     `MaxLimit="100"`); and the labeler formats the ALREADY-display value WITHOUT re-converting
     (`value => $"{value:0}{suffix}"`). Reference: the Thermal comparison + per-sensor card charts
     (`ThermalHistoryYAxisMin/MaxLimit`, `TemperatureAxisMin/MaxLimit`) and the Fan editor sensor chart.

   The classic bug (fixed 2026-07-19): a converted °F series paired with a hardcoded `MinLimit="0"
   MaxLimit="100"` (Celsius) axis + a converting labeler — the data lands outside the 0–100 window and
   nothing renders. A bound of `0` is exempt only when it is unit-invariant (0 RPM = 0 rev/s, 0 % = 0
   fraction, 0 W); a non-zero canonical bound with a display-unit series is always wrong.

## Adding a new unit option to an existing quantity

Both files must change together, keyed by the same option string:

1. `SubZeroFramework.Core/Services/Units/UnitPreferenceCatalog.cs` — add the
   `UnitPreferenceOption(key, label, description)`.
2. `SubZeroFramework/Services/Units/UnitsNetUnitFormattingService.cs` — add the key to the quantity's
   `Convert…` switch (UnitsNet property), the `Get…UnitSuffix()` switch, and the
   `Get…DefaultDecimals()` switch. Missing a branch silently falls back to the canonical unit.

## Adding a whole new quantity kind

`UnitQuantityKind` enum → catalog definition (group, default key, options) → suffix property +
`Format…`/`Format…Value`/`Convert…`(+ inverse if any input edits it) on `IUnitFormattingService` and
the UnitsNet implementation → route every display of that quantity through the service. The Display
units settings page picks up new catalog entries automatically (live samples included).
