# SubZeroFramework.Service

## Windows service registration

Publish the service, then register it as an auto-starting Windows service:

```powershell
sc.exe create SubZeroFrameworkService binPath= "C:\Path\To\SubZeroFramework.Service.exe" start= auto
sc.exe failure SubZeroFrameworkService reset= 0 actions= restart/5000/restart/5000/restart/5000
```

Run the service under an account with the privileges required for Framework EC access.

## Linux systemd registration

1. Publish the service to `/usr/local/lib/subzeroframework`.
2. Copy `subzeroframework.service` to `/etc/systemd/system/subzeroframework.service`.
3. Enable and start the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now subzeroframework.service
```

The service runs as `root` because Framework EC access on Linux requires elevated privileges.

## Safety note

The service restores automatic fan control during a normal host shutdown path. This reduces the risk of leaving EC fan control in a manual state before process exit.

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
