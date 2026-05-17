# SubZeroFramework.Service

SubZeroFramework.Service is the privileged local background service. It owns Framework EC access, hardware polling, inventory fallback mapping, fan-control writes, and the authoritative status and telemetry streams consumed by the Uno client over local gRPC.

## Packaged service bundles

The preferred deployment shape is a published service bundle staged next to the app artifact.

- Windows bundle: `service-package/windows`
- Linux bundle: `service-package/linux`

Local and CI packaging scripts:

- `Scripts/package-windows-service.ps1`
- `Scripts/package-linux-service.sh`

The client lifecycle UI discovers those bundles automatically when they are present and uses them as the install or update source.

## Microsoft guidance alignment

The current hosting shape is aligned with the relevant Microsoft Windows Service and ASP.NET Core systemd guidance for long-running .NET services:

- `Program.cs` calls `AddWindowsService` so Windows Service lifetime, content-root handling, and Event Log integration activate automatically when the process is running under SCM.
- `Program.cs` also calls `AddSystemd`, which is context-aware and enables systemd lifetime notifications and journald-friendly console formatting when the process is running under systemd.
- The project disables Web SDK `web.config` publish transforms because this service is hosted by SCM rather than IIS.
- `subzeroframework.service` uses `Type=notify`, which is required for `AddSystemd` readiness notifications, and includes the Microsoft-recommended `KillSignal=SIGINT`, `SyslogIdentifier`, and `TimeoutStopSec` settings for cleaner shutdown and easier log discovery.
- Windows Event Log settings are explicitly bindable from configuration, with a stable `SourceName` of `SubZeroFrameworkService`.

The packaged service bundle is now published as self-contained for both Windows and Linux service artifacts. Microsoft documents both framework-dependent and self-contained deployment as valid service-hosting choices; this repo now prefers self-contained service bundles so install and update do not depend on a preinstalled matching .NET runtime.

## Can the service change its own service definition on the fly?

Yes, but the safe way is through the separate elevated management invocation, not from the already running service process trying to rewire itself in place.

- On Linux, the packaged management mode can replace `subzeroframework.service`, run `systemctl daemon-reload`, and then restart the unit.
- On Windows, the packaged management mode can update SCM configuration with `sc.exe config` or related commands and then restart the service.

That is effectively how `update`, `enable-autorun`, `disable-autorun`, and `restart` already work today: a separate elevated launch of `SubZeroFramework.Service --service-management <operation>` mutates the unit or SCM entry and then stops or starts the service as needed.

What should not happen is the live gRPC service instance editing its own unit or SCM registration from inside the request-handling process and trying to continue uninterrupted. For changes to service registration, executable path, restart policy, or start mode, the correct pattern is:

1. launch the packaged service executable in management mode with elevation
2. update the unit file or SCM settings
3. reload the service manager if needed
4. restart or start the service

## Service management CLI

The published service executable supports an elevated self-management mode:

```text
SubZeroFramework.Service[.exe] --service-management <operation>
```

Supported operations:

- `install`
- `update`
- `uninstall`
- `shutdown`
- `restart`
- `enable-autorun`
- `disable-autorun`

This path is intentionally outside gRPC so install, update, shutdown, and uninstall still work when the service is offline, not yet registered, or intentionally stopping itself.

### Windows behavior

When launched elevated on Windows, the management mode:

- registers or updates `SubZeroFrameworkService` in the Service Control Manager
- points the service entry at the packaged executable path
- configures restart-on-failure behavior with `sc.exe failure`
- starts the service for `install` and `update`
- preserves the existing Windows startup mode during `update`; only `install`, `enable-autorun`, and `disable-autorun` change the startup type intentionally
- toggles autorun through `sc.exe config start= ...`
- stops or deletes the service for `shutdown` and `uninstall`

### Linux behavior

When launched with root privileges on Linux, the management mode:

- copies the published bundle into `/usr/local/lib/subzeroframework`
- installs `/usr/local/bin/SubZeroFramework.Service`
- installs `/etc/systemd/system/subzeroframework.service`
- runs `systemctl daemon-reload`
- starts the unit for `install` and `update`
- toggles autorun separately through `enable-autorun` and `disable-autorun`
- removes the installed files and unit on `uninstall`

Install and update currently start the service but do not automatically enable autorun; that remains an explicit lifecycle action.

## Manual invocation examples

```powershell
SubZeroFramework.Service.exe --service-management install
SubZeroFramework.Service.exe --service-management update
SubZeroFramework.Service.exe --service-management enable-autorun
```

```bash
sudo ./SubZeroFramework.Service --service-management install
sudo ./SubZeroFramework.Service --service-management update
sudo ./SubZeroFramework.Service --service-management enable-autorun
```

## Safety note

The service restores automatic fan control during a normal host shutdown path. This reduces the risk of leaving EC fan control in a manual state before process exit.

The same controlled shutdown path is used when service-management actions stop or restart the service.

Windows updating or sudden power loss can still interrupt any user-mode service, so EC auto-restore on shutdown is a mitigation, not a guarantee.

## Fan control state configuration

The telemetry stream now reports fan operating mode separately from runtime fan health.

- `auto`: EC-managed fan control.
- `manual`: direct RPM or duty control was applied through the fan-control RPCs.
- `custom_curve`: the service owns fan adjustments based on one or more driving sensors, an aggregation strategy, and a temperature-to-duty curve.

You can seed a reported control state from configuration today:

```json
{
	"FrameworkService": {
		"PollingInterval": "00:00:02",
		"AllowFanControlCommands": true,
		"FanControlStates": [
			{
				"FanIndex": 0,
				"Mode": "CustomCurve",
				"CustomCurvePoints": {
					"10": 30,
					"30": 70,
					"80": 100
				},
				"DrivingTemperatureAggregation": "Average",
				"DrivingSensorIndices": [6, 8]
			},
			{
				"FanIndex": 1,
				"Mode": "CustomCurve",
				"CustomCurvePoints": {
					"15": 25,
					"45": 65,
					"85": 100
				},
				"DrivingTemperatureAggregation": "Maximum",
				"DrivingSensorIndices": [2, 3, 4]
			}
		]
	}
}
```

Supported aggregation values are `Average`, `Median`, `Maximum`, and `Minimum`.

The telemetry contract reports sensor ids for each fan control state, not sensor temperatures. The UI is expected to resolve those ids through the regular temperature telemetry stream and compute the current driving temperature locally.
