# Handoff: SubZero — Fan Control page

## Overview
A redesigned **fan control page** for the SubZero (Framework Edition) hardware app. It lets a user monitor and control **1–6 fans**, each identified by a **location** (e.g. "Left fan / Slot 0", "APU fan / Slot 2") rather than a bare index. Every fan can run in **Auto / Manual / Custom curve / Max** mode. Edits are **staged per-fan** and committed together; **nothing touches the hardware until the user applies (or live-previews) the change.**

The page follows a **master / detail** model: a persistent fan list on the left, an editor that fills the right. Switching fans never loses another fan's pending edits.

## About the design files
The file in this bundle (`SubZero Fan Control.dc.html`) is a **design reference created in HTML** — a high-fidelity, interactive prototype of the intended look and behavior. It is **not production code to copy directly.** The real app is **WinUI 3 / Windows App SDK** (XAML), so the task is to **recreate this design with native WinUI 3 controls and the app's existing brush resources**, following the codebase's established patterns. The HTML uses a WinUI-recreation design system purely for fidelity.

> To *preview* the HTML you also need the bound design system under `_ds/…` (referenced by the file). For *implementation*, use the real WinUI 3 control library — not the HTML markup.

## Fidelity
**High-fidelity.** Final colors, typography, spacing, layout, and interactions are all intended as shown. Recreate faithfully using the app's existing WinUI controls and brushes.

---

## Brand brushes (use the app's existing resources)
The design is themed entirely from the app's existing `SolidColorBrush` resources:

| Role | Brush / value |
|------|------|
| Accent / primary / selected | `BrandPrimaryBrush` `#0078D7` |
| Secondary text (lavender) | `BrandSecondaryBrush` / `TextSecondaryBrush` `#D7D8FF` |
| App background | `AppBackgroundBrush` `#1b2727` |
| Sidebar / icon rail | `SidebarBackgroundBrush` `#000000` |
| Card | `CardBackgroundBrush` `#2e2e2e` |
| Recessed / secondary card | `CardSecondaryBackgroundBrush` `#1f1f1f` |
| Card border | `CardBorderBrush` `#2b2e2d` (or hairline white ~7% alpha) |
| Elevated tile / unselected chip | ~`#383b3b`; gauge track ~`#474b4b` |
| Text primary | `TextPrimaryBrush` `#fffffa` |
| Text tertiary | ~`#8d8ea3` |
| Success | `StatusSuccessBrush` `#6ccb5f` (fg `#000`) |
| Warning | `StatusWarningBrush` `#c5994e` (fg `#000`) |
| Error / danger | `StatusErrorBrush` `#442726` bg, text `#d9706a` |
| Info / soft blue | `StatusInfoBrush` `#8AB7E8` (icons, gauge ghost) |

Accent (`#0078D7`) drives: Apply/primary buttons, active segmented options, selection wash + bar, the nominal band of gauges, and the staged-state pill.

---

## Page anatomy (top → bottom)

1. **Title bar** (40px): app snowflake glyph + "SubZero — Framework Edition" + minimize / restore / close caption buttons.
2. **Body row:** black **icon rail** (56px) — dashboard / sensors / power / **fans (active)** / expansion / settings — beside a scrollable content region.
3. **Content:**
   - **Brand banner** + **System information** card (Model, CPU, Fans detected, Driver) — chrome, ~96px tall.
   - **Master / detail row** (`flex; gap:18px`; fills remaining height, min-height ~760px).

### Master — "Fans" list (fixed 380px card)
Header ("Fans" / "by location"), a scrollable list of fan rows, then a sticky footer.

**Fan row** (per fan):
- 50px segmented **ring gauge** (RPM ÷ max), colored by **speed band**: accent `#0078D7` < 60% → warning `#c5994e` 60–85% → danger `#d9706a` > 85%; fan glyph centered.
- **Location** name (Display 600 15px) + **slot** ("Slot 0", 11px tertiary).
- **RPM** (14px 600) + **1-min avg** ("⌀ 4,710", 11px tertiary). A stalled fan shows "Stopped" / "no rotation".
- Mode chip ("Custom curve" / "Auto" / "Manual" / "Max").
- Right column: **status chip pill** — OK (green bg/fg, check icon) or **Stalled** (danger bg/fg, `fan-alert` icon) — plus a **"Changes pending"** filled-accent pill (edit icon) when the fan has staged edits.
- **Selected** row: `rgba(0,120,215,0.16)` wash, `#0078D7` border, 3px inset left accent bar.

**Master footer** (sticky bottom of the list card):
- When any fans are staged: line "N fan(s) have unsaved changes" (accent, edit icon), then three buttons on one row — **Revert all** (danger, undo icon) · **Apply all** (accent, check icon) · **Preview all** (standard, play icon). Icon→label gap 5px here.
- When clean: "All fans up to date" (success check).

### Detail — editor (flex:1 card)
**Header:**
- 100px segmented gauge (current RPM, band-colored) with RPM value centered.
- Location (Display 600 24px) + slot; status chip; **context line** ("Custom curve · driven by 2 sensors" / "Fixed 62% duty" / "Controller policy" / "Commanded to full speed").
- "1-min avg X RPM" + "peak Y RPM".
- **History chart** (right, 268×84): two **left→right opacity-faded** lines on a **shared scale** — **rev/s** (accent blue) and **Temp** (danger red) — with end dots and a small legend. Old = transparent, now = opaque. (RPM is shown as rev/s here so it shares a numeric range with °C and both lines stay legible.)

**Applies to / link fans** (own recessed `#1f1f1f` card): "Applies to" label (link icon) + a chip per fan. The edited fan is locked (pencil icon); other healthy fans toggle to link; stalled fans are disabled. A **Link all / Only this fan** toggle sits at the right; a hint line explains the current scope ("Changes apply to Left fan only — tap another fan to link them" / "N fans linked — they share this curve and apply together").

**Mode selector:** a segmented **pill** (not tiles) with a "Mode" label (tune icon): **Auto · Manual · Custom curve · Max**, each with an MDI icon; the active option floods accent.

**Mode bodies** (constant height; short modes share one same-size card beside the gauge; all titles top-aligned):
- **Auto / Manual / Max:** a 184px gauge on the left — three faint severity zones behind a band-colored value arc, plus a faint accent **ghost arc** marking the target; center shows "RPM now" + target label "→ Auto target / → N target / → Max target". To the right, a same-size card with icon + title + description.
  - **Auto:** "Auto mode active — controller drives this fan from its built-in policy."
  - **Manual:** description, a **Slider** (0–100), a big duty %, and **Quick presets** (Silent 25 / Balanced 50 / Performance 80 / Full 100; active one highlighted accent).
  - **Max:** warning-toned card, "Max mode — 100% duty… expect audible noise."
- **Custom curve:** flat, **divider-separated** sections (no nested cards):
  1. **Curve editor** (SVG ~560×230): % (y) and °C (x) axes with gridlines, an area fill, a dashed **Applied** curve + a solid **Staged** curve with draggable point handles, and a **violet dashed marker line at the live driving temperature**. Readout: "At the current driving temperature X°C, this curve targets Y% duty."
  2. **Driving temperature sensors** (divider section): title + **Aggregate** segmented control (**Maximum / Average / Median / Minimum**), then an equal-width grid of sensor chips (Temp 0–7). Selectable chips flood accent; unusable sensors are disabled with state labels (**Error**, **Not powered**, **Not calibrated**); not-present sensors are omitted. A firmware-fallback note sits below.
  3. **Driving temperature · last 30s** (divider section): a live line chart — one faint colored line per selected sensor + a bold accent **Driving** line (the aggregate) — with a legend listing each sensor's current value and the driving value.

**Action bar** (bottom of detail) — per-fan live test:
- Staged & not previewing: edit icon + "Unsaved changes — test them live here, or commit from the fan list" + **Discard** + **Preview** (play icon).
- Previewing: warning dot + "Previewing live on the fan — reverts automatically unless you apply it" + **Stop preview** (danger).
- Clean: success check + "No unsaved changes for this fan."

### Stalled-fan state (important)
When the selected fan's reading state is **Stalled** (0 RPM while being driven), the editor **locks down**: the Applies-to card, the Mode pill, the mode body, **and the action bar are all hidden** and replaced by a single full-height **red warning panel** (`StatusErrorBrush` bg, danger border):
- large `fan-alert` icon,
- heading **"Fan stalled — please inspect this fan"**,
- a sentence on likely causes (obstruction, dust, disconnected fan header; re-seat or replace),
- a plain status line "0 RPM · no rotation detected" (no leading dot),
- a **"Re-check fan"** button (refresh icon).

The header still shows the fan with its red **Stalled** chip. No mode change is possible until it spins up.

---

## Interactions & behavior (functional spec)
- **Select fan** → loads its mode/duty into the editor; staged edits on *other* fans persist.
- **Edit anything** (mode, duty, sensor selection, aggregation, link set) → that fan is marked **staged** ("Changes pending" pill; counted in the footer).
- **Apply all / Revert all** → commit / discard **all** staged fans at once. **Discard** (in the action bar) clears just the current fan's staged edits.
- **Preview / Stop preview** → temporary **live** test of the current fan on the hardware; auto-reverts unless applied.
- **Preview all** → live-preview every staged fan together.
- **Link fans** → one edit spans the linked set; they share the curve/settings and apply together. Stalled fans are excluded.
- **Driving temperature** = `aggregate(selected usable sensors, aggregation)` where aggregate ∈ {Maximum, Average, Median, Minimum}. Example: Average of 30°C and 60°C → 45°C.
- **Target duty** = the custom curve evaluated at the driving temperature; the curve marker, readout, and driving chart all update live as sensors/aggregation change.
- **Gauges/rings** color by severity band (nominal / caution / critical); the faint accent **ghost arc** marks the target.
- **Stalled fan** → controls disabled, warning panel shown (see above).
- **Motion:** Fluent point-to-point easing, fast/normal (83/167ms); no looping/decorative motion.

## State model
- `selectedFanId`
- `staged: fanId[]` — fans with uncommitted edits
- `previewFanId | null` — fan currently being live-previewed
- per-fan working values: `mode`, `manualDuty`, `selectedSensors[]`, `aggregation`, `linkedFans[]`, `curvePoints[]`
- derived: `drivingTemp`, `targetDuty`
- **Fan reading state** enum: `Ok | NotPresent | Stalled` (NotPresent hidden from the list; Stalled shown but excluded from control).
- **Temperature sensor state** enum: `Ok | NotPresent | Error | NotPowered | NotCalibrated` (only `Ok` is selectable; `NotPresent` hidden).
- **Fan location** enum (platform-specific, from `FrameworkFanName`): `ApuFan` (FW12/13/Desktop slot 0), `LeftFan`/`RightFan` (FW16 slots 0/1), `FrontFan`/`ThirdFan` (Desktop), plus `Unknown`/`Generic`. Display the friendly name; keep the slot index as secondary text.

## Design tokens
- **Radii:** cards 14px, sub-cards/controls 12px, pills 999px; gauge caps round.
- **Spacing:** 4px grid; card padding 14–18px; icon→text gap **7px** (5px inside the compact footer buttons).
- **Type:** Segoe UI Variable — Display 600 for headings/values (15 / 20 / 24 / 30px), Text 13–14px body, 11–12px captions.
- **Gauge geometry:** 270° sweep (−135° → 135°); stroke thickness 5 (row ring) / 9 (header) / 16 (big mode gauge).

## Assets / icons
- **Segoe Fluent Icons** for Windows chrome glyphs (in the real app, use the system set).
- **Material Design Icons (MDI)** for hardware glyphs in this prototype: `fan`, `fan-alert`, `snowflake`, `thermometer`, `auto-fix`, `tune-variant`/`tune-vertical`, `chart-bell-curve`, `chart-line`, `speedometer`, `sigma`, `link-variant`, `play`, `stop`, `check`, `undo-variant`, `refresh`, `circle-edit-outline`, `shield-alert-outline`. Map these to the app's Segoe Fluent / icon set.
- No raster assets; all visuals are SVG / icon-font + CSS.

## Files in this bundle
- `SubZero Fan Control.dc.html` — the high-fidelity interactive prototype (master/detail page, all four modes, staging + preview model, curve editor, sensor + driving charts, stalled-fan lockdown). Open in a browser to interact. The embedded logic class documents the exact derived calculations (gauge arcs, curve interpolation, aggregation, driving temperature).
- `screenshots/` — PNG references: full page, master + detail, custom-curve editor, and the stalled-fan state.
