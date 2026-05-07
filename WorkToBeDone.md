# Work To Be Done

## Service architecture
- Move fan write operations fully behind `SubZeroFramework.Service` so the UNO UI never talks to Framework EC directly.
- Design a clear separation between read-only telemetry APIs and fan-control command APIs.
- Add a service status surface that reports connection state, driver, elevation status, and last error through IPC. Done for read-only status streaming via gRPC, including `IsGrpcActive` transport state.
- Decide whether the UNO app should fall back to in-process provider usage during development only.

## IPC
- Use gRPC over Unix domain sockets as the initial IPC transport baseline for Windows 10/11 and Linux.
- Add a strongly typed contract assembly for service requests, responses, snapshots, and command acknowledgements. Done for the current status and telemetry streaming contracts in `SubZeroFramework.GrpcContracts`. Fan-command acknowledgements still pending.
- Implement a request/response channel for status reads and fan commands. Done for status reads. Fan commands still pending.
- Implement a streaming/subscription channel for telemetry snapshots and state changes. Done for service status streaming and telemetry channel/current-value/series scaffolding. Telemetry UI consumption and snapshot-specific surfaces still pending.
- Integrate Rx-friendly adapters over gRPC streams with explicit buffering, throttling, and backpressure strategy for UI subscriptions. Done for status streaming with bounded channel buffering and shared status stream reuse. Telemetry shared-stream consolidation and snapshot throttling still pending.
- Add reconnect behavior when the service restarts. Done for the status client and telemetry streaming scaffolding.
- Add request cancellation and timeouts. Done for unary status reads. Long-lived streams intentionally do not use the unary timeout.
- Add local-only endpoint restrictions. Partially done through Unix domain socket-only local IPC.
- Add server-side caller validation and client-side server validation for the Unix domain socket transport. Partially done through expected socket path validation and socket-path hardening. Peer identity/permission validation still pending.

## Windows service operations
- Add an installation script for `sc.exe create` and service recovery configuration.
- Configure automatic restart on service failure.
- Validate service account requirements for EC access on Windows.
- Investigate whether specific shutdown ordering or preshutdown handling is needed before Windows update restart.
- Add logging guidance for Event Log or file logging in production.

## Linux service operations
- Publish and validate the systemd unit in a real Linux environment.
- Verify root execution and EC access end-to-end under systemd.
- Add an install script that copies the service binary and unit file, reloads systemd, and enables the unit.
- Add journald logging guidance.
- Consider hardening options in the systemd unit that do not break EC access.

## Fan safety
- Add explicit tracking for whether manual fan control is currently active.
- Ensure automatic fan control is restored on all controlled stop paths before exit.
- Attempt automatic fan control restore from global exception and unhandled termination paths when process state still allows native calls.
- Add protection against duplicate restore attempts during shutdown.
- Define behavior when restore-to-auto fails.
- Add tests or manual verification steps for service stop, restart, and machine shutdown scenarios.
- Consider a watchdog or heartbeat model if fan safety requires external recovery guarantees.

## UI integration
- Replace direct `IFrameworkDataProvider` usage in the UNO app with an IPC client abstraction. Done for service status reads and status streaming.
- Update warning and settings pages to show service installation, elevation, and connection guidance.
- Add a visible service-health indicator in the UI. Partially done through header and warning page status handling.
- Decide which UI surfaces should react only to state transitions versus live telemetry cadence. Header currently receives only distinct system-status changes, not every polling sample.
- Add user actions to open logs or copy diagnostics.

## Testing and validation
- Add tests for Linux root detection behavior.
- Add tests for service startup configuration and option binding.
- Add integration tests for IPC contracts once the transport is chosen. Status IPC coverage is still the first priority, followed by telemetry stream contract coverage.
- Add a manual deployment checklist for Windows and Linux.

## Near-term next work
- Add local caller validation for the gRPC socket before introducing fan-control commands.
- Add stronger client-side validation of expected local service endpoint ownership and permissions where the platform allows it.
- Add telemetry client sharing/consolidation for channels and current values similar to the shared status stream.
- Wire one telemetry surface end-to-end from `IFrameworkTelemetryClient` using the existing 1-hour DynamicData-backed history model.
- Decide whether header/service-health UI needs a heartbeat or last-updated indicator separate from distinct status transitions.
- Start the command contract for fan-control writes with explicit acknowledgements and validation boundaries.
- Add integration tests for status reconnect behavior and telemetry stream startup semantics.

## Packaging and deployment
- Decide publish layout for the worker service on Windows and Linux.
- Add publish profiles or scripts for the service binaries.
- Document versioning and update strategy for the service and UI.
