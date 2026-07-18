# IPC Authorization and UI Cadence Guide

This document defines the current command-authorization model for local IPC and the intended cadence for major UI surfaces.

## IPC caller validation by platform

SubZeroFramework currently uses gRPC over a Unix domain socket path on both Windows and Linux.

### What is validated today

The shared socket security layer validates the local endpoint path before client and server use it.

Current checks include:

- expected socket path matching
- expected socket directory matching
- symlink rejection
- Windows reparse-point traversal rejection
- Linux writable-by-group-or-others rejection for an existing socket file
- best-effort directory hardening

These checks protect the endpoint location and reduce local path-hijack risk.

### What is not validated today

The current Kestrel-over-UDS stack does not expose a portable, supported per-request caller identity check that can be relied on by the fan-command RPC boundary across both Windows and Linux.

That means the service cannot currently prove, in a cross-platform way, which local user or process issued a mutating fan-control command.

### Shipped posture (decided 2026-07-03, release blocker P0-3)

**The first public release ships with `HasCallerIdentityValidation = false`.** This is a deliberate,
documented decision, not an oversight. Rationale:

- The transport is local-only (Unix domain socket) with expected-path validation, symlink/reparse
  protection, permission checks, and a machine-scoped socket location — a remote attacker has no route to
  the endpoint, and local path-hijack risk is mitigated by the checks above.
- Mutating fan-control RPCs are **fail-closed**: denied unless `AllowFanControlCommands` is explicitly
  enabled in the service configuration.
- The limitation is honestly surfaced: the service reports it in its status, and the UI shows the
  "validation limited" state on the Warnings & Issues page.

Post-MVP hardening options (tracked, not scheduled): `SO_PEERCRED` on Linux; on Windows either a
named-pipe transport with client impersonation or socket-ACL ownership checks.

## Fan-command authorization policy

Because peer identity is not currently available in a portable way, fan-control RPCs are treated more strictly than read-only status and telemetry RPCs.

### Current policy

- read-only status RPCs are allowed
- read-only telemetry RPCs are allowed
- mutating fan-control RPCs are denied by default
- fan-control RPCs may be enabled only through explicit service configuration
- even when enabled, the service still reports that portable caller identity validation is unavailable on the current transport

### Current service option

`SubZeroFramework.Service/appsettings.json` now includes:

- `FrameworkService:AllowFanControlCommands`

This defaults to `false`.

### Why this is the default

Fan commands can change EC behavior and therefore require a higher trust bar than read-only telemetry.

Until the service can validate peer identity in a supported and portable way, enabling fan-control RPCs should remain an explicit operator decision, not the default behavior.

## Service lifecycle control path

Service install, update, uninstall, shutdown, restart, and autorun actions are intentionally not normal gRPC operations.

### Current policy

- the client discovers a packaged service bundle under `service-package/windows` or `service-package/linux`, or a configured override path
- the client launches the packaged service executable with `--service-management <operation>`
- Windows relies on a UAC administrator prompt for the service executable
- Linux relies on a root or `pkexec` prompt for the service executable
- the service executable performs the underlying `sc.exe`, `net.exe`, `systemctl`, and file-installation work on its own behalf

### Why this stays out of gRPC

- install and update must work even when the service is offline or not yet registered
- shutdown, restart, and uninstall may deliberately tear down the current service instance
- the client must remain unelevated and should not become the long-lived privileged worker

### UI expectations

- Settings and Warnings and Issues should surface install readiness and privilege-prompt guidance before enabling install or update actions
- action results should be surfaced distinctly from transport health, ideally through `InfoBar`-style feedback
- elevation prompt cancellation should be treated as a warning, not as a transport failure

## UI command enablement rules

The UI should not enable fan-control actions only because the service is reachable.

Fan command surfaces should require all of the following:

- `IsGrpcActive == true`
- `IsLibraryAvailable == true`
- `IsFrameworkDevice == true`
- `RequiresElevation == false`
- `IsConnectionOpen == true`
- `IsFanControlEnabled == true`

The UI should also display `FanControlAuthorizationMessage` when commands are disabled or when caller validation is limited.

### Recommended UI behavior

- if fan control is disabled by service configuration, show the authorization message and keep commands disabled
- if fan control is enabled but caller validation is unavailable, show a stronger warning before any command UI is made interactive
- do not silently fall back to direct in-process EC writes from the UNO app

## UI cadence rules

Not every surface should react to every polling sample.

### Transition-driven surfaces

These surfaces should react to meaningful state changes, not the full telemetry cadence:

- header service-health and error banner
- warning/issues summary state
- settings diagnostics state
- any command enablement or authorization banner

These surfaces are primarily driven by `FrameworkSystemStatus` transitions.

### Live telemetry-driven surfaces

These surfaces should react to current value updates or time-series updates:

- thermal current readings
- power current readings
- telemetry cards and lists
- time-series charts
- fan capability and reporting surfaces when tied to current hardware state

These surfaces should use the telemetry clients and retained series streams directly.

### Heartbeat handling

Heartbeat or last-updated text should stay separate from transport health and error semantics.

- `IsGrpcActive` means the IPC transport is reachable
- `LastTelemetryObservedAt` means the backend has seen telemetry samples
- a stale heartbeat should not automatically be treated as the same condition as transport failure

## Practical next step

The next security upgrade should be one of these:

1. move mutating commands to a transport with stronger built-in local peer identity semantics on every supported platform, or
2. add a supported custom transport or listener layer that can surface peer identity to ASP.NET Core in a maintainable way

Until then, the default stance should remain:

- allow read-only IPC
- deny mutating IPC by default
- require explicit operator opt-in for fan-control RPCs
