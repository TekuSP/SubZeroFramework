# Work To Be Done

Use this document as the execution roadmap and status tracker for the repo.

Use `FunctionalitySpecification.md` as the source of truth for intended menu-item and page behavior when deciding what each UI surface should do.

## Status legend

- ✅ Done
- 🟡 Partial / in progress
- ⏳ Pending

## Service architecture

- ✅ Move fan write operations fully behind `SubZeroFramework.Service` so the UNO UI never talks to Framework EC directly.
- ✅ Design a clear separation between read-only telemetry APIs and fan-control command APIs.
- ✅ Add a service status surface that reports connection state, driver, elevation status, and last error through IPC, including `IsGrpcActive` transport state.
- ✅ Keep the UNO app on the service boundary and do not fall back to in-process provider usage in normal UI flows.

## IPC

- ✅ Use gRPC over Unix domain sockets as the initial IPC transport baseline for Windows 10/11 and Linux.
- ✅ Add a strongly typed contract assembly for service requests, responses, snapshots, and command acknowledgements in `SubZeroFramework.GrpcContracts`.
- ✅ Implement a request/response channel for status reads and fan commands.
- ✅ Add a service-owned configuration IPC surface for runtime settings such as polling cadence and fan-command authorization. Settings now reads, watches, updates, and persists those values through typed gRPC contracts with explicit apply acknowledgements.
- 🟡 Implement a streaming/subscription channel for telemetry snapshots and state changes. Service status streaming and telemetry channel/current-value/series streaming are done; the remaining work is end-to-end telemetry UI consumption and any additional snapshot-specific IPC surfaces you still want.
- 🟡 Integrate Rx-friendly adapters over gRPC streams with explicit buffering, throttling, and backpressure strategy for UI subscriptions. Shared status stream reuse, telemetry sharing, non-dropping batched delivery, and bounded overload handling are done; the remaining work is any explicit throttling policy and broader integration coverage.
- ✅ Add reconnect behavior when the service restarts for status and telemetry streams.
- ✅ Add request cancellation and timeouts. Unary status and fan-command calls use explicit timeout behavior; long-lived streams use cancellation and intentionally do not use unary-style timeouts.
- ✅ Add local-only endpoint restrictions. Unix domain socket-only local IPC, expected path validation, symlink/reparse protection, Linux permission checks, and the safer Windows machine-scoped socket location are in place.
- 🟡 Add server-side caller validation and client-side server validation for the Unix domain socket transport. Endpoint/path validation, socket hardening, and fail-closed fan-command authorization gating are in place; true peer identity / caller identity validation still needs work.

## Windows service operations

- ✅ Add a packaged self-management install and update path for Windows service registration and recovery configuration. The published service executable now supports `--service-management install|update|...`, and install or update configures the SCM entry plus restart-on-failure behavior.
- ✅ Configure automatic restart on service failure during Windows install and update flows.
- ⏳ Validate service account requirements for EC access on Windows.
- ⏳ Investigate whether specific shutdown ordering or preshutdown handling is needed before Windows update restart.
- ⏳ Add logging guidance for Event Log or file logging in production.

## Linux service operations

- 🟡 Publish and stage the systemd unit in the packaged service bundle. CI now emits `service-package/linux`, and install or update copies the bundle into `/usr/local/lib/subzeroframework`, refreshes the unit, and starts the service; real Linux validation is still pending.
- ⏳ Verify root execution and EC access end-to-end under systemd.
- 🟡 Add an install and update path that copies the service binary and unit file, reloads systemd, and starts the unit. Explicit autorun remains a separate action.
- ⏳ Add journald logging guidance.
- ⏳ Consider hardening options in the systemd unit that do not break EC access.

## Fan safety

- ✅ Add explicit tracking for whether manual fan control is currently active.
- 🟡 Ensure automatic fan control is restored on all controlled stop paths before exit. Service stop requests, host stopping, controlled polling shutdown, and disposal now attempt targeted restore for active overrides, but broader verification and edge-case coverage still need work.
- ⏳ Attempt automatic fan control restore from global exception and unhandled termination paths when process state still allows native calls.
- ✅ Add protection against duplicate restore attempts during shutdown.
- 🟡 Define behavior when restore-to-auto fails. Failure state is now tracked and surfaced through fan-control state, but the broader operator-facing policy is still incomplete.
- ⏳ Define service-side coordination semantics when multiple UI instances issue concurrent fan-control commands.
- ✅ Add tests or manual verification steps for service stop, restart, and machine shutdown scenarios. Worker coverage now verifies start, service stop, and host-stopping paths, and `SubZeroFramework/Docs/FanSafetyShutdownChecklist.md` captures the manual restart, shutdown, and multi-instance verification steps.
- ⏳ Consider a watchdog or heartbeat model if fan safety requires external recovery guarantees.

## UI integration

- ✅ Replace direct `IFrameworkDataProvider` usage in the UNO app with typed IPC client abstractions. Current UI surfaces now stay on the service-backed status, telemetry, inventory, and lifecycle client path.
- ✅ Update warning and settings pages to show service installation, update, elevation, and connection guidance using WinUI or Uno `InfoBar` surfaces for readiness, privilege prompts, and action results.
- ✅ Move service-owned polling/auth configuration into the Settings page through the new gRPC configuration surface instead of treating those values as client-local preferences.
- 🟡 Add a visible service-health indicator in the UI. Header, settings, and warning/status handling are now in place; logs and deeper diagnostics actions still need work.
- ✅ Decide which UI surfaces should react only to state transitions versus live telemetry cadence. Header receives distinct system-status changes, with heartbeat/last-observed data kept separate.
- ⏳ Add user actions to open logs or copy diagnostics.

## Recent completed UI and inventory work

- ✅ Align Device Capabilities with the Dashboard visual language and stabilize refresh behavior using persistent card/view-model collections instead of rebinding fresh item arrays.
- ✅ Prefer Framework-provided runtime/device data when available, then fill remaining inventory gaps with Hardware.Info through the service, gRPC contract, and IPC client path.
- ✅ Expand Device Capabilities with cleaned-up device identity, EC version/build, BIOS release date, runtime sensor/fan/battery status cards, drive-level storage usage summaries, and detected network adapter inventory.
- ✅ Refactor Device Capabilities CPU and graphics inventory into modular package and graphics-card group controls: each CPU package now owns recent average-usage and frequency charts plus per-core usage cards, and monitors are grouped under detected graphics cards with an explicit Unknown graphics card bucket for unlinked displays.
- ✅ Keep the new CPU usage visuals scoped to the service-backed Hardware.Info snapshot/history path inside Device Capabilities; a separate top-level CPU telemetry/dashboard surface is still out of scope without another revalidation pass.
- ✅ Fix Power Telemetry battery health calculation, current unit/display consistency, and the `DesignCapacityAmpereHours` naming typo.
- ✅ Complete a Controls/MVVM cleanup pass: replace in-repo `SetProperty` wrappers with `[ObservableProperty]` public partial properties under new analyzer `SZF0012`, centralize recent-chart labels and separator-step defaults in `PresentationDefaults`, and replace placeholder `SZF0009` suppression justifications in lightweight UserControl code-behinds with real rationale.
- ✅ Keep Dashboard fan and thermal gauge rings visually stable by disabling hover pushout and hover highlighting.

## Recent completed service lifecycle and packaging work

- ✅ Add shutdown coordination hooks across host-stopping, process-exit, unhandled-exception, and unobserved-task paths so the service can attempt orderly fan restore and emit lifecycle logs during controlled teardown.
- ✅ Add broader structured logging around lifecycle boundaries, fan commands, publish points, and authorization rejections using DI-backed logging.
- ✅ Add client-facing lifecycle actions in Settings and Warnings and Issues for shutdown, restart, autorun, install, update, reinstall, and uninstall.
- ✅ Add service-owned configuration management for polling cadence and fan-command authorization through gRPC-backed read/watch/update flow, persistent overlay storage, and live apply in the service.
- ✅ Expand Settings to match the newer remediation/lifecycle UX with autorun state, reinstall, service identity/readiness guidance, and editable service-owned runtime configuration.
- ✅ Keep install and update out of the privileged client path by launching the packaged service executable elevated with `--service-management` instead of relying on a permanently elevated UI or self-destructive gRPC operations.
- ✅ Package the service alongside CI app artifacts under `service-package/windows` and `service-package/linux` so install and update have a deterministic source bundle.
- ✅ Validate the current integrated state with `build-windows`, `build-linux`, and `test-service`.

## Testing and validation

- ✅ Add tests for Linux root detection behavior.
- 🟡 Add tests for service startup configuration and option binding. Fan-command authorization/service-option behavior, autorun parsing, and the service configuration store/manager live-apply flows are now covered, but broader startup/config binding coverage is still pending.
- 🟡 Add integration tests for IPC contracts. IPC validation failure coverage now exists through `FrameworkGrpcSocketSecurityTests`, but status reconnect coverage and telemetry stream contract coverage are still needed.
- ⏳ Add a manual deployment checklist for Windows and Linux.

## Near-term next work

- ⏳ Add local caller validation for the gRPC socket before fan commands are enabled for broader use.
- ⏳ Add stronger client-side validation of expected local service endpoint ownership and permissions where the platform allows it.
- ✅ Add telemetry client sharing/consolidation for channels and current values similar to the shared status stream.
- ✅ Wire the first dedicated telemetry page end-to-end from the existing IPC telemetry clients using the DynamicData-backed history model. Thermal Telemetry now shows stable current sensor cards, sensor-selection toggles, and a dedicated multi-series history chart without reintroducing direct `IFrameworkDataProvider` usage.
- ✅ Decide whether header/service-health UI needs a heartbeat or last-updated indicator separate from distinct status transitions.
- ✅ Start the command contract for fan-control writes with explicit acknowledgements and validation boundaries.
- 🟡 Add integration tests for status reconnect behavior and telemetry stream startup semantics. Current regression coverage now includes endpoint validation failures and fan-command authorization gating, but reconnect and long-lived stream integration tests are still pending.

## Next execution breakdown

### 1. IPC hardening

- ✅ Validate Unix socket file ownership and permissions on platforms that expose them.
- ✅ Reject unexpected socket targets such as symlinks or mismatched resolved paths.
- ✅ Decide and document what caller validation is possible on Windows versus Linux in `SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md`.
- ✅ Add server-side checks before fan-command RPCs are introduced. Command validation and fail-closed service-side authorization gating now exist; portable caller identity validation still remains a separate hardening item.

### 2. Telemetry client sharing

- ✅ Share channel and current-value streams similarly to the shared status stream.
- ✅ Avoid one independent gRPC stream per consuming view-model where possible.
- ✅ Decide whether telemetry series streams should be shared by `(channelId, historyWindow)` key.
- ✅ Add disposal rules so shared streams stop cleanly when no subscribers remain.

### 3. First end-to-end telemetry surface

- ✅ Pick the first dedicated telemetry page to wire next. Thermal Telemetry is now the implemented first dedicated telemetry surface.
- ✅ Bind current values from the IPC telemetry clients.
- ✅ Bind one chart series using the existing DynamicData-backed history model.
- ✅ Keep the page behind IPC only; do not reintroduce direct `IFrameworkDataProvider` usage in the UI.

### 4. Header heartbeat decision

- ✅ Decide whether the header should react only to distinct state transitions.
- ✅ Decide whether the header also needs a last-updated timestamp or heartbeat indicator.
- ✅ If heartbeat is needed, keep it separate from error/status semantics such as `IsGrpcActive`.
- ✅ Document which UI surfaces consume state transitions versus live telemetry cadence in `SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md`.

### 5. Fan-command RPC boundary

- ✅ Define command request and acknowledgement contracts in `SubZeroFramework.GrpcContracts`.
- ✅ Separate read-only telemetry/status APIs from mutating fan-control APIs.
- ✅ Validate commands on the server before any EC write path is exposed.
- ✅ Define authorization and safety checks before enabling command UI. Status now surfaces `IsFanControlEnabled`, `HasCallerIdentityValidation`, and `FanControlAuthorizationMessage`, and fan-control RPCs are denied by default unless explicitly enabled by service configuration.

### 6. Integration and regression coverage

- ⏳ Add tests for status reconnect behavior after service restart.
- ⏳ Add tests for long-lived stream startup and cancellation behavior.
- ⏳ Add tests for telemetry stream contract parsing and history-window validation.
- ✅ Add tests for IPC validation failures so unauthorized or invalid endpoints are rejected cleanly.

## Packaging and deployment

- ✅ Decide publish layout for the worker service on Windows and Linux. Published app artifacts now include `service-package/windows` or `service-package/linux` next to the client binaries.
- ✅ Add publish scripts for the service binaries in `SubZeroFramework.Service/Scripts` and invoke them from CI.
- 🟡 Document versioning and update strategy for the service and UI. The update command path and packaged bundle discovery are in place, but release/version presentation still needs work.
