# SubZeroFramework

SubZeroFramework is a cross-platform Framework laptop companion split between an Uno/WinUI client and a local background service. The service owns privileged Framework EC access, hardware polling, inventory fallback mapping, and fan-control writes; the client stays unprivileged and consumes status, telemetry, inventory, and lifecycle guidance over local gRPC.

## Current progress

- Dashboard and Device Capabilities are the most mature UI surfaces. Device Capabilities now follows the dashboard card language, keeps stable card identity during refresh, uses Framework-first inventory data, and fills remaining gaps through Hardware.Info behind the service boundary, including CPU package cards with recent average-usage and frequency history, usage-colored per-core cards, explicit GPU ↔ monitor linking, current monitor mode details, and graphics-card groupings that keep unlinked monitors in an explicit Unknown graphics card bucket.
- The client now supports user-selectable display units for temperature, fan speed, clock frequency, refresh rate, information size, voltage, current, battery charge capacity, ratio or fraction, length, airflow, network link speed, and power. Settings persists those choices locally for the client, and dashboard, thermal, power, fan, and Device Capabilities surfaces plus chart axis labelers update without changing service-side canonical units.
- The service boundary is in place for status, telemetry, inventory, and fan-control commands. Socket/path hardening, reconnect handling, shared DynamicData-backed streams, and fan-command authorization gating are implemented.
- Recent UI polish includes dB(A)-normalized desktop fan acoustic displays with max-noise support, explicit Adapter labels and numbered monitor cards in Device Capabilities, wrap-friendly monitor and network card layouts, Unknown rendering for sentinel network speeds, and padded dashboard fan-history axes to prevent clipping.
- Settings and Warnings and Issues now surface service health, install readiness, privilege prompts, and lifecycle actions for shutdown, restart, autorun, install, update, and uninstall.
- Install and update no longer require an elevated client UI. The packaged service executable supports `--service-management` operations and can register or refresh itself when launched with administrator or root privileges.
- CI now publishes `service-package/windows` and `service-package/linux` bundles next to the app artifacts so the client can discover the install or update source automatically.
- The latest validation slice completed with a clean zero-warning solution build and passing tests.

## Key documents

- [CHANGELOG.md](CHANGELOG.md)
- [WorkToBeDone.md](WorkToBeDone.md)
- [Architecture.md](Architecture.md)
- [FunctionalitySpecification.md](FunctionalitySpecification.md)
- [SubZeroFramework.Service/README.md](SubZeroFramework.Service/README.md)
- [SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md](SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md)
- [SubZeroFramework/Docs/FanSafetyShutdownChecklist.md](SubZeroFramework/Docs/FanSafetyShutdownChecklist.md)
