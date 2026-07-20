# SubZero Framework Edition

A companion app for [Framework](https://frame.work) laptops: live thermal, power and fan telemetry, and
per-fan control with custom curves.

It ships as two pieces. A **background service** owns privileged embedded-controller (EC) access —
hardware polling and fan writes — and an **unprivileged desktop app** talks to it over a local-only
socket. Nothing leaves your machine; there is no telemetry, no account, and no network service.

> **0.1.0 is the first public release.** It is usable day to day, but see
> [Known limitations](#known-limitations-in-010) before you install — some surfaces are deliberately
> switched off in this release.

## Requirements

- A **Framework laptop**. Detected chassis families: Framework 12, 13, 13 Pro, 16, and Framework Desktop.
  Without Framework EC hardware the app still starts, but it will sit on its recovery screen.
- **Windows** (x64 or ARM64) or **Linux** (x64 or ARM64, systemd-based).
- **Administrator / root** to *install*, because registering the background service requires it. Everyday
  use does not — the app itself runs unprivileged.
- **.NET 10** — built on it, and both the app and the service ship **self-contained**, so the runtime is
  bundled and you do **not** need to install .NET separately. (Building from source does need the .NET 10
  SDK.)

## Install

### Windows

Download the installer for your architecture from the
[Releases](https://github.com/TekuSP/SubZeroFramework/releases) page and run it. It installs the app,
registers the background service, and starts it.

> **The installer is not code-signed yet.** Windows SmartScreen will warn you. To continue, choose
> **More info → Run anyway**. If you would rather not, you can
> [build from source](#building-from-source) instead.

### Linux

`.deb`, `.rpm`, a tarball, and an AUR package are produced for x64 and ARM64. Installing the service
package enables and starts the systemd unit.

The UI package depends on the service package at an exact version, so install both together:

```
sudo apt install ./subzeroframework-service_*.deb ./subzeroframework_*.deb
```

If the service was installed by your distro's package manager, the app defers to it — install, update and
uninstall stay with the package manager, while start/stop/restart remain available in the app.

## First run

**Fan control is off by default.** The service ships with `AllowFanControlCommands: false`, so on a fresh
install the app will show telemetry but refuse to change fan behaviour. This is deliberate — writing fan
duty to the EC is the one thing here that can affect your hardware, so it is opt-in rather than
opt-out.

To enable it: **Settings → Service → runtime configuration**, turn on fan-control commands, and apply.
The app tells you when a command was refused for this reason.

Fan control itself lives on the **Fan Curve Profiles** page — Auto, Manual, Max, and Custom curves, with
staged changes you preview before applying.

## Known limitations in 0.1.0

These are intentional for the first release, not bugs:

- **Modules** — the tab is disabled. It depends on EC slot reporting that is not complete yet.
- **Cooling profile presets** on the Dashboard render inert and read *Coming soon*.
- **The Dashboard is read-only.** All fan control is on the Fan Curve Profiles page, on purpose — one
  surface owns actuation.
- **Installers are unsigned** (see above).
- **Caller-identity validation is not enforced** on the local IPC socket. The transport is a local-only
  socket with path, permission and symlink checks, and fan-control commands are refused unless you
  explicitly enable them. The shipped posture is documented in
  [IpcAuthorizationAndUiCadence.md](SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md).

## Building from source

Requires the .NET 10 SDK.

```
dotnet build SubZeroFramework/SubZeroFramework.csproj -f net10.0-windows10.0.26100 -c Release
dotnet test  SubZeroFramework.Tests/SubZeroFramework.Tests.csproj
```

The app builds for two target frameworks: `net10.0-windows10.0.26100` (WinUI) and `net10.0-desktop`
(Skia). The service is a plain .NET worker.

## AI Usage Notice

Parts of this codebase may have been written with AI assistance; other parts may have been written
entirely by hand. The mix varies and is not tracked per file.

What does not vary: **every change is reviewed by a human before it lands.** A person reads it, decides
whether it is correct, and takes responsibility for shipping it. Nothing is merged simply because a tool
produced it.

Some commit messages may also be AI-generated. That applies to the *wording of the message only* — never
to the decision to make a change, what the change does, or the judgement that it was fit to commit.

So if something here is wrong, it is a human's mistake. That is where responsibility sits, and it is not
delegated.

## License

[MIT](LICENSE.txt) — © 2026 Richard "TekuSP" Torhan.

Third-party licenses are collected at build time across the full transitive dependency closure and are
viewable in-app under **Settings → Licenses**. Each entry shows the package's own embedded license text
where it ships one, a canonical SPDX text where the package declares an identifier we hold, and
"Unknown license terms" otherwise — nothing is ever guessed on a package's behalf.

## Documentation

- [CHANGELOG.md](CHANGELOG.md) — what shipped in each release
- [CONTRIBUTING.md](CONTRIBUTING.md) — building, the zero-warning bar, and hardware-safety rules
- [SECURITY.md](SECURITY.md) — reporting a vulnerability, and the limitations known in 0.1.0
- [docs/ReleasePlan.md](docs/ReleasePlan.md) — release scope, gating decisions, and outstanding work
- [docs/Architecture.md](docs/Architecture.md) — how the client and service fit together
- [SubZeroFramework/Docs/](SubZeroFramework/Docs/) — IPC authorization posture and the fan-safety checklist

Built with [Uno Platform](https://platform.uno).
