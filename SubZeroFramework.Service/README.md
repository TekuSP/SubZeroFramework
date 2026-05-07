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
