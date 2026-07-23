# Installing SubZero Framework Edition

Grab the artifacts for your platform from the
[Releases](https://github.com/TekuSP/SubZeroFramework/releases) page. Verify downloads against the
`SHA256SUMS.txt` attached to each release. No .NET installation is needed on any platform — everything
ships self-contained.

---

## Windows (x64 / ARM64)

Download `SubZeroFramework-Setup-<version>-x64.msi` (or `-arm64.msi`) and run it. The wizard installs
the app and registers + starts the background service automatically. Requires administrator.

> The installer is not code-signed yet — SmartScreen will warn. Choose **More info → Run anyway**, or
> verify the download against `SHA256SUMS.txt` first.

Silent install, for scripting:

```
msiexec /i SubZeroFramework-Setup-0.1.1-x64.msi /qn
```

**Uninstall:** Settings → Apps (or `msiexec /x <the .msi> /qn`). The service is stopped and
deregistered automatically.

---

## Debian / Ubuntu (x64 / ARM64)

The UI depends on the service package at the exact same version, so install **both in one command**:

```bash
sudo apt install ./subzeroframework-service_0.1.1_amd64.deb ./subzeroframework_0.1.1_amd64.deb
```

The service is enabled and started automatically.

**Uninstall:** `sudo apt remove subzeroframework subzeroframework-service`

---

## Fedora / RHEL (x64 / ARM64)

```bash
sudo dnf install ./subzeroframework-service-0.1.1-1.x86_64.rpm ./subzeroframework-0.1.1-1.x86_64.rpm
```

The service is enabled and started automatically.

**Uninstall:** `sudo dnf remove subzeroframework subzeroframework-service`

---

## Arch (x64 / ARM64)

One combined package (UI + service):

```bash
sudo pacman -U subzeroframework-bin-0.1.1-1-x86_64.pkg.tar.zst
sudo systemctl enable --now subzeroframework.service
```

Note the second command — per Arch convention, packages do not auto-enable services. Dependencies
(including `lshw`, which the service uses for memory/storage inventory) resolve from the repos
automatically.

**Uninstall:**

```bash
sudo systemctl disable --now subzeroframework.service
sudo pacman -R subzeroframework-bin
```

---

## Any Linux (tarball)

For distributions without a package above:

```bash
tar -xzf subzeroframework-0.1.1-linux-x64.tar.gz
cd subzeroframework-0.1.1
sudo mkdir -p /usr/lib/subzeroframework
sudo cp -r ui service /usr/lib/subzeroframework/
sudo ln -sf /usr/lib/subzeroframework/ui/SubZeroFramework /usr/bin/subzeroframework
sudo cp subzeroframework.service /usr/lib/systemd/system/
sudo cp subzeroframework.desktop /usr/share/applications/
sudo systemctl daemon-reload
sudo systemctl enable --now subzeroframework.service
```

The bundled systemd unit points at `/usr/lib/subzeroframework/service/`, so this layout is required,
not a suggestion. Runtime dependencies you may need from your package manager: `libudev`, `icu`,
`fontconfig`, `lshw` (memory/storage inventory), and the X11 client libraries (`libX11`, `libXext`,
`libXfixes`, `libXi`, `libXrandr`, `libGL`).

---

## After installing (all platforms)

1. **Verify the service**: `systemctl status subzeroframework` on Linux, or check
   **Settings → Service** in the app.
2. **Fan control is off by default.** The app shows telemetry but refuses fan writes until you turn on
   **Settings → Service → "Allow fan control commands"** and apply (Save to persist). This is
   deliberate: writing fan duty to the embedded controller is the one thing that touches your
   hardware, so it is opt-in.
3. On a Framework 16 without a reporting expansion-bay module, the bay simply shows as not present —
   that is normal, not an error.

## Troubleshooting

- **"Background service offline" while `systemctl` says running** — check
  `journalctl -u subzeroframework -n 100` and file a
  [bug report](https://github.com/TekuSP/SubZeroFramework/issues/new/choose) with the log; the issue
  form asks for everything needed.
- **Fan pages read-only** — see step 2 above; that is the opt-in toggle, not a fault.
- **Missing memory/storage details in Device Capabilities on Linux** — install `lshw` (the packages
  from 0.1.1 onward depend on it; tarball installs need it manually).
