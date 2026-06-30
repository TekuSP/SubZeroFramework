# Handoff: SubZero — Framework Edition (Fan Control, Telemetry & Modules)

## Overview

SubZero is a Windows desktop hardware-monitor / fan-control application for
**Framework** laptops and the Framework Desktop. This package covers the full
redesign of five feature areas, delivered as **10 HTML design references**:

1. **Fan Control** — master/detail fan tuning with a stage → preview → apply model.
2. **Thermal Telemetry** — temperature sensor comparison over time.
3. **Power Telemetry** — power-flow, battery health, charge limits, and USB‑C Power Delivery.
4. **Device Capabilities** — categorized hardware inventory (CPU, GPU, RAM, storage, network, profile).
5. **Modules** — physical module/port maps, one page per chassis family, plus a Module Library reference.

The visual language is **"SubZero"** — a dark, layered, expressive dashboard
built on the **WinUI 3 / Fluent** control set (this is a native Windows app).
Think Windows 11 chrome (title bar, NavigationView rail, Mica-style ground)
with a tonal, near-black teal‑tinted surface ladder and Windows‑blue accent.

---

## About the design files

The files in `*.dc.html` are **design references**, not production code. They are
HTML/CSS/JS prototypes that show the intended **look, layout, copy, and behavior**.
**Do not try to parse or transpile the HTML.** Your job is to **re-create these
designs natively** in the SubZero codebase's environment.

> **Target environment: .NET + XAML (WinUI 3 / Windows App SDK).**
> Every screen here maps cleanly onto stock WinUI controls — `NavigationView`,
> `Grid`/`StackPanel`, `ItemsControl`/`ListView`, `Expander`, `Border` (cards),
> `ProgressBar`/`ProgressRing`, `Slider`, `ToggleSwitch`, `RadioButtons`,
> and custom-drawn `Path`/`Canvas` for gauges, curves and sparklines. Suggested
> XAML mappings are noted per component. Because the prototypes already imitate
> WinUI, most of the visual work is choosing the right native control and binding
> it to your view-model.

**Use the screenshots as the source of truth for layout** (see `screenshots/`),
and this README for measurements, colors, copy, icon names, and—critically—
**which text is static chrome vs. which must be data-bound** to the EC / telemetry layer.

## Fidelity

**High-fidelity.** Final colors, typography, spacing, iconography, and
interaction model. Recreate pixel-faithfully using native WinUI controls and
your accent. Numbers shown in screenshots (RPMs, temps, voltages, serials) are
**representative sample data** — every one of them is a binding target, listed
per page below.

---

## Design tokens

All pages share one token set (declared per file in a `:root` block). Colors are
the actual hex values used in the prototypes.

### Surface ladder (dark, teal-tinted near-blacks — depth by tone, not shadow)
| Token | Hex | Use |
|---|---|---|
| `--sz-bg` | `#1b2727` | Window ground (behind everything) |
| `--sz-container-lowest` | `#161f1f` | Recessed wells |
| `--sz-container-low` | `#1f1f1f` | — |
| `--sz-container` | `#2e2e2e` | **Cards** (the main content blocks) |
| `--sz-container-high` | `#383b3b` | **Tiles** (stat tiles inside cards), hover |
| `--sz-container-highest` | `#474b4b` | Top tiles, scrollbar thumb, gauge knobs |
| `--sz-container-hover` | `#414646` | Row hover |
| `--sz-outline` | `rgba(255,255,255,0.07)` | Hairline between stacked surfaces |
| `--sz-outline-strong` | `rgba(255,255,255,0.12)` | Stronger divider |

> Nesting rule: a card (`--sz-container`) holds tiles (`--sz-container-high`) which
> may hold sub-tiles (`--sz-container-highest`). Each rung is one step lighter.
> In XAML: `Border` with `Background`, `BorderBrush`, `CornerRadius`, no drop shadow.

### Accent & status
| Token | Hex | Use |
|---|---|---|
| `--app-accent` | `#0078D7` | Primary accent — selection, Save/Apply, active segment, charts |
| `--app-accent-hover` | `#1f8ae6` | Hover |
| `--app-accent-press` | `#0063b1` | Press |
| `--app-on-accent` | `#ffffff` | Text/icon on accent fill |
| `--accent-text` | `#8AB7E8` | Accent-tinted text on dark (target labels) |
| `--sz-ok` | `#6ccb5f` | Healthy/OK (green) |
| `--sz-warn` | `#c5994e` | Caution (amber) |
| `--sz-danger` | `#d9706a` | Critical / stalled / destructive (red) |
| `--sz-violet` | `#A892FF` | Secondary brand accent, curve live-marker, "Driving" elements |
| `--sz-ok-bg` / `--sz-warn-bg` / `--sz-danger-bg` | rgba @ ~0.14–0.16 | Status pill/banner fills |

**Severity ramp** (the one scale for gauges, charge ring, usage bars, status):
`nominal → accent`, `ok → green`, `caution → amber`, `critical → red`.
Cancel / destructive actions are **always red**, never accent.

### Typography
- Family: **Segoe UI Variable** (`--font-text` for body, `--font-display` for headings/numerals). On Windows this is the system font; no embedding needed.
- Headings & all big numerals: **Segoe UI Variable Display, 600 (Semibold)**.
- Body / labels: **Text cut, 400**.
- Sizes seen: section titles 15–17px·600; page H1 28px·600; hero numerals 22–40px·600; body 12.5–14px; captions/labels 10–12px; tile values 18–28px·600.
- Casing: **sentence case** everywhere (headings, buttons, toggles). Title Case only for proper nouns (Framework, Windows, USB‑C, DisplayPort).
- No emoji. No exclamation marks in chrome.

### Spacing & shape
- 4px base grid. Card padding 14–22px; tile padding 12–16px; grid gaps 10–12px.
- Radii: tiles **10px**, cards **14px**, pills **999px**, large panels **18px**.
- Borders: 1px low-alpha (`--sz-outline`). No heavy shadows — depth is tonal.

---

## Icon system

Two icon sources are used. **Recreate icons natively** — don't pull the web fonts.

### 1. Material Design Icons (MDI) — primary UI glyphs
Rendered in HTML as `<span class="mdi mdi-NAME">`. In WinUI use your icon
solution of choice (e.g. an MDI font, `FontIcon`, or mapped `SymbolIcon`/Segoe
Fluent equivalents). The **exact MDI names used**, by area:

- **Title bar / nav rail:** `snowflake` (brand), `window-minimize`, `window-restore`, `window-close`, `view-dashboard-outline`, `thermometer`, `flash`/`flash-outline`, `fan` (active = Fan Control), `expansion-card-variant` (Modules), `cog`/`cog-outline` (settings), `alert-outline`.
- **System info rows:** `laptop` (Model), `cpu-64-bit`/`chip` (CPU), `fan` (Fans), `memory` (RAM), `harddisk`/`database` (Driver).
- **Fan Control:** `tune-vertical` (Mode label), `auto-fix` (Auto), `tune-variant` (Manual), `chart-bell-curve` (Custom curve), `speedometer` (Max), `chart-line` (history/charts), `sigma` (Aggregate), `thermometer` (sensor tiles), `fan-alert` (stalled), `link-variant` (Applies to), `check`/`check-circle` (OK), `refresh`/`backup-restore` (Revert), `play` (Preview), `content-save-check`/`check` (Apply), `pencil`/`circle-edit-outline` (Changes pending).
- **Thermal:** `thermometer`, `map-marker` (sensor location), `check-circle`, `chart-multiline`.
- **Power:** `power-plug` (Adapter/source), `chip`/`monitor` (System draw), `battery`/`battery-charging` (Battery), `chevron-right` (×2/×3, flow arrows), `heart-pulse`/`shield-check` (health), `battery-heart-variant` (wear), `counter` (cycles), `flask`/`atom` (chemistry), `factory` (manufacturer), `chart-line` (trends), `usb-c-port` (PD ports), `gauge`.
- **Device Capabilities:** `cctv`/`devices` (Onboard), `chip` (CPU), `memory` (Memory), `harddisk` (Storage), `expansion-card` (Graphics), `lan`/`sitemap` (Network), `information-outline` (System profile), `speedometer`, `clock-outline`, `gauge`, `chip`, `fan` (onboard fans), `wifi`/`ethernet` (network types), `check-circle`.
- **Modules:** `usb-c-port` (USB‑C slot), `video-input-hdmi` (HDMI), `usb` (USB‑A), `monitor` (DisplayPort), `sd`/`micro-sd` (SD/microSD), `harddisk` (SSD/Storage), `ethernet`/`ethernet-cable` (Ethernet), `expansion-card-variant` (expansion bay), `keyboard` (keyboard module/cover), `keyboard-variant`, `dots-grid` (LED matrix), `numeric` (numpad), `square-outline` (spacer/empty), `gesture-tap` (touchpad/touchscreen), `webcam`/`webcam-off` (webcam + disabled), `microphone`, `fingerprint`, `help-rhombus-outline` (unknown), `image-off`/`close-box-outline` (none/empty).

### 2. Iconify — vendor / brand logos only
Brand marks (which MDI lacks) come from **Iconify** (`<iconify-icon icon="…">`).
In a native app, ship these as bundled SVG/PNG assets. Names used:
- `simple-icons:amd` (red AMD wordmark), `simple-icons:nvidia` (green eye),
  `simple-icons:intel`, `logos:microsoft-icon` (4-color), `simple-icons:mediatek`,
  `simple-icons:teamviewer`.
- **Realtek** has no Iconify logo → falls back to an MDI `ethernet` glyph in
  Realtek-blue. Treat any unknown vendor the same way (type glyph in a brand-ish color).

---

## The Preview / Apply staging model (Fan Control)

This is the most important behavior to get right. Fan settings are **never live as
you edit** — they stage, optionally preview on hardware, then commit. State model:

- **Per-fan dirty flag.** Editing a fan's mode/duty/curve **stages** changes for that
  fan only (it does not touch hardware). A staged fan shows a **"Changes pending"**
  accent pill in the master list and detail header.
- **Preview** ("test on fan"): pushes the staged settings to the hardware
  *temporarily* so the user can hear/see the result. A ghost arc on the gauge shows
  **target vs. current**. Preview is reversible.
- **Apply / Revert** are global, in the **footer bar** (not per-card):
  - **Revert all** (red, `backup-restore` icon) — discard all staged changes.
  - **Apply all** (accent, `check`) — commit staged changes to the EC.
  - **Preview all** (`play`) — temporarily test all staged fans at once.
  The footer reads **"N fan has unsaved changes"** and is only emphasized when dirty.
- **Auto-revert:** a previewed-but-not-applied change reverts when the user leaves /
  after a timeout (commit is explicit).

VM shape: `Fan { Id, Location, SlotLabel, Mode, Duty, Curve, IsDirty, IsPreviewing, State }`
plus a session-level `StagedFans` set and `Apply()/Revert()/Preview()` commands.

---

## Screens

> Screenshots live in `screenshots/<area>/`. Each page is the **same app shell**:
> a 40px **title bar** (snowflake + "SubZero — Framework Edition" + min/restore/close),
> a left **NavigationView rail** (~56px, icon-only: Dashboard, Thermal, Power, Fans,
> Modules, …, Settings at bottom; active item shows the accent selection pill),
> and a scrolling content region. The title bar + rail are **static chrome**.
> Every page starts with the **brand card** (SubZero logo, static) + a **System
> information** card (all values **bound**: Model, CPU, RAM, Driver/EC build).

---

### 1. Fan Control  (`Fan Control.dc.html`)
*Screenshots: `screenshots/fan-control/01–06`*

**Purpose.** Tune 1–6 fans: pick a mode, preview, and apply. Master list on the
left, editor fills the right.

**Layout.** Content = brand + System info row on top; below, a two-column
master/detail. **Master** = fixed 380px **Fans** card (`ListView`), header
"Fans" + "by location" caption (static). **Detail** = flex card with a header
(gauge + name + mode segmented control) and a body that swaps by mode.

**Master list — each fan row** (`01-custom-curve.png`): mini speed gauge,
**Location** name + **slot** caption, current **RPM** + "⌀ avg", a **status pill**
(OK green / Stalled red), a mode chip ("Custom curve" / "Auto" / "Manual" / "Max"),
and—if dirty—a **"Changes pending"** accent pill.
- *Static:* "Fans", "by location", the chip labels, "Changes pending".
- *Bound:* `Location`, `SlotLabel`, `Rpm`, `AvgRpm`, `Mode`, `State`, `IsDirty`.

**Detail header.** Large segmented **speed gauge** (red→amber→green by speed band)
with **RPM now** in the center and a **target label** beneath (`accent-text`):
"Auto target" / "Max target" / the duty %. Then **Location** + slot, status pill,
the mode **SegmentedControl** (Auto · Manual · Custom curve · Max — each with an icon;
WinUI: `RadioButtons`/segmented), and an **RPM + Temperature history** sparkline
(last 60s; blue = rev/s, red = temp; left→right opacity fade; dots at the live end).
- *Bound:* `Rpm`, target, `Mode`, history series (`RpmHistory`, `TempHistory`).

**Body by mode** (all four panes are the same height):
- **Custom curve** (`05`, `06`): an interactive **temp→duty curve** editor — left-click
  to add a point, drag to move, right-click to remove. **Dashed grey = Applied**,
  **solid accent = Staged**, **violet vertical marker = live driving temperature**.
  Caption: "At the current driving temperature **NN°C**, this curve targets **NN% duty**."
  Below (flat, divider-separated sections, not nested cards): **Driving temperature
  sensors** — an **Aggregate** selector (`SegmentedControl`: **Maximum · Average ·
  Median · Minimum**) + a row of selectable **sensor tiles** (Temp 0…N with °C),
  a safety note, and a **Driving temperature · last 30s** multi-line chart (each
  selected sensor faint + the aggregated **Driving** line in accent, with a legend).
  - *Bound:* curve points (applied & staged), `DrivingTemp`, target duty, sensor list + selection, aggregate fn, 30s temp series.
- **Manual** (`03`): card titled "Manual" with `tune-variant` icon, a **Slider** (0–100%)
  + big **duty %** readout, ghost arc preview. Bound: `Duty`.
- **Auto** (`02`): card titled "Auto" with `auto-fix` icon, explanatory copy. Gauge target = "Auto target".
- **Max** (`04` shows the stalled case): card titled "Max" with `speedometer` icon, amber treatment, "commanded to full speed" copy. Gauge target = "Max target".

**Stalled fan lockdown** (`04-max-stalled.png`). When `State == Stalled`, the detail
body is **replaced** by a red banner: `fan-alert` icon, **"Fan stalled — please
inspect this fan"**, "{Location} is reporting no rotation while it is being
driven. Controls are locked until it spins up.", and **"0 RPM · no rotation
detected"**. All mode controls are disabled.

**Footer bar** (`06`): "N fan has unsaved changes" + **Revert all** / **Apply all**
/ **Preview all**, plus an inline "Unsaved changes — test them live here, or commit
from the fan list" with **Discard** / **Preview**. See the staging model above.

---

### 2. Thermal Telemetry  (`Thermal Telemetry.dc.html`)
*Screenshots: `screenshots/thermal/01–02`*

**Purpose.** Compare temperature sensors over time; toggle which sensors plot.

**Layout.** Brand + System info; page title **"Thermal telemetry"** + subtitle
"Compare sensor temperatures over time. Toggle a sensor to add or remove its
series." + a **History window** `SegmentedControl` (**1 min · 5 min · 15 min · 1 hour**,
5 min default). A device-meta strip (Device, Platform, Driver, EC build, Last seen,
Service = "Live telemetry" green, Last error = "None"). Then a **Sensors** grid
(4-wide, `01`/`02`): per-sensor card with a checkbox (plotted on/off), **Sensor N**
name, **location** (`map-marker`: CPU Tctl, SoC / APU, GPU edge, VRM, SSD 0,
Battery pack, Chassis, Ambient), a big **°C** value colored by severity, a mini bar,
and an **OK** status. Header "Sensors" + "N of 8 plotted" + "Select all". Finally a
**Temperature comparison** line chart with one colored line per plotted sensor and a
legend (Sensor N + live °C).
- *Static:* page title/subtitle, "History window", window options, column labels, "Select all".
- *Bound:* sensor list (name, **location**, °C, severity, OK/error, plotted flag), "N of 8 plotted", history window, all chart series, device-meta values.

---

### 3. Power Telemetry  (`Power Telemetry.dc.html`)
*Screenshots: `screenshots/power/01–04`*

**Purpose.** One screen for the whole power picture — input, system draw, battery
health, charge limits, trends, and per-port USB‑C Power Delivery.

**Layout (top→bottom):**
- **Power-flow hero** (`01`): three big stats connected by animated chevron arrows —
  **Adapter input** (`240 W`, sub "48.0 V · 5.00 A · USB‑C 4") **›››** **System draw**
  (`≈200 W`, "input − battery charge power") **›‹‹** **Battery** (`+40 W`,
  "76% · charging · full in ~29 min"). Signed battery W (charge +, discharge −).
- **Battery** (`02`): a **charge ring** — a full-circle track with **distinct color
  zones (no gradient): red 1/5 (critical) · amber 1/5 (low) · green 3/5 (healthy)**,
  the live charge filled and an **animated flowing dashed overlay** when charging;
  center = **76% charge**. Beside it: state pill (**Charging**), **Source** (AC /
  battery / AC+battery), **Voltage**, **Current** (signed, colored).
- **Health & capacity** (`02`): "N% healthy" + 8 tiles — Design / Last full /
  Remaining (Wh) / **Wear %** (amber) / Cycle count / Chemistry / Manufacturer /
  Model — plus a **Wear since new** bar ("last-full X of Y Wh design").
- **Charge limits** (`03`): copy "Keep the pack between a floor and a ceiling to
  extend its lifespan." + a **RangeSlider** (0–100%), **Minimum**/**Maximum** readouts
  (20% / 80%), **Set limits** button. (Maps to the EC `SetChargeLimits` control.)
- **Trends · last 5 min** (`03`): three sparkline tiles — Charge %, Current, Voltage.
- **USB‑C Power Delivery** (`03`,`04`): subtitle "Per-port negotiated power, data
  roles, alt-modes, and the expansion card in each slot." then a 2-col grid of
  **per-port cards** (USB‑C 1…6). Each: port icon, **USB‑C N**, a **plain-language
  pill set** (e.g. **Charging**, **Extended power**, **Charger attached**, **Charging
  this laptop**, **Host**, **Device attached**, **Providing power**, **Nothing
  connected**, **Cable power**, **Unknown / error**), the negotiated **V · A** + **W**
  pill on the charging port, the **card type** ("Card: USB‑C / USB‑A / DisplayPort /
  HDMI"), and the **alt-mode** ("DisplayPort", "Video + data", "Display detected", or
  "No alt-mode"). The active charging port is accent-outlined.
  > **Plain-language requirement:** show the friendly pills, **not** the raw USB‑PD
  > terms. Do **not** surface "Sink/Source/DFP/UFP/CC1/CC2" in the UI.
- *Bound:* essentially everything — adapter V/A/W, system draw, battery W/%/state/source/V/A, all health fields, charge-limit min/max, the three trend series, and per-port PD (role pills, contract V/A/W, alt-mode flags, card type/present).
- *Static:* section titles, the charge-limits explainer, PD subtitle.

---

### 4. Device Capabilities  (`Device Capabilities.dc.html`)
*Screenshots: `screenshots/device/01–09`*

**Purpose.** Browse the machine's hardware inventory by category.

**Layout.** Page title **"Device capabilities"**, then a **two-pane** body:
a left **category rail** (`ListView`/`NavigationView`: **Onboard devices · CPU ·
Memory · Storage · Graphics · Network · System profile**, each with an item count
badge), and a right detail pane. For categories with multiple instances, the detail
pane has its own structure: **global stats on top**, then a **horizontal
sub-menu / instance picker**, then **Package/instance detail** as a column of
**icon-led stat tiles** (every stat has an MDI icon).

Per category:
- **Onboard devices** (`01`,`02`): **Temperature sensors** grid (name + location +
  °C + OK) and a **Fans** grid — fans show **function as the title** (**CPU fan**,
  **GPU fan**) with the **position** as the location line (**Left fan**, **Right fan**)
  + RPM + status.
- **CPU** (`03`,`04`): a **Packages** sub-menu ("CPU 0 — AMD Ryzen AI 9 HX 370" with a
  large **AMD logo**); stat tiles (Current clock, Max clock, Socket, Physical cores,
  Logical processors, L1/L2/L3 cache, Virtualization); a **Per-core detail** grid
  (N logical) of small cards each with a util % + sparkline (hot core → red); plus a
  **Sockets** tile ("1 of 1 populated").
- **Memory** (`07`): per-DIMM sub-menu; capacity / type / speed / slot tiles.
- **Storage** (`08`): a **Storage usage** bar ("X used / Y free — NN% full"), a
  **Detected drives** sub-list, and per-drive tiles (Media type, Capacity, Firmware,
  Used…). Drive icons differ by type.
- **Graphics** (`05`): **Graphics adapters** sub-list with **vendor logos** — Adapter 0
  AMD Radeon 890M (**AMD** logo), Adapter 1 RTX 5070 Laptop (**NVIDIA** logo); detail
  tiles (Driver version/date, Resolution + standard badge e.g. WQXGA, Refresh rate,
  Adapter RAM, Monitors…).
- **Network** (`06`): **Detected adapters** sub-list with **large vendor logos +
  type-friendly names** — **Ethernet 10** (Realtek, "2.5 GbE"), **Wi‑Fi 7** (MediaTek,
  "160 MHz"), **Local Area Conn.** (VPN), **Bluetooth 3** (PAN); per-adapter tiles
  (Product, Connection, Link speed, MAC, IP, Gateways) and an **Internet** tile
  ("Connected", green = `ConnectedToInternet`).
  > Use friendly labels (Wi‑Fi 7 / Wi‑Fi 6 / GbE / 2.5 GbE / FE), **not** "802.11" / "802.1".
- **System profile** (`09`): single column — Model, Product/SKU, System revision, EC
  build, BIOS version, BIOS date, then **Cooling hardware & firmware** (Processor
  support, Primary CPU TIM e.g. "Liquid metal / PTM7958", Expansion bay power, Firmware
  RPM limit, Thermal stress max…).
- *Static:* page title, category names, sub-section headings, stat **labels**.
- *Bound:* item counts, instance lists, every stat **value**, vendor identity (→ logo), per-core util series, usage %, internet flag.

---

### 5. Modules
Five chassis variants + one reference library. All share the **physical-map →
selected-detail** pattern: a graphic of the machine with **clickable module/port
slots**; selecting one fills a **detail card** (icon, name, **confidence** chip,
**flag pills**, and raw Vendor/Product/Board IDs).

Common slot detail fields (bound): `Identity`, `SlotKind`, `Confidence`
(Direct→"Confirmed", DerivedStrong, DerivedWeak, Unknown), flag pills from
`FrameworkModuleFlags` (**Connected, Active, PD contract, DP alt-mode, BuiltIn,
Enabled, Fault, Ambiguous, Door closed**), PD contract (V/A/W) where present,
card type, alt-mode, and `VendorId`/`ProductId`/`BoardId` (hex).

- **Framework 16 — LED matrix layout** (`Modules - Two spacers layout.dc.html`,
  `screenshots/modules-fw16-spacers/`): top **port map** = a laptop body with an
  **Expansion bay** card above it (here **AMD Radeon GPU** + AMD logo) and **6
  numbered USB‑C expansion-card slots** around it (USB‑C / HDMI / USB‑A / DisplayPort
  / Empty). Below, the **input deck**: a sensor row (**Webcam** + disabled/crossed
  state, **Microphone** "privacy cover", **Fingerprint reader** "power button"),
  then **LED matrix | Keyboard module | Spacer**, then **Empty | Touchpad module |
  Empty** (the two flanking bays are always-empty). A second detail card serves the
  input-deck selection.
- **Framework 16 — Numpad layout** (`Modules - Numpad layout.dc.html`,
  `modules-fw16-numpad/`): same shell; input deck = **Keyboard module + Numeric
  keypad** (no spacers; the keypad occupies that whole side), with **two empty bays
  flanking the touchpad**. Cross-links to the LED-matrix layout.
- **Framework 16 — Wide touchpad layout** (`Modules - Wide touchpad layout.dc.html`,
  `modules-fw16-widetouchpad/`): **LED matrix | Keyboard | Spacer**, then a
  **full-width "Wide haptic touchpad module"** (module spans both lower slots; the
  touch area inside stays normal size).
- **Framework 12 / 13** (`Modules - Framework 12or13.dc.html`, `modules-fw12-13/`):
  **4 USB‑C slots** (2 per side), **no expansion bay**. Sensor row adds a
  **Touchscreen** tile ("multi-touch panel") beside Webcam/Mic/Fingerprint. Input
  deck is a single combined **Input cover** (keyboard + touchpad in one fixed module).
- **Framework Desktop** (`Modules - Framework Desktop.dc.html`, `modules-fw-desktop/`):
  a **tower-front** graphic (grille + "framework" wordmark) with **connector traces**
  down to **2 front expansion slots**; plus a fixed **Rear I/O & mainboard** section
  (its own left sub-list: Mainboard, HDMI 2.1, USB4 ×2, DP 2.1 ×2, USB‑A ×2, Audio…)
  with per-port detail (Standard, PHY, Features, Security).
- **Module Library** (`Modules - Library.dc.html`, `modules-library/`): a **reference
  catalog** of every `FrameworkModuleIdentity` SubZero can recognize, grouped:
  **Expansion cards (10)** · **Input deck modules — Laptop 16 (5)** · **Internal fixed
  devices (8)** · **Expansion bay variants (8)** (incl. AMD/NVIDIA GPU with logos) ·
  **Special states (2: Unknown USB‑C occupant, None)** · **Slot kinds**
  (`FrameworkModuleSlotKind` grid with counts). Selecting any module shows its
  Category / Slot kind / Interface / Bus / Power delivery / Serviceability.

- *Static:* section/grid headings, slot kind descriptions, the laptop/tower graphics.
- *Bound:* which slot holds what (identity → icon + name), confidence, flags, PD, IDs, expansion-bay occupant (→ vendor logo), per-slot presence.

---

## Data model & Framework enum mappings

These pages were built against the `framework-dotnet` enums. Map the raw enum to
the **display strings/icons** shown:

- **`FrameworkFanName`** → fan **location** label: `LeftFan`→"Left fan",
  `RightFan`→"Right fan", `ApuFan`→"APU fan", `FrontFan`→"Front fan",
  `ThirdFan`→"Third fan", `Generic`/`Unknown`→slot label only. Fans also carry a
  **function** (CPU/GPU) used as the title in Device Capabilities, with the location
  as the sub-line.
- **`FrameworkModuleIdentity`** (~29) → module icon + name (expansion cards
  DP/HDMI/Audio/USB‑A/USB‑C/Ethernet/Ethernet10G/microSD/SD/SSD; input deck keyboard/
  LED matrix/numeric/spacer/touchpad; internals keyboard/touchpad/fingerprint/
  touchscreen/webcam/microphone; bay variants Dual/Single interposer, UMA fans, SSD
  holder, PCIe accessory, AMD GPU, Nvidia GPU, fan-only; plus UnknownUsbCOccupant/None).
- **`FrameworkModuleSlotKind`** (8) → "Slot kind" value (UsbCExpansionCardSlot→
  "USB‑C expansion slot", InputDeckTopRow→"Input deck · top row",
  InputDeckTouchpad→"Input deck · touchpad", ExpansionBay→"Expansion bay",
  InternalFixed→"Internal · fixed", Detached→"Detached", None→"None").
- **`FrameworkModuleConfidence`** (4) → confidence chip (Direct→"Confirmed",
  DerivedStrong, DerivedWeak, Unknown).
- **`FrameworkModuleFlags`** (bitmask) → flag pills (BuiltIn, Active, Connected,
  Fault, Ambiguous, HasPdContract→"PD contract", DisplayAltMode→"DP alt-mode",
  DoorClosed→"Door closed", Enabled).
- **`FrameworkExpansionCardType`** (11), **`FrameworkExpansionBayBoard`** (6),
  **`FrameworkExpansionBayVendor`** (7: AmdGpu/NvidiaGpu→vendor logos, FanOnly,
  SsdHolder, PcieAccessory, …), **`FrameworkExpansionBayPcieConfiguration`** (5:
  Pcie4x1/4x2/4x4/5x4), **`FrameworkGpuDescriptorMagic`** (3) → as labeled in the
  Library and bay detail.

---

## Files in this bundle

| File | Page |
|---|---|
| `Fan Control.dc.html` | Fan Control (master/detail, staging model) |
| `Thermal Telemetry.dc.html` | Thermal Telemetry |
| `Power Telemetry.dc.html` | Power Telemetry |
| `Device Capabilities.dc.html` | Device Capabilities |
| `Modules - Two spacers layout.dc.html` | Modules — FW16, LED matrix + spacers |
| `Modules - Numpad layout.dc.html` | Modules — FW16, numpad |
| `Modules - Wide touchpad layout.dc.html` | Modules — FW16, wide haptic touchpad |
| `Modules - Framework 12or13.dc.html` | Modules — Framework 12/13 |
| `Modules - Framework Desktop.dc.html` | Modules — Framework Desktop |
| `Modules - Library.dc.html` | Module Library (reference catalog) |
| `screenshots/` | PNG captures, foldered per page (full-page vertical slices) |

> The `.dc.html` files open in any browser for reference. They depend on the WinUI 3
> design-system tokens/bundle that shipped with the prototypes; you do **not** need
> those to read the design — use the screenshots + this README to implement natively.

## Notes for implementation

- **Re-create, don't embed.** Build native WinUI controls; use the tokens above for
  brushes (`StaticResource`/`ThemeResource`), and your accent for one-knob theming.
- **Bindings:** treat every numeral, label-with-a-value, status, pill, and logo as a
  binding target to the EC/telemetry view-models. Static = chrome, headings, captions,
  explainer copy, and the device graphics.
- **Custom-drawn pieces** (speed gauges, charge ring, temp→duty curve editor,
  sparklines, the laptop/tower module maps) → WinUI `Path`/`Canvas` or Win2D. The
  curve editor and charge ring carry specific behaviors (drag points; animated charge
  flow; zone colors) described above.
- **Plain language** in the UI for USB‑C / PD and network standards; keep raw
  enum/tech values for tooltips or a "details" affordance only.
