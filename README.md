# SubZeroFramework

SubZeroFramework is a cross-platform Framework laptop companion split between an Uno/WinUI client and a local background service. The service owns privileged Framework EC access, hardware polling, inventory fallback mapping, and fan-control writes; the client stays unprivileged and consumes status, telemetry, inventory, and lifecycle guidance over local gRPC.

## Current progress

- Dashboard and Device Capabilities are the most mature UI surfaces. Device Capabilities now follows the dashboard card language, keeps stable card identity during refresh, uses Framework-first inventory data, and fills remaining gaps through Hardware.Info behind the service boundary, including explicit GPU ↔ monitor linking, monitor current mode details, and additive card-level per-core CPU detail from the updated Hardware.Info path.
- The service boundary is in place for status, telemetry, inventory, and fan-control commands. Socket/path hardening, reconnect handling, shared DynamicData-backed streams, and fan-command authorization gating are implemented.
- Settings and Warnings and Issues now surface service health, install readiness, privilege prompts, and lifecycle actions for shutdown, restart, autorun, install, update, and uninstall.
- Install and update no longer require an elevated client UI. The packaged service executable supports `--service-management` operations and can register or refresh itself when launched with administrator or root privileges.
- CI now publishes `service-package/windows` and `service-package/linux` bundles next to the app artifacts so the client can discover the install or update source automatically.
- The latest validation slice for this state passed on `build-windows`, `build-linux`, and `test-service`.

## Key documents

- [WorkToBeDone.md](WorkToBeDone.md)
- [Architecture.md](Architecture.md)
- [FunctionalitySpecification.md](FunctionalitySpecification.md)
- [SubZeroFramework.Service/README.md](SubZeroFramework.Service/README.md)
- [SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md](SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md)
- [SubZeroFramework/Docs/FanSafetyShutdownChecklist.md](SubZeroFramework/Docs/FanSafetyShutdownChecklist.md)
