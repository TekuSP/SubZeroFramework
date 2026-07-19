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
