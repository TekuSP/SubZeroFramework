# Architecture

## Purpose

This document defines the runtime architecture of SubZeroFramework.

Use `WorkToBeDone.md` as the execution roadmap and priority list.

Use `FunctionalitySpecification.md` as the source of truth for intended menu-item and page behavior.

Use this document as the source of truth for the service/client split, privilege boundary, IPC ownership, process lifecycle, and multi-instance assumptions.

## High-level runtime model

SubZeroFramework is split into two major processes:

- a long-lived local background service
- a user-facing client application

The background service owns privileged Framework EC access and local hardware polling.

The client application owns UI, user interaction, local presentation state, and subscriptions to service-exposed status and telemetry streams.

The boundary between them is local-only IPC over gRPC.

## Privilege boundary

### Service privileges

The service is the only process that should talk directly to FrameworkDotnet, the EC connection, and other privileged hardware access paths.

Platform intent is:

- on Linux, the service runs under systemd as root because Framework EC access requires elevated privileges
- on Windows, the service runs as a Windows service in an elevated service context with the rights needed for EC access

The service is independent from any interactive user session.

### Client privileges

The client application runs as the normal interactive user.

The user can start and stop the client whenever they want without changing the intended lifetime of the service.

The client must not assume it has direct permission to access the EC or to bypass the service boundary.

## Ownership rules

### Service responsibilities

The service owns:

- Framework EC connection lifecycle
- telemetry polling and retained snapshots
- hardware inventory collection and fallback mapping
- fan-control command execution
- authorization checks for mutating fan-control commands
- service self-registration, update, removal, and autorun changes when launched in service-management mode
- restore-to-auto safety behavior during controlled shutdown paths
- the shared source of truth for current telemetry, fan state, command state, and fan safety state such as active overrides and restore failures

### Client responsibilities

The client owns:

- page composition and navigation
- view models and local DynamicData projections for presentation
- subscribing to status, telemetry, and fan-control state streams from the service
- rendering charts, cards, legends, and selection state
- initiating user-requested commands through the service boundary
- discovering packaged service-bundle readiness and surfacing lifecycle guidance or quick actions without becoming the privileged worker

The client should treat the service as the authority for runtime hardware state.

## IPC model

SubZeroFramework uses local-only gRPC over a Unix domain socket path on both Windows and Linux.

The service publishes:

- system status
- telemetry channels and current values
- telemetry history streams
- fan capability and fan-control state streams, including authoritative override and restore-failure state
- explicit fan command RPCs
- hardware inventory snapshots

The client consumes these through typed client abstractions rather than through direct provider access.

## Service management path

Install, update, uninstall, shutdown, restart, and autorun changes are intentionally not normal gRPC operations.

Those operations must still work when the service is offline, not yet installed, or in the middle of stopping itself, and the client must remain unelevated.

The current path is:

- publish a packaged service bundle under `service-package/windows` or `service-package/linux`
- let the client discover that bundle and launch the packaged service executable with `--service-management <operation>`
- use the OS privilege prompt on the service executable rather than on the Uno client process
- let the service executable perform the underlying `sc.exe`, `net.exe`, `systemctl`, and file-installation work on its own behalf

This keeps lifecycle control local and explicit without weakening the service or client privilege boundary.

## Lifecycle model

### Service lifecycle

The service is intended to be long-lived and OS-managed.

It may start before any UI instance exists, and it should continue running after all UI instances are closed.

The service should remain the single owner of hardware polling and command execution throughout its lifetime.

### Client lifecycle

The client is user-driven and disposable.

Users may:

- launch the client when they want
- close it when they want
- relaunch it later
- potentially run multiple client instances at the same time

Closing the client should not stop the service.

Closing the client should not directly change EC control state unless the client explicitly requested a command before closing.

### Service restart expectations

Client instances should tolerate service restarts.

When the service restarts, clients should reconnect and rebuild their local projections from service streams instead of assuming in-process persistence.

### Lifecycle command expectations

Lifecycle requests that affect service registration or availability should be treated as out-of-band local management actions.

The client may surface those actions in Settings or Warnings and Issues, but the actual privileged work should continue to happen through the packaged service executable and the platform service manager rather than through a long-lived mutating gRPC channel.

## Multi-instance client model

Multiple UI instances should be treated as a realistic edge case, not an impossible one.

### Read-only behavior

For read-only status and telemetry:

- multiple client instances may subscribe at the same time
- each client instance may build its own local DynamicData caches from the service streams
- this is acceptable as long as each individual client instance still shares its own subscriptions internally and does not create unnecessary duplicate gRPC streams per view model

DynamicData is a good fit for this because each client instance can keep local observable caches while the service remains the authoritative publisher.

### Command behavior

For mutating fan-control commands:

- the service remains the only writer to hardware
- client pages must not assume exclusive ownership of fan control
- current control mode and fan-control state should always be taken from the service, not from local optimistic UI state alone
- active override and restore-failure indicators should also be taken from the service-published fan-control state stream
- custom-curve and manual-control features must assume another client instance could have changed state

This means command UI should stay conservative and should refresh from service state after commands are applied.

### Open design question

Concurrent command ownership across multiple client instances is not fully defined yet.

Until a stronger ownership or leasing model exists, the service should remain conservative and authoritative, and UI command flows should assume last-writer-wins behavior unless the service adds stricter coordination.

## DynamicData and stream-sharing expectations

DynamicData patterns should be preserved at both layers:

- the service should expose shared, reusable observables and change-set streams rather than per-consumer polling loops
- each client instance should share subscriptions internally so multiple view models do not create duplicate service streams
- UI collections should preserve stable item identity using persistent mutable card or row models exposed through `ReadOnlyObservableCollection<T>`

This is important both for performance and for correctness when more than one UI instance is running.

## Fan safety and shutdown expectations

The service is responsible for restoring automatic fan control on controlled shutdown paths.

That includes:

- service stop requests
- host application stopping events
- controlled polling shutdown paths
- service disposal paths when process state still allows restore

The service should avoid duplicate restore attempts and should target fans with explicit active overrides instead of blindly restoring every available fan.

The client should not own restore-to-auto behavior for process shutdown.

## Architecture consequences for future work

When implementing new features:

- keep privileged hardware access in the service
- keep the client unprivileged and replace direct provider usage with typed IPC clients
- keep install, update, shutdown, restart, and uninstall flows out of the normal gRPC command surface
- assume multiple UI instances can exist at once
- design read-only pages to tolerate concurrent listeners
- design command pages so service state is authoritative after every mutation
- surface fan safety state from service-owned streams rather than inventing local UI-only fan override state
- preserve reconnect behavior after service restarts
- prefer shared reactive streams and DynamicData projections over ad hoc polling in the UI

## Relationship to other documents

- `WorkToBeDone.md` tracks what should be done next and what is still incomplete
- `FunctionalitySpecification.md` describes what each menu item and page should do
- `SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md` describes current IPC authorization limits and UI cadence rules
- `SubZeroFramework/Docs/FanSafetyShutdownChecklist.md` captures the manual verification steps for service stop, restart, machine shutdown, and multi-instance fan-safety behavior
- `SubZeroFramework.Service/README.md` documents the packaged service bundle layout and `--service-management` lifecycle operations
