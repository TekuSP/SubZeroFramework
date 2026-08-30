# Changelog

All notable changes to this repository should be documented in this file.

## [0.2.2] - Unreleased

### Added

- **Adaptive asks the controller whether the processor is throttling, instead of guessing.** It used to infer
  it from a performance ratio, which also falls for power limits, parked cores and a workload that simply
  stopped — none of which is a thermal emergency, and each of which spun the fan up for a reason nobody could
  explain. The embedded controller now states it directly, and distinguishes trimming clocks from protecting
  the silicon, so the fan's response is proportionate to which is happening. Firmware that cannot answer keeps
  the old behaviour rather than losing throttle handling.

- **Temperature sensors are labelled with the firmware's own names.** Where the controller has a name for a
  sensor, that is what the chips and the thermal page show, instead of a position like "Temp 0". The name is
  the one printed in the service manual, so it is the one worth searching for.

- **The firmware's own limits are visible.** The thermal history chart marks where the controller starts
  warning, and the Adaptive target cannot be set above that point — a target beyond it is one the machine
  would never be left to hold, and the fan behaviour that followed read as this app misbehaving.

- **Embedded controller health on Warnings & Issues.** A new card reports what the controller says about
  itself: the processor being held back, a recorded panic, firmware write protection left off, or a machine
  running its recovery image. Only conditions that are true are listed, and the card is absent entirely on
  firmware that does not answer — an empty card would read as "no problems" rather than "cannot be asked".

- **What each USB-C port actually negotiated.** Alongside the live voltage and current, and the slot's own
  rating, ports now show the contract this cable and this charger agreed on, and say so when it falls well
  short of what the slot supports. Between them the three answer "why is this charging so slowly", which no
  one of them could.

- **Pack health on Power Telemetry.** A section, read on request rather than polled, showing per-cell voltages
  with the spread called out, the pack's age beside its cycle count, what it is asking the charger for, and
  its own state of health. Cell drift is the early sign of a failing battery and nothing in the app showed it.
  A sealed pack says so plainly instead of showing blanks.

- **Component firmware in Device Capabilities.** Versions for cameras, USB hubs, audio, input modules, the
  power-delivery controllers, the retimer and NVMe drives, under the platform firmware they sit beneath.
  Groups the machine does not have are omitted rather than shown empty. Each Modules slot card also names the
  firmware of the module in it, matched by vendor and product id rather than by slot number — the peripheral
  descriptors number slots by USB enumeration order, which bears no relation to the physical positions that
  page draws. A drive whose firmware the operating system does not report now falls back to what the drive
  itself says.

  The proto gained fields for all of the above, so **the service and the app must be rebuilt together.**

### Changed

- **Adaptive fans are driven by duty percent, always.** Where calibration judged that the embedded controller
  could hold a commanded speed, the demand used to be converted to an RPM setpoint and handed to the EC's own
  loop. That loop clamps the request to the firmware's configured maximum for the channel — around 4900 RPM on
  Framework 16, where the same fan reaches past 6200 on a direct duty write. The top quarter of the range could
  not be asked for: the fan sat well short of a speed nothing was ever going to deliver, and because the
  shortfall happened inside the firmware rather than at the controller's own clamp, the trim wound to +100%
  against a ceiling it had no way to see. The learner was being taught from duty values the EC had quietly
  replaced, so the model drifted away from the machine it was supposed to describe.

  Its one advantage was the firmware holding a speed steady as a fan ages — which is exactly what the adaptive
  learner already does, so removing it costs nothing. Speeds are still shown: each duty demand is reported
  alongside the speed it is expected to produce, interpolated between the two speeds calibration actually
  measures (the minimum-spin point and full duty) rather than scaled off the maximum, which read far too low
  across the lower half of the range. Calibration no longer probes speed tracking and is one settle shorter.

  **Re-run calibration on each fan** to pick up the full-speed reading the new display is anchored to.

### Fixed

- **The installed 0.2.1 app could not start.** It died on launch with `Could not load file or assembly
  'SubZeroFramework.GrpcContracts, Version=0.2.1.0'` while every local build ran fine. CI built the app with
  the run's version, then built the test project without it — and that second build walks
  Tests → Service → GrpcContracts. GrpcContracts is the only shared project with a single target framework, so
  where Core and Service hand the app head and the tests separate `net10.0-windows10.0.26100` and `net10.0`
  outputs, GrpcContracts hands both the same file. The test build recompiled it at the repository's default
  version, overwriting the copy the app had just been compiled against, and `dotnet publish --no-build`
  shipped that older stamp. The runtime refuses a lower assembly version than the one recorded in the
  reference, and reports it as a missing file. The test builds now carry the same version, and the payload's
  assembly versions are asserted before the installer is built, so a repeat cannot reach a release.

- **The fan editor's body could show a different mode than the mode selector.** Opening a fan whose mode had
  changed while you were elsewhere — switching cooling profiles, which happens on the Dashboard — left the
  detail pane on "Auto mode active" under a selector reading Adaptive. The pane is a navigation sub-region
  whose default route is Auto, and the guard that kept it in sync remembered the mode it had last *asked* for
  rather than the one on screen, so once the region re-attached on its default the guard suppressed the very
  navigation that would have corrected it.

- **A profile that failed to apply could be silently redefined.** Applying a profile whose fans the service
  refused left those fans on Auto, and the auto-save then captured that as the profile's new meaning — a
  profile named for the mode it no longer set. Auto-save is now suppressed for a settle window after an apply
  rather than only for the call, and skipped entirely when the apply reported a fan that did not take.

## [0.2.0] - Unreleased

### Added

- **Cooling profiles.** A profile is a named setup covering every fan, switched from a shelf of cards on the
  Dashboard. Applying one puts every fan into the state it describes in a single action, and the profile you
  are on tints the title bar and the navigation rail in a colour you choose, so the machine's cooling mood is
  visible from any page rather than only from the page that set it.

  The library lives in the **service**, alongside the fan state it describes, and streams to every connected
  client — a profile saved in one window appears in another without either side polling, and the selection
  survives a restart. What the fans are actually *doing* remains the fan state store's answer alone: the
  service stores a library and a label, never a competing command, and the remembered selection is a name for
  the current setup rather than an instruction replayed at startup. Whether the fans still match the profile
  is computed on the client by comparing against live state, which is what the Modified prompt reports — so
  changing a fan by hand stops the app claiming a profile is in effect at exactly the moment it stops being
  true.

  A profile carries its own **curve points** rather than a reference to a numbered slot, so overwriting a
  fan's saved curve cannot silently change what an unrelated profile means. Fans start on Auto when a profile
  is created; to make it anything else, select it, change the fans, and save those changes back into it.
  Renaming, editing appearance and deleting are on the cards themselves, and the library is never empty —
  deleting the last profile restores the Default baseline and applies it.

- **Adaptive fan control.** A per-fan mode that holds a temperature target instead of following a fixed
  curve. It calibrates the fan first — finding the duty at which the blades actually start turning, how much
  cooling each additional percent buys, and how long the heat takes to answer — and keeps refining that model
  while it runs, so it follows a repasted heatsink or a dust-blocked intake rather than needing a curve
  redrawn by hand. Calibration cools the machine down between its measurement phases and holds the sibling
  fan at a floor while it works, so measuring one fan does not cook the component the other one is cooling.

- **Update notifications.** The app checks GitHub Releases and says when a newer version exists, naming the
  version and offering a button to the release page, with a "Check for updates" item in the navigation rail
  that turns amber while an update is outstanding. It never downloads or installs anything by itself. The
  automatic check is once a day, uses conditional requests so a repeat check costs nothing, and can be turned
  off in Settings; pressing the button always checks regardless.

- **The Dashboard's fan cards show the last 60 seconds.** The same fan-speed and driving-temperature history
  the fan detail editor draws, spanning the card and fading out under the readout. Both lines are resampled
  onto one shared timeline, so the two describe the same instants rather than one running out of points
  early.

- **Applying a profile can raise a desktop notification**, naming what each fan was set to, when status
  notifications are enabled.

### Changed

- **The mode row on the Dashboard reports all five modes.** Adaptive had no segment, so a fan running it lit
  none of the four and read as being in no mode at all — the one state hardest to infer from the numbers
  beside it.

- **"Reset fan settings to factory defaults" now means factory new.** Alongside returning every fan to
  automatic control it clears the cooling profile library, display units, and the startup, alert and
  update-check settings, then re-seeds and selects the Default profile. Previously it left everything outside
  fan state untouched, which made "factory defaults" a half-wipe.

- **Dependency and security updates.** The full NuGet dependency set was moved forward, picking up upstream
  security fixes along with it.

## [0.1.7] - 2026-08-23 (released as v0.1.7)

### Fixed

- **Five findings from a private security advisory are closed**, and the one that remains open is documented
  rather than quietly carried. See [SECURITY.md](SECURITY.md) for the shipped posture.

- **The Linux service could not persist its own settings.** `ProtectSystem=full` made `/etc` read-only for
  the unit, so every configuration write failed. Fixes #72.

- **A single failing hardware probe no longer skips the rest.** Each static inventory probe is isolated, so
  one unreadable subsystem cannot blank out the others.

- **Linux kernel device-name resolution is more robust.**

### Added

- **Linux: physical drives** enumerated from `/sys/block`, and **memory modules** from SMBIOS type 17.
- **Linux: the graphics driver version** read from the DRM `VERSION` ioctl.

## [0.1.6] - 2026-08-06 (released as v0.1.6)

### Fixed

- **Fedora package dependencies.**

### Changed

- Dependency updates, including Uno.Sdk.

## [0.1.5] - 2026-07-27 (released as v0.1.5)

### Added

- **Windows: live GPU and NPU utilization.** Each graphics adapter's detail now carries its own live
  utilization — the current percentage over a rolling 30-second graph, the same card the CPU uses per core —
  and a new **Neural processor** category presents the NPU the way the CPU category presents packages: a
  count, a processor list, and the picked device's detail. On a Framework 16 that is the integrated Radeon,
  the discrete GPU module and the Ryzen AI NPU, each with its own reading, never blended into one number.
  The service reads the Windows GPU Engine performance counters through one persistent PDH query (~1.5 ms per
  second-tick, measured) and derives busy time per adapter the same way Task Manager does; devices are named
  via SetupAPI and matched by adapter LUID. Software adapters with no physical device behind them (WARP) are
  excluded. The Windows readers and their interop are compiled only into Windows builds — Linux ships none of
  it, needs nothing new at install time, and shows an honest empty state until its per-vendor readers land.

- **Linux: Device Capabilities finally shows graphics and displays.** The page was empty on Linux because
  Hardware.Info enumerates both lists by shelling out to `xrandr`, which needs a display server the background
  service does not have. They now come from the kernel instead: adapters from `/sys/class/drm` with their
  names resolved through the system `pci.ids` database, and displays from each DRM connector's EDID — model,
  manufacturer, product code, serial, manufacture date, physical size and density. Works headless, and
  identically under X11, Wayland or no session at all. The current resolution and refresh rate come from the
  kernel's mode-setting state, so a 165 Hz panel reads 165 Hz rather than the 60 Hz its EDID advertises as
  "preferred". Because a connector belongs to its card by kernel topology, each display is attributed to the
  right GPU exactly, instead of by matching names as on Windows.

- **Linux: GPU utilization for AMD, NVIDIA and Intel.** Same cards as on Windows, one per GPU. AMD reads the
  amdgpu driver's own `gpu_busy_percent`. NVIDIA goes through NVML, loaded at runtime only if the proprietary
  driver is already installed — there is no new package dependency, and on an AMD-only machine the attempt is
  silently skipped. Intel reads the i915 or xe performance counters, discovering the available engines from
  what the driver advertises rather than assuming a layout.

  Sleeping GPUs are not woken to be measured. Reading AMD's busy attribute reaches the SMU, and an NVML query
  takes a power-management reference on the card; either can hold a discrete GPU awake, which is how monitoring
  tools have historically ruined laptop battery life. A GPU whose power state already reads "suspended" is
  reported as 0% — which is what it is — without being touched.

  Each source is independent and optional: one vendor's driver misbehaving cannot blank out the others. Where
  a reading genuinely cannot be taken the device simply reports nothing. In particular Intel's counters need
  kernel 6.15 or newer on the newest GPUs (the xe driver), and the alternatives — inferring load from power
  residency, or summing per-process figures — were rejected as misleading rather than shipped as an
  approximation.

- **Linux: NPU utilization.** Intel NPUs (Core Ultra) report through the driver's busy-time counter, which
  costs nothing to read and leaves an idle NPU asleep. AMD Ryzen AI NPUs report per-column utilization, on
  kernels new enough to expose it and on the Ryzen AI 300 parts where it is wired up; where it is not
  available the device is still listed, just without a reading. As with the GPUs, a sleeping NPU is reported
  as 0% rather than being woken to be asked — for AMD that gate is the difference between a monitoring
  feature and a battery-life regression.

- **The Neural processor page now describes the hardware, not just its load.** Vendor, driver and driver
  version, firmware version, location and description sit alongside the utilization graph, the way the CPU
  and graphics pages have always shown their devices. Windows reads them from the device properties; Linux
  from the kernel's accelerator class, including the NPU firmware version where the driver publishes one.

- **Device Capabilities categories with nothing in them are disabled.** A category whose body could only show
  an empty state no longer opens: the rail entry dims and says why on hover ("No graphics detected"). Onboard
  devices and System profile always stay open. This is what a Linux machine sees for Graphics today, and what
  any machine sees for a category its hardware does not have.

- **Settings → Service logs.** Shows what the background service has logged since it started, with a level
  filter and a one-click "Copy all" for pasting into a bug report — no more hunting through Event Viewer or
  `journalctl`. The service mirrors its log into a bounded in-memory buffer (the last 2,000 entries) that sits
  alongside the platform sinks rather than replacing them; when older entries have been dropped the page says
  so instead of implying a complete history.

### Changed

- **Settings ▸ Service logs is now Settings ▸ Logs, and shows the app's logs too.** The page only ever showed
  the background service. The app's own records had nowhere to go on Windows at all: the desktop head is a
  GUI-subsystem binary, so its console sink writes to a console that does not exist and its debug sink only
  exists under a debugger. A released build could warn about a broken service connection every second and none
  of it would reach you. The app now keeps the same bounded buffer the service does, and the page interleaves
  both by timestamp with a source column and a Service / App / Both filter — so a client reconnect warning
  reads directly next to the service restart that caused it. Copy-all tags each line with its source, because
  "which process said this" is the first question asked of a pasted log. If the service cannot be reached, its
  half fails alone and the app's own entries still appear — those are exactly the ones explaining why it looks
  dead.

- **Telemetry no longer reaches the UI faster than the UI can use it.** Thirty-five subscriptions across the
  view models and telemetry clients inherited the service's poll cadence directly, so lowering the poll
  interval multiplied UI work with it. Each now states its own ceiling — 250 ms for live readouts, 500 ms for
  chart history, 1 s for inventory — applied *before* marshalling to the UI thread, so coalescing happens off
  it. These are ceilings, not floors: the operators only emit when something actually arrived, so a
  one-second poll still updates once a second.

### Fixed

- **Two code-analysis rules had been silently inert.** SZF0004 (require `ObserveOn`) and SZF0005 (require
  `DisposeWith`) skipped every subscription whose receiver was typed as `IObservable<T>` — which is nearly all
  of them, since every `Watch*`/`Connect*` method and every Rx operator returns the interface. The type check
  compared a display string (`System.IObservable<T>`) against a metadata name (``System.IObservable`1``),
  which never matches for a generic, and the interface check looked at implemented interfaces, which do not
  include the type itself. Both rules now apply where they always should have.

  Fixing that exposed ~53 violations, and working through them showed the rules had been over-broad as well as
  under-applied. They now recognise the legitimate patterns they used to flag: `Observable.Create` factories
  (whose disposal is the returned disposable, and whose scheduler is the consumer's choice), ownership by
  storage in a member, `SerialDisposable` slots and keyed registries, `using var`, returning the subscription
  to a caller, and marshalling inside the handler via the dispatcher rather than upstream. `ObserveOn` is also
  no longer required in the service and Core, which have no UI thread for it to marshal to.

- **New rule: SZF0013, telemetry subscriptions must rate-limit.** Flags a subscription that starts at a
  per-poll telemetry source without bounding its rate. The operator it asks for depends on the stream: change
  sets must use `Batch`, which coalesces, because `Sample` and `Throttle` *drop* items and a dropped change set
  loses an add or a remove permanently — DynamicData ships no `Sample`/`Throttle` for change sets for exactly
  this reason. Snapshot streams carry the whole value each time and may use either.

- **Uninstalling from inside the app now uninstalls the app.** The Uninstall button used to delete the
  background service's registration — the same registration the Windows installer owns — so removing SubZero
  afterwards through Add/Remove Programs left Windows Installer deleting a service that was already gone. On
  an installed build the button now hands over to the real uninstaller and closes the app so its files can be
  removed; the installer stops and removes the service itself. It asks first, and says plainly that saved fan
  profiles are kept in case you reinstall. On a development build, where there is no installer, it still
  removes just the service and the button says so.

### Fixed

- **Error status text was unreadable.** "Unavailable", "Stalled", "Not Present", a critical battery and a
  ≥85 °C reading were drawn in the palette's dark red *fill* tone (#442726) as foreground — near-invisible on
  the dark card. They now use the error text tone, like the rest of the app. Same swap for the storage/memory
  usage bar past 90%, whose fill was the odd one out next to its bright green and amber siblings.
- **A newer service could kill the telemetry stream on an older client.** The app's copy of the wire enum
  mapper parsed an unknown value into the *first* member instead of rejecting it, producing a valid-looking
  channel identity that collided with a real sensor — a GPU percentage could land in the thermal cache as
  degrees. Unknown values are now rejected on both sides and the offending update is skipped rather than
  throwing, so an unrecognized channel is ignored instead of ending the subscription.

- **"Apply all" put an unselected fan back on its curve instead of applying the mode you staged.** Staging
  Auto (or Manual/Max) on one fan, switching to another, and applying reached the first fan through the wrong
  branch: it still looked curve-driven to the service — which is exactly what the staged change was about to
  end — so its parked curve draft was re-applied and re-activated, and the staged mode was cleared without ever
  running. The fan came back reporting no unsaved changes, still on Custom curve. A staged simple mode is now
  applied ahead of any parked curve draft, matching the order the staged-work check already used.
- **The UI thread was doing chart work it should never have seen.** Every telemetry history subscription
  re-emits its whole window (~3×/s per series) and was sorted and rebuilt into an array *after* being marshalled
  to the UI thread — once several series were live (one per sensor, per fan, per battery metric) that starved
  rendering and left pages half-painted. History streams are now sampled and projected off the UI thread, which
  only receives the finished array. Fixed in the fan/temperature history store, thermal telemetry and power
  telemetry; the telemetry UI guide documented the broken order and now documents the correct one.
- **A fan handed back to firmware control still reported the duty it was last commanded.** When no driving
  sensor can be read the service stops driving the fan, but the remembered duty stayed on its control state,
  so clients read it as a speed the curve was still holding. It is now cleared as part of the handover; the
  mode, curve points and driving sensors are untouched, so the profile resumes intact when a sensor returns.

## [0.1.4] - 2026-07-25 (released as v0.1.4)

Sensor-availability release: what happens to a fan curve when the hardware it watches goes dark.

### Added

- **"Treat missing readings as 0°" per curve profile.** A selected driving sensor that loses power counts as
  cold instead of dropping out — for sensors that go quiet because whatever they measure is switched off, like
  a sleeping GPU. With Maximum aggregation a dark sensor then simply stops mattering; with the other modes it
  pulls the driving temperature down, so the editor warns about that combination.
- **The curve chart shows where fan control actually is.** A red ✕ rides the curve at the live driving
  temperature and the duty the service last applied. Because that duty already includes the CPU boost, the
  marker can sit above the drawn curve — the gap is the boost. It disappears whenever no driving temperature
  can be read, rather than freezing at a position the fan is no longer holding.
- **Windows installer offers a clean install.** A new wizard page (before the install location) asks whether
  to keep existing fan settings; ticking it deletes the service's saved configuration instead of upgrading
  onto it. Off by default, and available unattended as `msiexec /i … CLEANINSTALL=1`. Per-user preferences
  (display units, startup, alerts) are not touched — the installer only owns machine-wide service data.

### Fixed

- **A sleeping GPU rewrote the fan's saved profile.** When a driving sensor became unavailable the editor
  deselected it and adopted whichever sensor happened to be first (Mainboard), and the next Apply persisted
  that — the curve silently stopped following the GPU for good. Availability is runtime status, not user data:
  a selection now survives its sensors going dark, comes back when they return, and is only auto-seeded for a
  slot that has no selection at all.
- **A powered-down sensor could under-cool the machine.** The service read every selected sensor's raw value
  without checking whether it was reporting, so a dark sensor's 0 °C was folded into the aggregate — halving
  an Average against a hot CPU. Sensor state is now honored, and the client and service share one
  implementation of the reduction (`FanDrivingTemperature`), so the predicted duty matches what the fan does.
- **"Falls back to its firmware-safe curve" is now true.** When no driving sensor of a curve-driven fan could
  be read, the service simply skipped the fan — leaving the embedded controller holding the last duty the
  curve asked for, with nothing observing the heat. It now hands that fan back to firmware fan control (once
  per episode, retried if the handover fails) and resumes the curve automatically when a sensor reports again.
  The stored profile is never altered by the fallback.
- **Opening Fan Control showed the Auto panel for a fan that was not on Auto.** The mode selector correctly
  read "Custom curve" while the body below it described automatic control. The mode body is a navigation
  sub-region whose navigator does not exist yet on first entry; the sync gave up in that case and nothing ever
  retried, because a fan already running a curve never raises a mode change to retrigger it.
- **The sensor list changed length when the GPU powered down.** Its four sensors do not report the same way —
  three go "not powered" while the die sensor goes "not present" — and a not-present sensor was deleted from
  the selector outright, so eight chips became seven and a selected sensor disappeared from view. A sensor
  that has never reported is still omitted, but one already on screen now stays put and dims.
- **An unpowered sensor could not be picked as a driving sensor.** Its chip was disabled, so a GPU sensor was
  unselectable exactly when you wanted to choose it (with the GPU idle) — and a selected sensor that went dark
  could not be deselected either. Chips now stay interactive and simply dim while they have no reading.
- **Windows: the taskbar showed a generic window icon.** An unpackaged WinUI 3 window does not inherit the
  icon embedded in the executable, so the app icon appeared in Explorer but not on the taskbar or in Alt+Tab.

## [0.1.3] - 2026-07-25 (released as v0.1.3)

### Added

- **Settings → Service: "Reset fan settings to factory defaults".** Returns every fan to the controller's
  automatic mode and deletes all saved fan settings in one confirmed action — curve profiles in every slot,
  the active profile per fan, "Applies to" links, CPU boost, and manual/max overrides — including entries for
  fan indices the hardware no longer reports, which no per-fan action could reach. Display units, startup and
  alert settings are untouched.

### Fixed

- **Curve points could be dragged off the chart and stranded there.** The editor let a point be parked
  outside the plotted temperature window (dragging past the plot edge keeps producing coordinates out in the
  axis margin), where it was invisible and could never be grabbed again — the curve became uneditable. Points
  now snap into a band inside the visible window, and curves already holding stranded points are pulled back
  when they load. Pressing empty chart space also picks the new point straight up, so press-drag places a
  point in one motion.
- **A curve no longer flatlines above its last point.** The hidden anchor closing every curve at the top of
  the temperature range inherited the last point's duty, so a curve ending at 0 % (or any low duty) held that
  speed all the way to 130 °C instead of climbing. It is now pinned to full speed, matching the idle anchor at
  the cold end: every curve ramps from idle to maximum across the range, however early the user's own points
  stop. The client's drawn curve, its predicted-duty readout, and the service's actuation now share ONE
  implementation of that rule (`FanCurveDomain`), so a preview cannot promise a speed the fan does not deliver.

## [0.1.2] - 2026-07-23 (released as v0.1.2-alpha)

### Fixed

- **Linux: constant CPU spikes from `lshw`.** Static hardware inventory (RAM modules, drives,
  motherboard, BIOS, network adapters, OS identity) was refreshed on the hardware-info poll — every
  second by default — and on Linux the memory and drive lists each spawn a full `lshw` device-tree
  probe, so making `lshw` a dependency in 0.1.1 turned that into two heavy probes per second. Static
  inventory now refreshes every 10 minutes (still catching USB drives and network changes), while CPU
  usage (the fan-boost input) and memory free/used keep the fast cadence.

## [0.1.1] - 2026-07-23 (released as v0.1.1-alpha)

First post-release fixes, driven by field reports.

### Fixed

- **Framework 16: an unreporting expansion bay locked the whole app into recovery mode**
  ([#51](https://github.com/TekuSP/SubZeroFramework/issues/51)). Bay configurations whose EC answers
  "Unavailable" put that message into the service's global error state on every poll, which the app
  treats as unhealthy — while fans and thermals were reading perfectly. The bay is now presented as an
  **empty bay** (distinct from "could not read"), the app works normally, and the condition is logged
  once per connection instead of a stack trace every poll.
- **Missing hardware-probe tools no longer spam the journal.** A tool Hardware.Info shells out to but
  which is not installed (e.g. `lshw`) produced two warning+stack-trace entries per poll; it now logs a
  single actionable line naming the tool and what installing it enables.

### Changed

- **Linux packages now depend on `lshw`** (deb/rpm/Arch): the service uses it for the memory and
  storage inventory in Device Capabilities. Tarball installs should install it manually.
- Added [docs/INSTALL.md](docs/INSTALL.md) with per-platform install/uninstall commands, linked from
  the README.

## [0.1.0] - 2026-07-22 (first public MVP, released as v0.1.0-alpha)

First public release. Ships the redesigned app with fan control (Auto / Manual / Max / Custom curve with
staging + live preview safety holds), live thermal/power telemetry, device capabilities, client-local
display units, and a hardened background service with installers for Windows and Linux.

Deliberately gated for the MVP: the Modules tab is disabled (FFI slot-reporting gaps), Dashboard cooling
profile presets render grayed (not supported yet), and the Dashboard is a read-only overview — fan control
lives on Fan Curve Profiles.

### Added

- **Fan Curve Profiles** page: per-fan curve profile slots (up to 5), driving-sensor selection with
  aggregation modes, follow links ("Applies to" fan groups), Stage → Preview → Apply flow with a
  service-side preview watchdog that reverts uncommitted previews if the client disappears.
- **Service-side curve drive**: the background service actuates stored curves against live temperatures
  (identical interpolation to the client preview), restores persisted Manual/Max overrides after restart,
  and returns fans to automatic EC control on every shutdown path.
- **CPU boost (usage modifier)**: optional per-fan exponential feed-forward — up to a configured extra duty
  on top of the curve as CPU load rises, smoothed fast-attack/slow-decay so fans ramp before heat reaches
  the sensors without oscillating. Configurable from the Fan Curve Profiles page in Custom curve mode.
- **Redesigned pages**: Dashboard (live overview), Thermal Telemetry, Power Telemetry (incl. battery charge
  limits and USB-C PD ports), Device Capabilities, Settings (Service / Display units / Startup & alerts /
  Licenses / About), and Warnings & Issues (recovery hero covering all service states).
- Client-local **display-unit preferences** for 13 quantities (temperature, fan speed, ratio, power, …)
  applied instantly app-wide and persisted per-user; all UI values route through UnitsNet formatting.
- **Start with system boot**: launch-at-sign-in registration on Windows (Run key) and Linux (freedesktop
  autostart), backed by the cross-platform AutoLaunch library.
- **Settings → Licenses**: a build-time license report covering the full transitive NuGet dependency
  closure of the app, Core, GrpcContracts and Service projects, plus the native components vendored
  inside FrameworkDotnet. Each entry carries the package's own embedded license file where it ships one,
  a canonical SPDX text where the package declares an identifier we hold a template for, and
  "Unknown license terms" otherwise — nothing is ever guessed.
- **Thermal alerts**: opt-in desktop notification when a sensor crosses the critical band (85 °C), with
  per-sensor hysteresis and cooldown and a "Send test notification" button. Delivery via the
  DesktopNotificationsFixed library — native toasts on Windows, `org.freedesktop.Notifications` (D-Bus)
  on Linux.
- **Service lifecycle management**: `--service-management` CLI (install / update / uninstall / restart /
  autorun) used by both the in-app Settings/Warnings actions and the installers; SCM restart-on-failure
  configured on install.
- **Packaging & CI**: Windows MSI installer (WiX, x64/arm64) that lays down the app + packaged service
  and registers the service declaratively (auto-start, restart-on-failure, stop-before-upgrade,
  deregister-on-uninstall); Linux `.deb`/`.rpm`/tarball/AUR packages with systemd enable-on-install;
  unit tests gate all publish jobs; a Windows startup smoke test runs on the artifact.

### Changed

- Fan control commands are fail-closed behind explicit service configuration
  (`FrameworkService:AllowFanControlCommands`) over a local-only Unix domain socket with endpoint
  hardening; the shipped caller-identity posture is documented in `Docs/IpcAuthorizationAndUiCadence.md`.
- Service host hardening: fatal worker faults restore fans to automatic control and terminate with a
  non-zero exit code so SCM/systemd recovery restarts the service (a clean .NET host stop would otherwise
  never trigger recovery); shutdown timeout raised to 90 s to guarantee the fan restore completes.
- UI + service + installer share a single version stamped from one property (`Directory.Build.props`).

### Fixed

- The curve worker's interpolated duties are rounded to the whole percent the EC accepts — previously every
  fractional write failed silently and stored curves were reported active but never actuated.
- The legacy custom-curve commit path no longer wipes a fan's stored profile slots, link, and CPU boost
  from the persisted configuration (it now persists the full control state like every other command).
- Setting a CPU boost during a live preview is rejected instead of silently committing the uncommitted
  preview and disarming its safety revert.
- Stale CPU readings (failed or stopped hardware polling) decay the CPU boost instead of freezing it at the
  last value; a sustained missing usage source with modifiers configured logs a warning.
- Switching a fan into Custom curve now stages immediately (pending pill + Preview) without requiring a
  curve-point edit first; discarding a staged activation exits the editor cleanly.
- Windows toast registration works for the self-contained unpackaged app on WindowsAppSDK 2.3.1 (the 1.x
  `Register()` failure is gone).

### From the earlier unreleased log (2026-05-24, folded into 0.1.0)

- Client-local display-unit preferences and the Units section in Settings.
- UnitsNet-backed formatting/conversion across cards, inventory surfaces, cooling summaries, chart axes.
- Desktop fan acoustic-noise normalization to dB(A); sentinel network link speeds render as Unknown;
  dashboard mini-chart axis headroom; serialized unit-preference writes.
