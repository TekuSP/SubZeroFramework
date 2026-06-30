# SubZero redesign — implementation plan

Source of truth: `Design Ideas/design_handoff_redesign/` (README + 9 `.dc.html` mockups + `screenshots/`).
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
- **Power Telemetry** — PD per-port section ✅ already built. Add: **power-flow** (adapter→system→battery, derived
  from active PD port V×A + battery V×A), **battery charge ring** + details, **charge limits** read + a
  `SetChargeLimits` control (gated by fan-control auth), trends.
- **Device Capabilities** — `HardwareInfo` (CPU packages/cores, GPU, network, memory, storage, system profile) +
  onboard fans (`FrameworkFanName` function CPU/GPU as title). Vendor logos via the mapper.
- **Modules (NEW page + rail item)** — **physical-map → selected-detail** pattern, **layout chosen by
  `PlatformFamily`**: FW16 (3 input-deck variants: LED-matrix+spacers / numpad / wide-touchpad — driven by
  input-deck `Position`+`Identity`+width), FW12/13 (4 USB-C, combined input cover, touchscreen tile), FW Desktop
  (tower front slots + rear I/O), + **Module Library** (static reference catalog of every identity/slot-kind).
  Consumes: module inventory + per-slot PD + `Position` + expansion-bay GPU descriptor + privacy switches +
  fingerprint. Most data needs the Core→proto→client vertical (only PD is wired today).

## Phasing
1. **Foundation** — resolver DONE; Modules rail entry + stub DONE; title-bar/rail chrome restyle CANCELLED by user
   (keep the existing title bar + nav rail as-is — only page content changes).
2. **Thermal** — DONE (see above). Proves the telemetry-redesign pattern for Power/Device-Caps/Modules.
3. **Power** (extends the existing PD work: power-flow + battery + charge-limits control).
4. **Device Capabilities** (mostly HardwareInfo, already partly flowing).
5. **Modules** (largest: new page + per-platform layouts + the most new EC plumbing) — do the **Library** first
   (static, no EC), then FW16, then FW12/13, then Desktop.

## Cross-cutting
- Every new datum = the 6-layer vertical (Core read → proto → service map/stream → client → UI); mirror the PD slice.
- EC **writes** (charge-limit set, fingerprint LED, S0ix reset) gate behind the fan-control authorization service.
- One slice at a time, verify 0/0 + tests, no unrequested scope.
