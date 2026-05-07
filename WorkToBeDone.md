# Work To Be Done

## Service architecture
- Move fan write operations fully behind `SubZeroFramework.Service` so the UNO UI never talks to Framework EC directly.
- Design a clear separation between read-only telemetry APIs and fan-control command APIs.
- Add a service status surface that reports connection state, driver, elevation status, and last error through IPC.
- Decide whether the UNO app should fall back to in-process provider usage during development only.

## IPC
- Choose the IPC transport between the UNO UI and `SubZeroFramework.Service`.
- Add a strongly typed contract assembly for service requests, responses, snapshots, and command acknowledgements.
- Implement a request/response channel for status reads and fan commands.
- Implement a streaming/subscription channel for telemetry snapshots and state changes.
- Add reconnect behavior when the service restarts.
- Add request cancellation and timeouts.
- Add local-only endpoint restrictions.

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
- Replace direct `IFrameworkDataProvider` usage in the UNO app with an IPC client abstraction.
- Update warning and settings pages to show service installation, elevation, and connection guidance.
- Add a visible service-health indicator in the UI.
- Add user actions to open logs or copy diagnostics.

## Testing and validation
- Add tests for Linux root detection behavior.
- Add tests for service startup configuration and option binding.
- Add integration tests for IPC contracts once the transport is chosen.
- Add a manual deployment checklist for Windows and Linux.

## Packaging and deployment
- Decide publish layout for the worker service on Windows and Linux.
- Add publish profiles or scripts for the service binaries.
- Document versioning and update strategy for the service and UI.
