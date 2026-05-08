# Work To Be Done

## Status legend
- ✅ Done
- 🟡 Partial / in progress
- ⏳ Pending

## Service architecture
- ✅ Move fan write operations fully behind `SubZeroFramework.Service` so the UNO UI never talks to Framework EC directly.
- ✅ Design a clear separation between read-only telemetry APIs and fan-control command APIs.
- ✅ Add a service status surface that reports connection state, driver, elevation status, and last error through IPC, including `IsGrpcActive` transport state.
- ⏳ Decide whether the UNO app should fall back to in-process provider usage during development only.

## IPC
- ✅ Use gRPC over Unix domain sockets as the initial IPC transport baseline for Windows 10/11 and Linux.
- ✅ Add a strongly typed contract assembly for service requests, responses, snapshots, and command acknowledgements in `SubZeroFramework.GrpcContracts`.
- ✅ Implement a request/response channel for status reads and fan commands.
- 🟡 Implement a streaming/subscription channel for telemetry snapshots and state changes. Service status streaming and telemetry channel/current-value/series streaming are done; telemetry UI consumption and snapshot-specific surfaces still need completion.
- 🟡 Integrate Rx-friendly adapters over gRPC streams with explicit buffering, throttling, and backpressure strategy for UI subscriptions. Shared status stream reuse, telemetry sharing, and non-blocking bounded overload handling are in place; snapshot throttling and broader coverage still need work.
- ✅ Add reconnect behavior when the service restarts for status and telemetry streams.
- 🟡 Add request cancellation and timeouts. Unary status reads use explicit timeout behavior; long-lived streams use cancellation but intentionally do not use the unary timeout.
- 🟡 Add local-only endpoint restrictions. Unix domain socket-only local IPC and endpoint path validation are in place, but stronger peer validation is still pending.
- 🟡 Add server-side caller validation and client-side server validation for the Unix domain socket transport. Expected socket path validation, symlink/reparse protection, and socket-path hardening are in place; peer identity/permission validation still needs work.

## Windows service operations
- ⏳ Add an installation script for `sc.exe create` and service recovery configuration.
- ⏳ Configure automatic restart on service failure.
- ⏳ Validate service account requirements for EC access on Windows.
- ⏳ Investigate whether specific shutdown ordering or preshutdown handling is needed before Windows update restart.
- ⏳ Add logging guidance for Event Log or file logging in production.

## Linux service operations
- ⏳ Publish and validate the systemd unit in a real Linux environment.
- ⏳ Verify root execution and EC access end-to-end under systemd.
- ⏳ Add an install script that copies the service binary and unit file, reloads systemd, and enables the unit.
- ⏳ Add journald logging guidance.
- ⏳ Consider hardening options in the systemd unit that do not break EC access.

## Fan safety
- ⏳ Add explicit tracking for whether manual fan control is currently active.
- 🟡 Ensure automatic fan control is restored on all controlled stop paths before exit. Controlled shutdown restore exists, but broader verification and edge-case coverage still need work.
- ⏳ Attempt automatic fan control restore from global exception and unhandled termination paths when process state still allows native calls.
- ⏳ Add protection against duplicate restore attempts during shutdown.
- ⏳ Define behavior when restore-to-auto fails.
- ⏳ Add tests or manual verification steps for service stop, restart, and machine shutdown scenarios.
- ⏳ Consider a watchdog or heartbeat model if fan safety requires external recovery guarantees.

## UI integration
- 🟡 Replace direct `IFrameworkDataProvider` usage in the UNO app with an IPC client abstraction. Status-side integration is done; telemetry surfaces should continue moving to IPC-only usage.
- 🟡 Update warning and settings pages to show service installation, elevation, and connection guidance. Diagnostics and endpoint validation messaging are present, but the full guidance experience still needs work.
- 🟡 Add a visible service-health indicator in the UI. Header, settings, and warning/status handling are partially in place.
- ✅ Decide which UI surfaces should react only to state transitions versus live telemetry cadence. Header receives distinct system-status changes, with heartbeat/last-observed data kept separate.
- ⏳ Add user actions to open logs or copy diagnostics.

## Testing and validation
- ✅ Add tests for Linux root detection behavior.
- ⏳ Add tests for service startup configuration and option binding.
- ⏳ Add integration tests for IPC contracts. Status reconnect coverage, telemetry stream contract coverage, and IPC validation failure coverage are still needed.
- ⏳ Add a manual deployment checklist for Windows and Linux.

## Near-term next work
- ⏳ Add local caller validation for the gRPC socket before fan commands are enabled for broader use.
- ⏳ Add stronger client-side validation of expected local service endpoint ownership and permissions where the platform allows it.
- ✅ Add telemetry client sharing/consolidation for channels and current values similar to the shared status stream.
- ⏳ Wire one telemetry surface end-to-end from `IFrameworkTelemetryClient` using the existing 1-hour DynamicData-backed history model.
- ✅ Decide whether header/service-health UI needs a heartbeat or last-updated indicator separate from distinct status transitions.
- ✅ Start the command contract for fan-control writes with explicit acknowledgements and validation boundaries.
- ⏳ Add integration tests for status reconnect behavior and telemetry stream startup semantics.

## Next execution breakdown

### 1. IPC hardening
- ⏳ Validate Unix socket file ownership and permissions on platforms that expose them.
- ✅ Reject unexpected socket targets such as symlinks or mismatched resolved paths.
- ✅ Decide and document what caller validation is possible on Windows versus Linux in `SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md`.
- ✅ Add server-side checks before fan-command RPCs are introduced. Command validation and fail-closed service-side authorization gating now exist; portable caller identity validation still remains a separate hardening item.

### 2. Telemetry client sharing
- ✅ Share channel and current-value streams similarly to the shared status stream.
- ✅ Avoid one independent gRPC stream per consuming view-model where possible.
- ✅ Decide whether telemetry series streams should be shared by `(channelId, historyWindow)` key.
- ✅ Add disposal rules so shared streams stop cleanly when no subscribers remain.

### 3. First end-to-end telemetry surface
- ⏳ Pick the first telemetry page to wire, preferably thermal telemetry.
- ⏳ Bind current values from `IFrameworkTelemetryClient`.
- ⏳ Bind one chart series using the existing 1-hour DynamicData-backed history model.
- ⏳ Keep the page behind IPC only; do not reintroduce direct `IFrameworkDataProvider` usage in the UI.

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
- ⏳ Add tests for IPC validation failures so unauthorized or invalid endpoints are rejected cleanly.

## Packaging and deployment
- ⏳ Decide publish layout for the worker service on Windows and Linux.
- ⏳ Add publish profiles or scripts for the service binaries.
- ⏳ Document versioning and update strategy for the service and UI.
