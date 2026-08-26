---
name: debug-deep-link
description: 'Launch the SubZeroFramework app directly at any page or calibration-dialog state via a command-line route argument (DEBUG builds only). Use when reviewing a screen or dialog design, reproducing a UI bug on a specific page, testing a wizard failure state without a real hot run, or extending the deep-link grammar with new routes or dialog states.'
argument-hint: 'Name the screen or dialog state to open, or the new route/state being added.'
---
# Debug deep-link navigation

DEBUG builds accept a route as a plain command-line argument and open the app there on launch.
The whole mechanism lives in `SubZeroFramework/Services/DebugDeepLink.cs`, wrapped in `#if DEBUG`
along with its call site in `App.OnLaunched` — **it does not exist in RELEASE builds**, so nothing
here can ever navigate a real user somewhere they did not ask to go.

## Why it exists

Reaching a calibration failure screen legitimately costs a ten-minute hot run per attempt;
reaching the blocked-on-battery state costs unplugging the machine. A route argument reaches any
of them in seconds, rendered by the **production XAML** — this is how the calibration dialog's
nine states were design-reviewed one by one.

## Launching

```
SubZeroFramework.exe <route>
```

- From a terminal: run the exe in `SubZeroFramework/bin/x64/Debug/net10.0-windows10.0.26100/win-x64/`
  (WinUI 3) or `bin/x64/Debug/net10.0-desktop/` (Uno Skia) with the route as the argument.
- From Visual Studio: Project → Properties → Debug → **Application arguments**, then F5.
- The first non-`-`-prefixed argument is taken as the route; anything else is ignored.

## Route grammar

### Pages — any registered route name

Route names come from `App.RegisterRoutes` (case-sensitive, matching `RouteMap` names):

`Dashboard` · `DeviceCapabilities` · `Modules` · `FanCurveProfiles` · `PowerTelemetry` ·
`ThermalTelemetry` · `WarningIssues` · `Settings`

Nested routes use `/`: `FanCurveProfiles/Adaptive`, `DeviceCapabilities/Cpu`, `Settings/SettingsService`.

### Calibration dialog states — `dialog/calibration/<state>`

Opens the real `FanCalibrationDialog` driven into a state with plausible fake data
(fake model, fake fan "Left fan", three fake sensors — **no hardware is touched**):

| State | Shows |
|---|---|
| `consent` | The consent screen, sensors pre-ticked, AC-ready |
| `blocked` | Blocked on battery (76% charge, power tiles, no Start button) |
| `running` | Mid-run with a full synthetic trace on all four charts, step marker included |
| `success` | The result screen (K 0.42 · τ 26 s · stall 1,180 RPM · cascade) |
| `failure-load` | Insufficient load (6.4 W of 25 W, warning) |
| `failure-swing` | Insufficient ΔT (2.1 °C of 3 °C, warning) |
| `failure-ceiling` | Temperature ceiling (97 °C peak, danger) |
| `failure-cancelled` | Cancelled (neutral card, WHERE IT GOT TO rows) |
| `failure-disconnected` | Lost contact (danger, no retry button) |

Known harness quirks, both deliberate: `consent`'s Start button fires an event nothing handles
(it must not launch a real run), and `running`'s Stop completes to a fake cancelled outcome so the
dialog stays closable.

## Extending it

- **New page route:** nothing to do — any route registered in `App.RegisterRoutes` works
  immediately.
- **New dialog state:** add a `case` to `ShowCalibrationStateAsync` in `DebugDeepLink.cs`,
  constructing the state with fake data (use the existing cases as templates; drive the model with
  `BeginRun`/`Apply`/`Complete` exactly as production does).
- **A different dialog:** add a new prefix branch in `TryHandleAsync` (mirror the
  `dialog/calibration/` handling). Keep the production dialog + a fake model — do not fork the
  XAML for review purposes, the whole point is reviewing what actually ships.
- Keep everything inside the `#if DEBUG` file; never move any of it into shared code paths.

## Review workflow (how the state-by-state tour works)

```
taskkill /IM SubZeroFramework.exe /F 2>NUL
SubZeroFramework.exe dialog/calibration/<state>
```

One state per launch; kill and relaunch to switch. The harness instance never commands fans, so it
is always safe to kill.
