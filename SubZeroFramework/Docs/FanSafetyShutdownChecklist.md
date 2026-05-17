# Fan Safety Shutdown Checklist

## Purpose

This checklist defines the manual verification steps for fan safety during service stop, service restart, host shutdown, and full machine shutdown or reboot.

It is intended to validate the architecture described in `Architecture.md`:

- the service is the privileged owner of EC writes and restore-to-auto behavior
- the client is an unprivileged consumer of service-published state
- multiple UI instances may exist at the same time
- fan override and restore-failure state must come from the service, not from local UI-only assumptions

## Platforms and service identities

- Windows service name: `SubZeroFrameworkService`
- Linux systemd unit: `subzeroframework.service`
- When a packaged bundle is available, Settings and Warnings and Issues route stop or restart requests through the published service executable's `--service-management` mode.

## Preconditions

- Run on supported Framework hardware with a writable EC path.
- Ensure fan-control commands are explicitly enabled for the test environment.
- Ensure the service is installed and running.
- Ensure the client can connect to the service and display fan-control state.
- If testing the UI lifecycle actions, ensure a packaged service bundle is discoverable by the client.
- If possible, have service logs available during the test.
- Prefer testing with at least one visible fan that supports manual override.

## Common setup

1. Start the service.
2. Start one client instance.
3. Navigate to a fan-control-capable surface.
4. Apply a manual RPM or duty override to one fan.
5. Confirm the UI shows the service-owned override state, such as `Manual override active`.
6. Record the fan index, the chosen override, and whether the service reports any prior restore failure.

## Scenario 1: Service stop

1. Perform the common setup.
2. Stop the service through the platform service manager or through the Settings or Warnings quick action.
3. Keep the client open if possible so status transitions can be observed.

Expected results:

- the service enters its stopping path without hanging
- the service attempts restore-to-auto for the overridden fan only
- the client reflects service unavailability rather than inventing a local fallback state
- after the service is started again, the client reconnects and rebuilds state from the service
- the fan does not remain incorrectly shown as manually overridden unless restore actually failed
- if restore fails, the failure is visible through service-owned fan-control state or service logs

## Scenario 2: Service restart

1. Perform the common setup.
2. Restart the service rather than only stopping it, either through the platform service manager or through the Settings or Warnings quick action.
3. Observe the client during the disconnect and reconnect window.

Expected results:

- restore-to-auto is attempted during the stop side of the restart
- the client reconnects cleanly after the service returns
- the client rebuilds fan state from the service rather than keeping stale optimistic local state
- the fan-control mode shown after restart matches the service-published state
- if restore failed during restart, the failure remains visible from service state after reconnect

## Scenario 3: Full machine shutdown or reboot

1. Perform the common setup.
2. Request a normal OS shutdown or reboot while the override is active.
3. After boot, allow the service to start.
4. Launch the client again and inspect fan-control state.

Expected results:

- the service receives a controlled shutdown path whenever the OS and service manager permit it
- restore-to-auto is attempted before the process fully exits
- after reboot, the client does not show a stale manual or curve override unless the service reports one
- if restore failed, the failure is visible through service-owned state or service logs
- the system returns to a clean reconnect state without depending on any client-local persistence

## Scenario 4: Multi-instance sanity check

1. Start the service.
2. Start two client instances.
3. In the first client, apply a manual override to one fan.
4. In the second client, verify the same override state appears from the service stream.
5. Close only one client instance.
6. Verify the remaining client still shows the service-owned override state.
7. Stop or restart the service while the remaining client stays open.

Expected results:

- both clients observe the same override state from the service
- closing one client does not trigger restore-to-auto
- only the service stop or restart path triggers restore attempts
- reconnect behavior after service restart is consistent across clients
- no client instance behaves as if it privately owns fan control state

## What to capture for each run

- platform and OS version
- service manager action used, including whether it was triggered directly or through the UI lifecycle action
- whether the override was RPM, duty, or future custom curve
- whether one or multiple clients were open
- whether restore succeeded
- any restore-failure text shown in the UI
- any relevant service log lines

## Pass criteria

A scenario passes when all of the following are true:

- the service remains the only actor performing restore-to-auto behavior
- the client reflects service-owned state before and after shutdown or restart
- overridden fans are targeted for restore without unnecessary duplicate restore attempts
- reconnect behavior does not leave stale local override state behind
- restore failures are visible rather than silently ignored

## Follow-up when a scenario fails

- capture the exact platform and shutdown path
- capture the last visible client state before disconnect
- capture service log output if available
- note whether the failure was restore omission, duplicate restore, reconnect staleness, or state visibility mismatch
- add the result back into `WorkToBeDone.md` if it reveals a new missing shutdown or coordination behavior
