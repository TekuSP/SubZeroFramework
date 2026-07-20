# SubZeroFramework App

This project contains the Uno Platform client application. It is intentionally not the privileged hardware owner; it consumes service-published status, telemetry, inventory, fan-control state, and lifecycle metadata over local gRPC.

## Current UI status

- Dashboard and Device Capabilities are the strongest reference pages for the current card-based visual language, stable item identity, and Framework-first inventory flow, including CPU package cards with recent average-usage and frequency charts, usage-colored per-core cards, explicit GPU ↔ monitor linking, monitor current mode details, and an Unknown graphics card bucket for unlinked monitors.
- Settings now includes a local Units section backed by `IUserUnitPreferencesClient`, and UI-facing numeric values are formatted through `IUnitFormattingService` rather than page-specific conversion logic.
- Dashboard, Thermal Telemetry, Power cards, fan cards, cooling hardware views, and Device Capabilities CPU / memory / storage / monitor / network surfaces now honor client-local display units and chart labelers while keeping service-side canonical units intact.
- Device Capabilities also now uses explicit Adapter labels, numbered monitor subcards, left-aligned wrap-friendly layouts, Unknown rendering for sentinel network link speeds, and dB(A)-normalized desktop cooling acoustic displays.
- Settings and Warnings and Issues now expose service health, package readiness, privilege-prompt guidance, and quick lifecycle actions for restart, install, update, uninstall, and autorun changes.
- Thermal Telemetry is now the first end-to-end dedicated telemetry page over the typed IPC clients; the larger remaining UI slices are Power Telemetry and Fan Curve Profiles.

## Related docs

- [../docs/ReleasePlan.md](../docs/ReleasePlan.md)
- [../docs/Architecture.md](../docs/Architecture.md)
- [../docs/FunctionalitySpecification.md](../docs/FunctionalitySpecification.md)
- [Docs/TelemetryUiGuide.md](Docs/TelemetryUiGuide.md)
- [../SubZeroFramework.Service/README.md](../SubZeroFramework.Service/README.md)

## Uno references

To discover how to get started with Uno Platform: [https://aka.platform.uno/get-started](https://aka.platform.uno/get-started)

For more information on how to use the Uno.Sdk or upgrade Uno Platform packages in this solution: [https://aka.platform.uno/using-uno-sdk](https://aka.platform.uno/using-uno-sdk)
