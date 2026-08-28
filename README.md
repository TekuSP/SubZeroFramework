# SubZero Framework Edition

A companion app for [Framework](https://frame.work) laptops: live thermal, power and fan telemetry, and
per-fan control with custom curves.

It ships as two pieces. A **background service** owns privileged embedded-controller (EC) access —
hardware polling and fan writes — and an **unprivileged desktop app** talks to it over a local-only
socket. Nothing leaves your machine; there is no telemetry, no account, and no network service.

> **0.2.0 is the current release.** See [What's new](#whats-new-in-020) for this release, and
> [Known limitations](#known-limitations-in-020) before you install — a few surfaces are still
> deliberately switched off.

<img width="2560" height="1528" alt="image" src="https://github.com/user-attachments/assets/1af5fc42-ca90-44a9-acaa-32a6a9103a3e" />
<img width="2560" height="1528" alt="image" src="https://github.com/user-attachments/assets/e1d11b50-3ff3-4063-b878-30fd47088f02" />
<img width="2560" height="1528" alt="image" src="https://github.com/user-attachments/assets/7c880731-2de0-47bd-8c74-0f874463f8f8" />
<img width="2560" height="1528" alt="image" src="https://github.com/user-attachments/assets/07d03fd3-c52c-4dfa-944d-1a2f919b11fb" />
<img width="2560" height="1528" alt="image" src="https://github.com/user-attachments/assets/b5845e83-a310-4969-92d9-fe2c1adae289" />


## What's new in 0.2.0

**Cooling profiles.** A profile is a named setup covering every fan — switch between them from a shelf of
cards on the Dashboard, and the one you are on tints the title bar and the side bar in a colour you pick,
so the machine's cooling mood is visible from any page. The service owns the library, so a profile saved
in one window shows up in another, and the selection survives a restart. Profiles carry their own curve
points rather than pointing at a slot, so editing a fan's saved curve cannot silently change what a
profile means.

**Adaptive fan control.** A new per-fan mode that measures your machine and then holds a temperature
target rather than following a fixed curve. It calibrates the fan first — learning its minimum spin
speed, how much cooling each percent of duty actually buys, and how long the heat takes to respond — and
keeps refining that model while it runs, so it adapts to a repasted heatsink or a dusty intake instead of
needing a curve redrawn by hand.

**Update notifications.** The app checks GitHub Releases and tells you when a newer version exists, with
a button to the release page and a "Check for updates" item in the side bar. It never downloads or
installs anything by itself, and the automatic check can be switched off in Settings.

**Security and dependency updates.** The full NuGet dependency set was moved forward, picking up upstream
security fixes along with it.

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

Per-platform commands (Debian/Ubuntu, Fedora, Arch, tarball, silent Windows install, uninstall) live in
**[docs/INSTALL.md](docs/INSTALL.md)**. The short version:

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

Per-fan control lives on the **Fan Curve Profiles** page — Auto, Manual, Max, Custom curves and Adaptive,
with staged changes you preview before applying. Adaptive asks you to run a calibration first, because it
needs to measure the fan before it can hold a target with it.

Whole-machine switching lives on the **Dashboard**, where cooling profiles apply a saved setup to every
fan at once.

## Known limitations in 0.2.0

These are intentional, not bugs:

- **Modules** — the tab is disabled. It depends on EC slot reporting that is not complete yet.
- **Per-fan mode changes are not on the Dashboard.** Its mode row reports what each fan is doing but does
  not change it; one surface owns per-fan actuation, and that is the Fan Curve Profiles page. The
  Dashboard does apply cooling profiles, which act on every fan at once.
- **A profile's fan settings are fixed when it is created.** Editing one changes its name, colour and
  icon. To change what it does, select it, adjust the fans, and save those changes back into it.
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
- [SECURITY.md](SECURITY.md) — reporting a vulnerability, and the known limitations of the shipped posture
- [docs/ReleasePlan.md](docs/ReleasePlan.md) — release scope, gating decisions, and outstanding work
- [docs/Architecture.md](docs/Architecture.md) — how the client and service fit together
- [SubZeroFramework/Docs/](SubZeroFramework/Docs/) — IPC authorization posture and the fan-safety checklist

Built with [Uno Platform](https://platform.uno).
