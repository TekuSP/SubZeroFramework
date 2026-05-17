# SubZeroFramework App

This project contains the Uno Platform client application. It is intentionally not the privileged hardware owner; it consumes service-published status, telemetry, inventory, fan-control state, and lifecycle metadata over local gRPC.

## Current UI status

- Dashboard and Device Capabilities are the strongest reference pages for the current card-based visual language, stable item identity, and Framework-first inventory flow.
- Settings and Warnings and Issues now expose service health, package readiness, privilege-prompt guidance, and quick lifecycle actions for restart, install, update, uninstall, and autorun changes.
- The preferred next major UI slice remains the first dedicated Thermal Telemetry page wired end to end through `IFrameworkTelemetryClient`.

## Related docs

- [../WorkToBeDone.md](../WorkToBeDone.md)
- [../Architecture.md](../Architecture.md)
- [../FunctionalitySpecification.md](../FunctionalitySpecification.md)
- [Docs/TelemetryUiGuide.md](Docs/TelemetryUiGuide.md)
- [../SubZeroFramework.Service/README.md](../SubZeroFramework.Service/README.md)

## Uno references

To discover how to get started with Uno Platform: https://aka.platform.uno/get-started

For more information on how to use the Uno.Sdk or upgrade Uno Platform packages in this solution: https://aka.platform.uno/using-uno-sdk
