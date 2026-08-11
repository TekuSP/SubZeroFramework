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

Fan-control commands are **off by default**: the service refuses them unless `AllowFanControlCommands` has
been explicitly enabled. A default install does not change fan behaviour until someone opts in.

**Be clear about what that is and is not.** The opt-in is a safety default, **not an authorization
boundary**. It is settable over the same local socket it gates, so any local process that can reach the
socket can turn it on and then issue fan-control commands — no elevation, no user interaction. Until
caller-identity validation lands (see below), the flag protects you from *accident*, not from a *local
adversary*. Earlier revisions of this document described the gating as "fail-closed" and said a default
install "cannot" change fan behaviour; that overstated it, and the wording is corrected here rather than
left to imply a guarantee the implementation does not make.

Keeping the flag settable over the socket is a deliberate trade for 0.1.x: the in-app toggle is how users
are expected to enable fan control, and moving it to a root-only file would remove that. The residual risk
is accepted knowingly and stated here rather than assumed away.

## Known and accepted limitations in 0.1.0

Please do not file these as new findings — they are documented, deliberate decisions for the first
release, with the reasoning recorded in
[`SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md`](SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md).

- **Caller-identity validation is not enforced** (`HasCallerIdentityValidation = false`). Any local process
  able to reach the socket can issue any RPC, **including the one that enables fan control** — so the
  opt-in does not compensate for this limitation, it shares it. The mitigations actually relied on are the
  local-only machine-scoped transport and path/permission validation. On Linux the socket is world-
  connectable by design (`connect(2)` needs write permission on the socket file and the client is
  unprivileged), so on a multi-user machine this means *any* local account, not only your own.
  The app surfaces this state in its Warnings page rather than hiding it.
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
