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
   CFM, bits/s, bytes, watt-hours). Conversion happens only at presentation time.
2. **A single quantity is formatted by the CONVERTER, in XAML — not by the view model.** The view model
   exposes the canonical number (`double?`, `ulong?`, …) and the binding names the quantity kind:

   ```xml
   Text="{x:Bind ViewModel.PowerWatts, Mode=OneWay,
          Converter={StaticResource UnitFormat}, ConverterParameter=Power}"
   ```

   Three converter instances are registered in `App.xaml.cs`: `UnitFormat` (renders "--" when the value
   is null), `UnitFormatUnknown` ("Unknown"), and `UnitFormatValue` (bare number, no suffix, for a tile
   that draws the unit smaller beside it — pair it with a view-model `…UnitSuffix` string). The
   parameter takes an optional precision suffix: `Ratio:1`. An unknown kind throws rather than silently
   rendering the wrong unit.

   Prefer a NULLABLE canonical property over a sentinel string: "no reading" is the converter's job.

   The view model still formats when a converter cannot:
   - **Composites** — one string joining two things ("20 V · 5 A", "87 % healthy", "Curve: 45 %",
     "1,600 × 1,000 · 165 Hz"). A converter formats one value and cannot compose two.
   - **Chart axis labelers and limits** — see rule 6.
   - **Anything outside a binding** — notification bodies, status-bar messages, log lines.

   Exempt entirely: standard and product NAMES that merely contain a number ("USB4 40 Gbps",
   "3.5 mm combo", "RJ45 5 GbE", "DDR5-5600"). Those are nomenclature, not readings — converting them
   makes them wrong.

   In those cases use `Format<Quantity>(...)` (value + suffix) or `Format<Quantity>Value(...)` (bare
   value) on `IUnitFormattingService`.
3. **Re-render on a unit change with a null-named `PropertyChanged`.** A converter is pull-only: it has
   no handle on the bindings that use it, so it cannot react to anything. The PAGE model subscribes once
   to `IUserUnitPreferencesClient.WatchPreferences()` and calls `RefreshUnitFormatting()` on each of its
   cards — one subscription per page, not one per card. That method recomputes the composites and axis
   labelers, then raises `OnPropertyChanged(propertyName: null)`, which the generated x:Bind code turns
   into "re-read every binding on this source" (verified in the generated `.g.cs`).

   This is NOT the revision-counter pattern this codebase removed: a counter faked a value change to
   force a re-read. Here the values genuinely have not changed — only their presentation has — and the
   null name is the framework's own signal for exactly that.

   A view model that MIRRORS another (`FanModeModelBase`, `DeviceCapabilitiesCpuSectionModel`,
   `FanQuickControlModel`) must treat an empty `PropertyName` as "everything changed" and re-mirror.
4. **Input controls convert BOTH directions.** A slider or number box editing a quantity must present
   its **Minimum, Maximum, AND Value** in the display unit — a °F user gets a Fahrenheit scale, not a
   Celsius scale with a °F label. The view model exposes `…DisplayValue` / `…DisplayMinimum` /
   `…DisplayMaximum` properties converted via `Convert<Quantity>(...)`, and the value setter converts
   back with the inverse (`ConvertTemperatureToCelsius`, etc.), guarded by a suppression flag against
   round-trip feedback loops. Reference implementation: the thermal-alert warning-temperature slider in
   `SettingsModel` (`ThermalAlertThresholdDisplayValue` et al.).
5. **A composite string is recomputed and ASSIGNED, never a live getter.** A stored property reassigned
   by `RefreshUnitFormatting` raises PropertyChanged only when the text actually changed, and it works
   for a plain `Binding` as well as `x:Bind` (see `BoostStrengthDisplay`, `ThermalAlertThresholdDisplay`,
   `PickerSubtitle`). Never bump an int to force a re-read — see rule 3.
6. **Every chart plots in DISPLAY space — series, axis limits, and labeler must all agree.** LiveCharts
   axis `MinLimit`/`MaxLimit` and the `Labeler` operate in the SAME coordinate space as the series values,
   and as of 2026-08-24 that space is display units everywhere in this app. Concretely:
   - **Series:** pre-converted via `Convert<Quantity>(...)` before the points are built, off the UI thread.
   - **Axis limits:** ALSO converted — either in the view model (`ConvertTemperature(0)` /
     `ConvertTemperature(100)`) or in XAML through the `UnitValue` converter on a canonical VM property
     (`MinLimit="{x:Bind VM.AxisMinCelsius, Converter={StaticResource UnitValue}, ConverterParameter=Temperature}"`).
     Never hardcode a canonical bound like `MaxLimit="100"`, and never write a numeric literal at all —
     `grep -rnE '(MinLimit|MaxLimit|MinStep)="[-0-9.]' --include=*.xaml` must stay empty.
   - **Labeler:** an `IUnitFormattingService.Format<Quantity>AxisTick` method, which formats an
     already-scaled value and does NOT convert. Bind it from a stored `Func<double, string>` rebuilt as a
     fresh closure in `RefreshUnitFormatting` so a unit change rebinds the axis.
   - **Steps:** `MinStep` is a WIDTH, so pick it round in the DISPLAY unit (25 °F, not a converted 10 °C).
     If a width must be converted, use `ConvertTemperatureDelta` — `ConvertTemperature` carries the scale
     offset and turns a 10 °C step into 50 °F.

   There is deliberately **no converting labeler family any more.** A parallel `Format*AxisLabel` set taking
   canonical values was deleted on 2026-08-24: every chart here is display-space, so the only way to reach
   for it was by mistake, and doing so was invisible on the default unit and silently wrong on every other.
   Add a `Format<Quantity>AxisTick` sibling for a new quantity rather than reintroducing one.

   **An EDITED chart must invert the pointer.** `ScalePixelsToData` returns display units, but the model and
   the EC store canonical, so convert back at the single point where pointer input enters —
   `FanCurveEditorView.TryScaleToCanonical` via `FanCurveChartModel.ToCanonicalTemperature` /
   `ToCanonicalDuty`. Hit-test radii then stay CANONICAL (both sides of the comparison are canonical);
   converting them too would double-apply the scale. Guarded by `FanCurveDisplayUnitRoundTripTests`.

   **Never bind `UnitFormatConverter` to `MinLimit`/`MaxLimit`.** It returns a STRING and those are
   `double?`. An axis limit is a coordinate in a plotting space, not display text — it converts in the view
   model with `Convert<Quantity>(...)`, exactly like a slider's `Minimum`/`Maximum` (rule 4, and why
   `ConvertBack` throws). The converter is for text only.

   The classic bug (fixed 2026-07-19): a converted °F series paired with a hardcoded `MinLimit="0"
   MaxLimit="100"` (Celsius) axis + a converting labeler — the data lands outside the 0–100 window and
   nothing renders. A bound of `0` is exempt only when it is unit-invariant (0 RPM = 0 rev/s, 0 % = 0
   fraction, 0 W); a non-zero canonical bound with a display-unit series is always wrong.

   **`0` is NOT unit-invariant for temperature** — 0 °C is 32 °F and 273 K. Three instances of this shipped
   and were fixed 2026-08-24, all invisible in the default units and broken in every other:
   - `FanCardView` fan-speed axis: display-space series with `FormatFanSpeedAxisLabel` as labeler, which
     converts from RPM — every tick labelled at 1/60 of its plotted value under an rev/s preference.
   - `FanCardView` temperature axis: same double-conversion, plus `MinLimit="0"` against a display series.
   - `FanDetailEditorView` dual sparkline: a fixed unitless `0–120` window shared by rev/s and temperature,
     fed a DISPLAY-space temperature — which runs to ~212 in °F and leaves the window entirely.

   A fourth class shipped and was fixed the same day — a **converting labeler on a display-space series**,
   on the compute-usage, VRAM, CPU-core, CPU-package and battery cards. All five bound a `Labeler` that
   called the canonical `Format*AxisLabel`, so every tick was scaled twice. Invisible on percent/RPM/°C.

   Audit with: `grep -rnE '(MinLimit|MaxLimit|MinStep)=' --include=*.xaml`, then for each, check the series,
   the limits and the labeler are in the same space; and `grep -rn "AxisTick\|AxisLabel" --include=*.cs` to
   confirm no labeler converts. The one exemption is a sparkline whose limits are DERIVED FROM ITS OWN DATA
   and whose labels are hidden (`LabelsPaint="{x:Null}"`, `TextSize="0"`): its shape is identical in any
   unit, so it is genuinely unit-agnostic — the Power trend sparklines and the `FanDetailEditorView` header
   sparkline are the only ones.

## Adding a new unit option to an existing quantity

All three files must change together, keyed by the same option string:

1. `SubZeroFramework.Core/Services/Units/UnitPreferenceCatalog.cs` — add the
   `UnitPreferenceOption(key, label, description)`.
2. `SubZeroFramework/Services/Units/UnitsNetUnitFormattingService.cs` — add the key to the quantity's
   `Convert…` switch (UnitsNet property), the `Get…UnitSuffix()` switch, and the
   `Get…DefaultDecimals()` switch. Missing a branch silently falls back to the canonical unit.
3. `SubZeroFramework.Core/Services/Units/UnitPreferenceDisplay.cs` — add the short option label.
   `UnitPreferenceDisplayTests` fails if any catalog option has none.

Check the UnitsNet type actually HAS the property before adding the option — `Energy`, for instance,
has `WattHours` and `Kilojoules` but no `MilliwattHours`.

## Adding a whole new quantity kind

`UnitQuantityKind` enum → catalog definition (group, default key, options) → suffix property +
`Format…`/`Format…Value`/`Convert…`(+ inverse if any input edits it) on `IUnitFormattingService` and
the UnitsNet implementation → route every display of that quantity through the service. The Display
units settings page picks up new catalog entries automatically (live samples included), but two more
files must be updated by hand or the `UnitPreferenceDisplayTests` fail:
`SubZeroFramework.Core/Services/Units/UnitPreferenceDisplay.cs` (icon, short description, and a short
label for EVERY option) and the sample line in `SettingsUnitsSectionModel`. Finally, add the kind to
`UnitFormatConverter` — an enum member with no converter case throws at render time.
