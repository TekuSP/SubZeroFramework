# Security Policy

## Reporting a vulnerability

Please report security issues privately rather than opening a public issue.

Use GitHub's [private vulnerability reporting](https://github.com/TekuSP/SubZeroFramework/security/advisories/new)
for this repository. If that is unavailable to you, open an issue containing only a request for a private
contact channel — no details.

This is a small, volunteer-maintained project. There is no formal SLA, but reports are taken seriously and
you can expect an acknowledgement.

## Threat model in brief

SubZero has two components with very different privilege levels:

- The **background service** runs as LocalSystem (Windows) or root (Linux). It is the only component with
  embedded-controller access, and it is the security boundary that matters.
- The **desktop app** runs unprivileged and holds no hardware access of its own. It is a client.

They speak gRPC over a **local-only** transport — a Unix domain socket on Linux, in a machine-scoped
location, with expected-path validation, symlink/reparse protection, and permission checks. **No network
listener is opened**, and nothing is transmitted off the machine.

Fan-control commands are **fail-closed**: the service refuses them unless `AllowFanControlCommands` has
been explicitly enabled in its configuration. A default install cannot change fan behaviour until the user
opts in.

## Known and accepted limitations in 0.1.0

Please do not file these as new findings — they are documented, deliberate decisions for the first
release, with the reasoning recorded in
[`SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md`](SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md).

- **Caller-identity validation is not enforced** (`HasCallerIdentityValidation = false`). Any local process
  able to reach the socket can issue RPCs subject to the fail-closed gating above. The mitigations relied
  on are the local-only machine-scoped transport, path/permission validation, and the explicit
  fan-control opt-in. The app surfaces this state in its Warnings page rather than hiding it.
  Post-MVP hardening options under consideration: `SO_PEERCRED` on Linux; on Windows either a named-pipe
  transport with client impersonation, or socket-ACL ownership checks.
- **Release binaries are not code-signed.** Windows SmartScreen will warn on the installer. Verify what you
  download, or build from source.

New findings *about* these areas — for example a concrete way to defeat the path validation, or to get a
fan-control command executed without the opt-in — are very much in scope and worth reporting.

## Scope

In scope: privilege escalation via the service, bypassing the fan-control opt-in, anything that lets a
remote or unprivileged actor reach the EC, and unsafe hardware states the software can be made to produce.

Out of scope: issues requiring an attacker who already has Administrator/root, and vulnerabilities in
third-party dependencies that we merely consume — please report those upstream, though a heads-up here is
appreciated so the dependency can be bumped.
