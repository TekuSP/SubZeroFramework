# SubZero redesign — implementation plan

Source of truth: `Design Ideas/design_handoff_redesign/` (README + `.dc.html` mockups + `screenshots/`).
**High-res PDF targets (authoritative, supersede the PNGs):** one per view under `screenshots/<area>/*.pdf` —
device ×7 (`Device Capabilities-{onboard,cpu,memory,drives,graphics,network,system}.pdf`), modules ×6
(Library, FW12/13, FW16 spacers/numpad/wide-touchpad, Desktop), `dashboard/Dashboard.pdf`,
`warning/SubZero Fan Control Design-warning.pdf`, settings ×5
(`Settings-{service,unitsnet,startup,licenses,about}.pdf`). Read the matching PDF before touching a view;
for exact hues/spacing grep the `.dc.html` source (tile spec: icons = StatusInfoBrush #8AB7E8, surface =
CardBackgroundBrush #2e2e2e + SurfaceOutlineBrush, radius 11; only VALUES carry ok/warn/danger state colors).
Build everything as green vertical slices (0/0 both targets + tests after each). Reuse existing controls + the
token sheet; don't redraw chrome.

## Icons — DONE (inventory + packages)
Two sources, per the handoff:
- **MDI (primary, 56 distinct glyphs)** → the project's existing **`Material.Icons.UNO` `MaterialIcon Kind="…"`**.
  Map `mdi-foo-bar` → `Kind="FooBar"`. **No new package.**
- **Brand logos (Iconify, brand-only)** → installed: **`IconifyBundle.SimpleIcons`** (`amd, nvidia, intel,
  mediatek, teamviewer, framework, kingstontechnology`) + **`IconifyBundle.Logos`** (`microsoft-icon`), on the
  core **`IconifyBundle`**. All 0.3.1, in the UI project + CPM. Build stays 0/0 (generator materializes only
  referenced icons).
- **Fallback rule:** Realtek / any unknown vendor has no Iconify logo → MDI `ethernet` (or type glyph) tinted in a
  brand-ish color.

## Foundation (build first — everything depends on these)
1. **Brand-logo pipeline + resolver — DONE.** Instead of an SvgImageSource control, brand SVGs are extracted at
   build time from the IconifyBundle `.icondata` packs (referenced-only, curated `@(IconifyIcon)` list in the UI
   csproj) by `Directory.Build.targets`'s `ExtractIcondataIcons` task → `Assets/Iconify/<asset>.svg` (git-ignored,
   `Link`-pinned) → Uno.Resizetizer rasterizes → `ms-appx:///Assets/Iconify/<asset>.png`. Resolver: Core
   `SubZeroFramework.Branding.IconAssetNaming` (Layer 1: id→asset/uri, build-sanitizer mirror) + `BrandLogoCatalog`
   (Layer 2: vendor→iconify id). Consume via UI `Controls/Branding/BrandLogoView` (`Vendor`/`IconifyId` DPs;
   Image-or-MDI-`Ethernet` fallback). Covered by `BrandLogoResolverTests`. **To add a brand:** one
   `<IconifyIcon Include="prefix:name"/>` + a `BrandLogoCatalog` constant/keyword.
2. **Vendor → logo mapper — DONE** (folded into `BrandLogoCatalog` above; extend its keyword map for
   `FrameworkExpansionBayVendor` / CPU/GPU/network vendor strings as those verticals land).
3. **Enum → display mappers** (from README "Data model" section): `FrameworkFanName` → location label (+ CPU/GPU
   *function* for Device Caps titles), `FrameworkModuleIdentity`(~29) → icon+name, `FrameworkModuleSlotKind`(8),
   `FrameworkModuleConfidence`(4 → Confirmed/DerivedStrong/…), `FrameworkModuleFlags` → flag pills, expansion-card
   type / bay board / vendor / PCIe config / GPU magic.
4. **App shell** — 40px title bar (snowflake + title + min/restore/close), ~56px icon-only `NavigationView` rail
   (Dashboard, Thermal, Power, Fans, **Modules** (new), Settings), brand card + System-information card reused on
   every page. Token sheet already mapped (surface ladder, accent/status, type ramp, 4/8/12/16/24 spacing).
   - **DONE:** the new **Modules** rail entry + scaffold page (`Presentation/MenuItems/Modules/`,
     `ExpansionCardVariant` glyph, `IsModulesEnabled` gating, ViewMap/RouteMap "Modules", placeholder bound to
     `PlatformFamily`). Brand + System-info cards already shared via `SubZeroHeaderControl` in `MainPage`.
   - **TODO:** the chrome restyle — 40px custom title bar (snowflake + title + window min/restore/close),
     icon-only 56px rail, rail rename ("Fans"). Deferred as its own slice (touches working window chrome + nav).

## Per-page plan (data each needs; ✅ = data already flows)
- **Fan Control** — largely built (staging model, master/detail). Redesign pass to match mockup; reuse
  `BandRingGaugeView`, `StatusBadgeView`, `FanStatusChipView`.
- **Thermal Telemetry — DONE.** Full 6-layer vertical for the sensor **location**: `FrameworkSensorName` (FFI) →
  `CurrentTelemetryValue.SensorName` → proto `TemperatureSensorNameValue sensor_name` (mirrors the enum) →
  `TelemetryGrpcMapper.MapSensorName` → client `ParseSensorName` → `TemperatureTelemetrySnapshot.SensorName`. UI:
  `FrameworkSensorNameDisplay.ToLocation` (Core, tested) maps the role → short label ("APU / SoC" etc.). New
  `ThermalSensorTileView` (top series-colour stripe + plot checkbox + "Sensor N" + `map-marker` location +
  severity °C/bar + OK status, dims when unplotted) replaces `ThermalStateCardView` (deleted). Page: segmented
  history window (1/5/15/60, default 5 min), icon device-meta strip (green "Live telemetry"), "Sensors" header
  with "N of N plotted" + Select all, the tile grid, and a comparison chart with a custom live legend
  (swatch + Sensor N + °C). **This is the reference pattern for the remaining telemetry-redesign verticals.**
- **Power Telemetry — DONE.** Power-flow hero + animated arrows, battery charge ring + health/capacity, trends
  sparklines, charge-limits EC-write (auth-gated), the 1:1 layout, and the full USB-C PD section: plain-language
  role pills, real card types, per-port **capabilities** (data lane / DP / charging pills), **non-PD slot**
  handling, the FW16 **4-mainboard-PD + GPU = 5** count fix, **position** labels + chassis-mirroring layout. See
  the "Power" phasing below for the slice list.
- **Device Capabilities — DONE** (redesigned, PDF-fidelity iteration complete + verified live 2026-07-02, incl.
  the two-picker Graphics monitors design update, `StatTilePanel` adaptive tiles, picker empty-state text, and
  the csproj Page-item pass). See Phasing §4.
- **Modules (NEW page + rail item) — NEXT UP** — **physical-map → selected-detail** pattern, **one layout VIEW
  per platform** (user instruction 2026-07-02): **FW12, FW13 and FW13 Pro each get their OWN view** (own
  chassis/keyboard art; input deck is a fixed input cover — NO input-deck slots, keyboard only; expansion cards
  per chassis), **FW16** (composable input deck: 2 top-row module slots flanking the keyboard — LED matrix /
  numpad / spacer — and a touchpad row that is EITHER touchpad + 2 touchpad spacers OR a wide touchpad; any
  combination of the two rows, driven by live `Position`+`Identity`), **FW Desktop** (tower front slots + rear
  I/O). The **Module Library PDF is NOT a user-facing view** — it's the developer reference for how each
  identity/slot-kind is presented (names, icons, chips, descriptions).
  Consumes: module inventory + per-slot PD + `Position` + expansion-bay GPU descriptor + privacy switches +
  fingerprint. Most data needs the Core→proto→client vertical (only PD is wired today). **Real chassis/module
  art now exists in `SubZeroFramework/Assets/Png/` and REPLACES the PDFs' placeholder shapes** — see the asset
  mapping in Phasing §5.
- **Dashboard** — full redesign: cooling-profile presets + per-fan quick control + thermal snapshot bars +
  power card + quick toggles. See Phasing §6. **NOT started.**
- **Settings** — 5 sub-pages (Service / Display units / Startup & alerts / Licenses / About) via the
  Device-Capabilities nested-region pattern. See Phasing §7. **NOT started.**
- **Warnings & Issues** — recovery-mode hero for the service-not-installed state. See Phasing §8. **NOT started.**

## Phasing
1. **Foundation** — resolver DONE; Modules rail entry + stub DONE; title-bar/rail chrome restyle CANCELLED by user
   (keep the existing title bar + nav rail as-is — only page content changes).
2. **Thermal** — DONE (see above). Proves the telemetry-redesign pattern for Power/Device-Caps/Modules.
3. **Power** — DONE. The page previously held ONLY the PD ports section; everything else is new here.
   - **DONE (slice 1):** wired `IBatteryTelemetryClient` into `PowerTelemetryModel`; **power-flow hero** (Adapter input ›› System draw ›› Battery, derived: adapter = active PD port V×A, battery W = V×A signed by state, system = input − battery charge) + **battery overview** (state pill, source, V, A — signed/coloured). Page title + restyled PD section kept.
   - **DONE (slice 2):** **battery charge ring** — `Controls/Power/BatteryChargeRingView` custom `Path`/`ArcSegment` 270° gauge (grey track + red 0–20% / amber 20–40% / green 40–100% zones clipped to charge, centre %), replaces the stand-in bar. (Animated dashed charging overlay still TODO — visual polish.)
   - **DONE (slice 3):** **Health & capacity** card — Design/Last full/Remaining (Wh = Ah×nominal V) · Wear% (amber) · Cycle/Chemistry/Manufacturer/Model tiles + "N% healthy" header + "Wear since new" bar. All from the battery snapshot.
   - **DONE (slice 4 — animation):** `Controls/Power/FlowArrowsView` (3 chevrons, traveling pulse) between the hero stats; the charge ring now has a flowing dashed `StrokeDashOffset` overlay, animated only while charging/discharging (reverses when discharging). NOTE: Uno needs **keyframe** opacity animations (no `AutoReverse` — not implemented). `StrokeDashOffset` animation on Uno Skia is unverified — fall back to a rotating overlay if it doesn't flow.
   - **DONE (slice 5 — trends):** `Trends · last 5 min` card — 3 LiveCharts sparkline tiles (Charge % / Current / Voltage); battery-history subscriptions wired into `PowerTelemetryModel` (3 metrics, 5-min window).
   - **DONE (slice 6 — charge limits EC write):** full vertical — `GetChargeLimits()`/`SetChargeLimitsAsync()` on the provider (framework-dotnet `Get/SetChargeLimits(Ratio)`), `GetChargeLimits`/`SetChargeLimits` RPCs on `FrameworkFanControlService` (Set **gated by `_authorizationService.EnsureCommandAccess()`**), `IFrameworkFanControlClient` methods, and a `RangeSelector` + Min/Max tiles + **Set limits** button (enabled only when fan-control authorized; reads current via Get on EC-available).
   - **DONE (slice 7 — 1:1 layout):** page rebuilt to the design — power-flow hero (animated arrows), **2-col Battery | Health**, **2-col Charge limits | Trends**, battery card = ring + filled state pill (bolt) + Source (plug) + Voltage/Current pair. Ring dashes made fine.
   - **DONE (slice 8 — PD redesign):** `PowerDeliveryPortViewModel` now emits **plain-language pills** (Charging / Charging this laptop / Charger attached / Providing power / Host / Device attached / Extended power / Cable power / Nothing connected — no raw Sink/Source/DFP/UFP) with per-pill brushes; 2-col `ItemsRepeater`/`UniformGridLayout` cards, active port accent-outlined, **W** pill, alt-mode line.
   - **DONE (slice 9 — card type + polish):** PD **"Card:" type now real** — `FrameworkExpansionCardSlotSnapshot.CardType` flows Core→proto→client→UI (the inventory was already read in `BuildPowerDeliverySnapshot`); shows DisplayPort/HDMI/USB-A/USB-C/Ethernet/SD/SSD/… or "No card in slot". Dark-teal surface palette (`SurfaceCardBrush`/`SurfaceWellBrush`); fixed-height rounded-rect pills (no ovals); ring dashes thinned/sparsened so the flow reads; soft state pill; ring centre = big number + small "%".
   - **DONE (slice 10 — FFI port capabilities):** new `FrameworkUsbCPortCapability` (data lane / DisplayPort version /
     charging watts / USB-A note) on a per-`FrameworkPlatform` table in the **Rust FFI** + managed projection; flows
     Core→proto→client→UI as **muted capability pills** ("USB4", "DP 2.1 UHBR10", "240 W"). Non-PD slots
     (documented `!SupportsCharging`) render as data-only USB, not PD ports.
   - **DONE (slice 11 — FW16 PD-port model fix):** FW16 has **4 mainboard PD ports, not 6** (`builder.rs`
     `usb_c_slot_count` 6→4, matching upstream `power.rs` `ports = 4`; the six bays mux onto four controllers — the
     900 mA front bays aren't PD ports). The **graphics module adds a 5th** PD port (bay probe at EC index 4),
     appended as a distinct port. Verified on hardware via the plug-each-slot mapping.
   - **DONE (slice 12 — positions):** `FrameworkUsbCPortPosition` enum in the FFI (upstream `fl16` mapping: Right
     Back / Right Middle-or-Front / Left Middle-or-Front / Left Back + Graphics module); managed derives
     `PositionName` + `IsLeftSide`. UI **titles ports by position** and orders the 2-col grid to mirror the chassis
     (left ports left, right ports right, Back→Middle→Front). **Rust unit tests** (`cargo test`, 8 pass) + a **FW16
     hardware test** in `FrameworkHardwareTests.cs` asserting 4 + GPU = 5. framework-dotnet crate → 0.6.2, NuGet 0.8.213.
   - **Known partial:** precise alt-mode wording ("Video + data" vs "Display detected") still approximated from `AltModeFlags`.
4. **Device Capabilities — DONE (PDF-fidelity iteration complete, verified live 2026-07-02).** All seven
   categories match their PDFs: two-pane shell, instance pickers (full-height, per the side-panel rule),
   data-navigated details, uniform-accent `StatTileView` (theme-reuse: StatusInfoBrush icons,
   CardBackgroundBrush tiles, StatusErrorTextBrush danger), brand-colored logos, CPU page (Sockets tile,
   load-colored bare core sparklines), value-state colors (Free amber/red,
   Connected/Fastest/Internet/LinkSpeed/Virtualization green). Final pass added:
   - Memory/Storage/Graphics de-chromed (plain headings + StatTileViews, usage bars severity-tinted via
     `UsageBarBrush`); Graphics **Primary display** tile with resolution `Badge`.
   - Onboard **battery tile**: amber "Discharging · AC + battery" status (BatteryMinus/BatteryCharging icon
     override), `DetailLines` rows — green health % (HeartPulse), cycles (Counter), chemistry (Flask); tone from
     `FrameworkBatteryState` (dead `GetBatteryStatus`/`Format*State` helpers removed).
   - Storage drive detail: 4-col 5-tile layout + severity-tinted per-drive usage bar; Network adapter detail:
     plain title + 4-col 6 tiles (Product/Connection/Link speed/MAC + IP/Gateways).
   - Graphics (design update 2026-07-02): second **MONITORS picker** beside GRAPHICS ADAPTERS + a monitor detail
     stacked under the adapter detail in one scroll pane — both are DataViewMap sub-regions
     (`GraphicsMonitor` route, `DeviceCapabilitiesGraphicsMonitorDetailModel/View`, two `Region.Attached`
     hosts each synced by its own navigator). The MONITORS picker is SCOPED to the picked adapter's linked
     monitors (`SyncMonitorSource` swaps `MonitorPicker.ItemsSource` to the group's `MonitorCards`, auto-selects
     the first, collapses the monitor detail when the adapter has none — keeps the list consistent with the
     adapter's "Monitors" count). Monitor tiles: green Active, type, manufacturer, resolution +
     shared `DeviceCapabilitiesResolutionBadge` chip, refresh, density, product code, serial, "2024 · week 42",
     description. Monitor snapshots with no WMI mode borrow resolution/refresh from the linked adapter
     (`EnrichMonitorWithLinkedControllerMode`). Dead `MonitorSummaryCardView`/`MonitorResolutionCards` removed.
   - **`StatTilePanel`** (Controls/DeviceCapabilities): adaptive tile layout — equal-width columns with
     `MinTileWidth` (160) and `MaxColumns` (4 default, Memory summary 6, Laptop-13 cooling 3); tiles wrap onto
     extra rows on narrow panes instead of clipping text. Replaced every fixed star-column tile Grid across the
     Device Caps sections/details (CPU/Memory/Storage/Graphics/Network/System profile/Cooling). Use it for any
     future stat-tile row instead of a Grid.
   - System profile restructured per `-system.pdf`: **Device identity** (6-col identity tiles incl. EC/BIOS),
     **Cooling hardware & firmware** (all four platform templates rebuilt on StatTileView; Laptop 16 = 5 tiles
     with amber Thermal stress max + "9 W max" expansion power) + **Fan modules** cards (215px, big blue Fan
     icon, dimensions + "N mm thick" via new `ShellFan/GraphicsFanThicknessDisplay`), **Motherboard** tiles.
   Builds 0 warnings/0 errors both targets; 131/131 tests. Original implementation notes follow. Additions past the original slices: **colored stat
   tiles** (`StatTileView`; every metric label across all sections has an accent MDI
   icon) and **instance pickers with data navigation** — each multi-instance category (CPU · Memory · Storage ·
   Graphics · Network) has a PACKAGES/INSTALLED MODULES/DETECTED DRIVES/… picker whose selection navigates an
   inner nested region via `DataViewMap` + `NavigateDataAsync` (the picked card model IS the navigation data;
   detail VMs in `Models/Categories/*DetailModel`, views in `Categories/*DetailView`). Auto-selects the first
   instance; per-instance lists removed from the section views (stats/usage bars remain). Implementation
   notes: the two-pane shell uses **ViewMap/RouteMap nested-region navigation like the fan modes** (NOT visibility
   toggles) — 7 thin category views/VMs (`Categories/`, `Models/Categories/`) bridge to the page model via
   `DeviceCapabilitiesAccessor` (FanCoordinatorAccessor pattern); the page code-behind mirrors
   `SelectedCategoryIndex` into the region with the deferred QueueSync pattern. The **fan function** ("CPU fan" /
   "GPU fan") is a full vertical: `FanNameValue fan_name` on the current-telemetry proto (mirrors `sensor_name`) →
   `CurrentTelemetryValue.FanName` → `FanTelemetrySnapshot.FanName` → `FrameworkFanNameDisplay.ToFunction`.
   Onboard tiles reuse the enriched `DeviceCapabilitiesRuntimeStatusItemModel` (icon/value/severity/location).
   Logos: `BrandLogoView` in CPU package, Graphics adapter, and Network adapter headers (keyword resolve from the
   product string; Realtek → type-glyph fallback). Network friendly types derived in the card model; **Internet
   tile** = client-side `NetworkInterface.GetIsNetworkAvailable()` on the page VM. WQXGA added to the resolution
   tiers. Gap analysis (kept for reference): the page VM
   (`DeviceCapabilitiesModel`, ~1.6k lines) already streams **everything heavy** — HardwareInfo inventory+runtime
   (CPU packages/cores + usage/clock histories incl. per-core, memory modules + RAM/swap usage, drives + usage,
   video controllers + monitors + tier labels, network adapters, OS/BIOS/system), FrameworkStatus (model, EC build),
   per-platform cooling details (`FrameworkLaptopXXCoolingDetails` → FanAdvancedInfo cards), temp/fan/battery
   status. **No new gRPC verticals needed** except one small fan-function mapping. The work is layout + display:
   - **Slice 1 — two-pane shell:** replace the current single `ScrollViewer` of 9 stacked section views with the
     mockup's **left category rail** (Onboard devices · CPU · Memory · Storage · Graphics · Network · System
     profile, each with a **count badge**) + right detail pane (only selected section rendered). Reuse the section
     VMs as-is; counts already exist on the page VM.
   - **Slice 2 — Onboard devices:** temperature-sensor tile grid (name + `map-marker` location + °C severity + OK
     — reuse the Thermal tile pattern) + **Fans grid titled by function** ("CPU fan"/"GPU fan", location as
     sub-line). Fan **function** is the one missing datum: extend the Core fan display mapping
     (platform + `FrameworkFanName` → function) and flow the extra string to the client (small vertical).
   - **Slice 3 — CPU:** Packages instance picker (**AMD logo** via `BrandLogoView` — first logo use on this page)
     + icon-led stat tiles (clock/socket/cores/logical/L1-L3/virtualization) + **Per-core detail** grid (% +
     sparkline, hot core → red; data already computed in `BuildCpuCoreUsageHistories`) + Sockets tile.
   - **Slice 4 — Graphics:** adapter picker with AMD/NVIDIA logos; stat tiles (driver version/date, resolution +
     **standard badge** e.g. WQXGA — extend the tier mapper, refresh rate, adapter RAM, monitors).
   - **Slice 5 — Network:** adapter picker with **friendly type names** (Wi-Fi 7 / 2.5 GbE / FE — derive from
     product string + speed, never "802.11") + vendor logos (MediaTek; Realtek → MDI ethernet fallback;
     Bluetooth → Microsoft) + per-adapter tiles + **Internet tile** ("Connected", green) — `ConnectedToInternet`
     is NOT in HardwareInfo; use a client-side Windows connectivity check (no service round-trip).
   - **Slice 6 — Memory + Storage:** instance pickers + the RAM-usage and Storage-usage bars (all data present;
     restyle to global-stats-top + picker + tiles).
   - **Slice 7 — System profile:** single column (Model/SKU/System revision/EC build/BIOS version+date) + fold
     the existing Cooling + Platform-firmware sections into the mockup's **"Cooling hardware & firmware"** block.
5. **Modules** (largest: new page + per-platform layouts + the most new EC plumbing) — data vertical first,
   then FW16 → FW12/13 → Desktop. High-res PDFs exist for all views (`modules-fw16-numpad`,
   `modules-fw16-spacers`, `modules-fw16-widetouchpad`, `modules-fw12-13`, `modules-fw-desktop`).
   **`modules-library/Modules - Library.pdf` is a DEVELOPER REFERENCE ONLY (user instruction 2026-07-02)** —
   it documents how to present every `FrameworkModuleIdentity` / `FrameworkModuleSlotKind` (display name, icon,
   category, slot-kind label, bus/interface, PD wattage, serviceability, one-line description); there is NO
   user-facing Library view. That presentation data still lives in code as an internal catalog helper (identity
   → display metadata) so every layout renders modules consistently — consult the Library PDF +
   `Modules - Library.dc.html` when authoring it.

   **Asset rule (user instruction 2026-07-02): the PDFs draw chassis/modules as simple shapes (gear-in-a-box
   body, rectangle key grids) — the real PNG art in `SubZeroFramework/Assets/Png/` is used instead.** Mapping:
   - Chassis body (map centre): `framework-16-top.png` / `framework-13-top.png` / `framework-13pro-top.png` /
     `framework-12-top.png`; Desktop tower: `framework-desktop-front.png` (front slots view) +
     `framework-desktop-back.png` (rear I/O view).
   - FW16 input deck (picked by module `Identity` at each `FrameworkInputModulePosition`):
     keyboard → `framework-16-keyboard.png`, numpad → `framework-16-numpad-module.png`,
     LED matrix → `framework-16-ledmatrix-module.png`, spacer → `framework-16-spacer-module.png`,
     touchpad → `framework-16-touchpad.png`, touchpad spacer → `framework-16-touchpad-spacer.png`,
     wide touchpad → `framework-16-wide-touchpad.png`.
   - FW12/13 fixed input cover: `framework-12-keyboard.png` / `framework-13-keyboard.png` /
     `framework-13pro-keyboard.png`.
   - PNGs under `Assets/**` are auto-included as Content by the Uno SDK — reference via
     `ms-appx:///Assets/Png/<name>.png`; selected-module highlight = accent Border AROUND the image tile (don't
     tint the art). The PDFs' "View numpad/LED-matrix layout" toggle link is a mock artifact — the real deck
     arrangement comes from the live descriptors, no toggle.

   **Slices:**
   - **M1 — module presentation catalog** (internal, static, zero EC): a Core/UI helper mapping every
     `FrameworkModuleIdentity` + `FrameworkModuleSlotKind` to its display metadata (name, MDI icon, category,
     slot-kind label, bus/interface, PD wattage, serviceability, description) authored from the Library
     reference. NOT a view — it's what the layout views and heroes consume so every module renders
     consistently. Unit-test that every enum member has catalog coverage.
   - **M2 — module-inventory vertical** (the EC plumbing; approved by user 2026-07-02). The data is FINISHED
     in framework-dotnet — the app just never sees it because only the service talks to the EC. Source of
     truth: `IFrameworkEcConnection.GetModuleInventorySnapshot()` (framework-dotnet
     `FrameworkEcConnection.cs`) → `FrameworkModuleInventorySnapshot` (6 `UsbCSlots`, 5 `InputTopRowModules` +
     `InputTouchpad`, fixed internals incl. `ExpansionBay`, 4 `DetachedModules`); each entry is a
     `FrameworkModuleDescriptorSnapshot` whose fields map 1:1 onto the PDF hero (Identity→card type,
     Confidence→"Confirmed" chip, SlotKind, Flags→BuiltIn/Connected/Active pills, VID/PID/BoardID tiles,
     `Position`→FW16 deck placement). Carry it across the gRPC boundary by copying the PD vertical file-for-
     file: **Core read** `SubZeroFramework.Core\Services\FrameworkDataProvider.cs` → Core model (twin of
     `Core\Models\PowerDeliveryPortSnapshot.cs`) → **proto** message + mirrored enums in
     `GrpcContracts\Protos\framework_telemetry.proto` → **service map/serve** in
     `Service\Services\FrameworkTelemetryGrpcService.cs` (+ mapper) → **app client** twin of
     `SubZeroFramework\Services\GrpcPowerDeliveryClient.cs`/`IPowerDeliveryClient.cs` → **VM** `ModulesModel`
     (as `PowerTelemetryModel` consumes PD) → **Core display helpers** for enum→text (the
     `FrameworkSensorNameDisplay` pattern, tested). Joins, not duplicates: per-slot PD state (wattage/DP pill)
     comes from the EXISTING PD stream matched by slot index; webcam/mic privacy + fingerprint state feed the
     internal chips row (add to this slice only if not already streamed).
   - **M3 — FW16 layout** (`Modules - Numpad/Two spacers/Wide touchpad.pdf`): expansion-bay banner
     (`BrandLogoView` AMD/Nvidia + variant name) over the chassis map — slots 1-3 left, 4-6 right flanking
     `framework-16-top.png`, chassis-mirrored like the Power PD section; slot cards (number badge, card-type
     icon, name, Empty state, selected accent) → **Selected expansion module** hero (title "Slot N · <type>
     expansion card", Confirmed chip, flag chips, PD pill "100 W · DP alt-mode" / "No PD contract", 6
     StatTiles). Below: **Input deck & internal modules** — webcam/mic/fingerprint status chips + the
     PNG-based COMPOSABLE deck: **top row = 2 module slots flanking the keyboard** (each slot holds LED matrix
     / numpad / spacer / empty), **touchpad row = touchpad + 2 touchpad spacers OR wide touchpad** — any
     combination of the two rows, assembled from the live descriptors' `Position`+`Identity` (the three PDFs
     are just sample combinations); **Selected input-deck module** hero (7 StatTiles: Module/State/Slot
     kind/Confidence/VID/PID/Board ID).
   - **M4 — FW12 / FW13 / FW13 Pro layouts — THREE SEPARATE VIEWS** (user instruction 2026-07-02;
     `Modules - Framework 12or13.pdf` shows the shared skeleton): each uses its own chassis + keyboard art
     (`framework-12-top/-keyboard`, `framework-13-top/-keyboard`, `framework-13pro-top/-keyboard`). **No
     input-deck slots — the deck is a fixed input cover (keyboard only)**, accent-outlined as one unit with its
     own hero ("Input cover (fixed)" slot kind). Expansion-card slots per chassis flank the top-down PNG;
     internal chips row adds the Touchscreen tile on touch models (FW12). Distinguish 13 vs 13 Pro from the
     platform/model data when picking the view.
   - **M5 — Desktop layout** (`Modules - Framework Desktop.pdf`): `framework-desktop-front.png` with the 2
     front slots connector-linked beneath it; **Rear I/O & mainboard** section ("Fixed · soldered" chip):
     mainboard card + rear port list (HDMI 2.1 / USB4 ×2 / DP 2.1 ×2 / USB-A ×2 / Audio / 5G LAN — static
     per-platform catalog like FanAdvancedInfo, no EC read) with per-port detail tiles (Standard / PHY /
     Features / Security) — reuse `InstancePickerView` for the port list.
   - **M6 — platform switch + verify**: Modules page picks the layout route by `PlatformFamily` (nested-region
     navigation, accessor bridge, deferred QueueSync — the Device-Caps page pattern verbatim); 0/0 both
     targets + tests + live verify on the FW16 (numpad deck) before moving on.
6. **Dashboard — full redesign** (`dashboard/Dashboard.pdf`, 2 pages). Much more than a visual pass:
   - **Cooling profile presets row** (Silent · Balanced · Performance · Turbo · Custom — icon + name + one-line
     description, selected card accent-outlined + check; "applies to all N fans instantly"; header shows Average
     fan speed). NEW concept: a profile = one preset applied to every fan (Custom = per-fan tuned state); needs a
     preset→fan-mode/duty mapping + persistence, driven through the existing staging/actuation services.
   - **Fans quick control** cards (one per fan): name + slot + function chip (CPU/GPU/Sys), rev/s ring gauge,
     "Now driving: <profile> · <duty>%" + progress bar, Auto/Manual/Max segmented, − duty% + stepper, rocket
     (apply/boost). Changes apply live (no staging on the dashboard).
   - **Thermal snapshot** card: 8 sensors as label + horizontal bar + °C (severity-colored), header "driving
     temperature <max>°C". **Power** card: charge ring + Charging state, Adapter W, "Full in ~N min" (ETA needs
     charge rate → time-to-full derivation). **Quick toggles** card: Zero-RPM idle, Sync all fans, Keep 80%
     charge limit, Start with Windows — each toggle wires to an existing capability (idle/link/charge-limit/
     startup).
7. **Settings — new sub-page layout** (`settings/Settings-*.pdf` ×5). Same master-detail + sub-nav pattern as
   Device Capabilities — REUSE the nested-region navigation (sub-nav card full height, "Service reachable"
   status pinned at its bottom):
   - **Service** (`-service.pdf`): green "Reachable over local gRPC — <service> — last check" banner + Recheck;
     UAC note; Restart (primary) / Shut down / Update / Uninstall (danger outline) — existing lifecycle service.
   - **Display units** (`-unitsnet.pdf`): per-quantity segmented pickers with live sample values — Temperature
     °C/°F/K, Fan speed RPM/rev/s/%, Power W/mW, Voltage V/mV, Current A/mA, Battery capacity Wh/Ah/%, Network
     rate Mbps/MB/s/Gbps — over the existing UnitsNet preferences service.
   - **Startup & alerts** (`-startup.pdf`): toggles Start with Windows, Start minimized to tray, Autorun
     service, Thermal alerts (notify on critical sensor — may need a small notification vertical).
   - **Licenses** (`-licenses.pdf`): library list (name, version, license chip) + full license text column.
   - **About** (`-about.pdf`): rows with versions + GitHub links — SubZero, EC build, framework-dotnet,
     framework-system-ffi-extensions, framework-system.
8. **Warnings & Issues — recovery-mode hero** (`warning/…-warning.pdf`): centered red fan badge + "RECOVERY
   MODE" eyebrow + "Background service not installed" headline + explanation; **Install service** (primary) +
   **Restart service** buttons + UAC note; two cards — "Paused in recovery mode" (list: live telemetry &
   history, manual fan control, curve & profile changes, hardware inventory refresh) and "Detected state"
   (Manager: Windows SCM, Registration: Not installed in red, Service name); "Recheck now" link. Drives off the
   existing service-lifecycle status.
9. **Fan Control** redesign pass (functionally built; visual pass to match its PDF/mockup) — lower priority.

## Cross-cutting
- Every new datum = the 6-layer vertical (Core read → proto → service map/stream → client → UI); mirror the PD slice.
- EC **writes** (charge-limit set, fingerprint LED, S0ix reset) gate behind the fan-control authorization service.
- One slice at a time, verify 0/0 + tests, no unrequested scope.
- **Reuse controls + Uno navigation everywhere** (soft rule — new controls when genuinely needed). Every
  master-detail/sub-page = ViewMap/RouteMap nested regions (Device-Caps pattern: accessor bridge, deferred
  QueueSync, DataViewMap for instance details, full-height side panels). **Restyling an existing control is
  fine when it's used only on the page being edited (grep usages first); shared controls get a styling knob
  (DP/style) instead of an in-place restyle. Conversely: extract any repeating XAML fragment into a shared
  control proactively when it lowers total XAML (as StatTileView did for ~50 tile blocks).** Per-page reuse
  mapping:
  - **Dashboard**: fan ring = `BandRingGaugeView`; power ring = `BatteryChargeRingView`; function chips
    (CPU/GPU/Sys) = `FanStatusChipView`; Auto/Manual/Max = the fan page's segmented-pill pattern; thermal bars ≈
    ProgressBar rows (severity brushes from Device-Caps helpers); toggles → shared new control (below).
    NEW controls: **ProfilePresetCardView** (icon + name + description + selected accent, 5×),
    **FanQuickControlCardView** (composes gauge + segments + duty stepper), **ToggleRowView** (icon + title +
    subtitle + ToggleSwitch — used 4× here AND 4× in Settings-startup).
  - **Settings**: sub-nav + detail = the Device-Caps nested-region pattern verbatim (5 routes; sub-nav card
    full height with "Service reachable" pinned at bottom); Service pane reuses the existing lifecycle
    service/commands + status banner ≈ InfoBar-style Border; Startup toggles = **ToggleRowView**; unit rows →
    NEW **UnitSegmentPickerView** (icon + name + live sample + segmented units over the UnitsNet prefs
    service); Licenses/About = plain list layouts (no new controls).
  - **Warnings**: recovery hero is a page layout, not a control — reuses buttons/cards/status brushes; the
    two info cards ≈ `StatTileView`-style Borders with line lists.
  - **Modules**: detail card fields = `StatTileView` in `StatTilePanel` (adaptive widths); confidence chip ≈
    `FanStatusChipView`-style pill; flag pills = the PD pill pattern from Power; vendor logos = `BrandLogoView`;
    Desktop rear-I/O list = `InstancePickerView` (with `EmptyText`). The genuinely new pieces: per-platform
    chassis-map layout views built around the `Assets/Png/` art (see §5 asset mapping), a shared
    **ModuleSlotCardView** (number badge + icon + name + empty/selected states, used by every map), and a shared
    **selected-module hero** (icon + title + confidence/flag chips + PD pill + StatTilePanel — same control
    serves both the expansion-card hero and the input-deck hero).
