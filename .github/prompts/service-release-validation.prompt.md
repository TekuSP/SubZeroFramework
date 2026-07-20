---
name: "Service Release Validation"
description: "Validate service release readiness in SubZeroFramework after service, IPC, packaging, or lifecycle changes. Use for install, update, uninstall, restart, autorun, packaged bundle checks, release-risk review, and pre-merge service validation."
argument-hint: "Describe the service, IPC, packaging, or release-candidate change to validate."
agent: "SubZero Service and IPC Engineer"
---
Validate the current SubZeroFramework service release slice for the user-provided scope.

Use these references first:
- [repo instructions](../copilot-instructions.md)
- [roadmap](../../docs/ReleasePlan.md)
- [architecture](../../docs/Architecture.md)
- [service README](../../SubZeroFramework.Service/README.md)
- [IPC authorization and UI cadence](../../SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md)
- [fan safety shutdown checklist](../../SubZeroFramework/Docs/FanSafetyShutdownChecklist.md)

Validation goals:
- confirm lifecycle actions still go through `SubZeroFramework.Service --service-management <operation>` rather than normal gRPC
- check packaged bundle expectations for `service-package/windows` and `service-package/linux`
- verify service, IPC, and fan-safety boundaries still match the docs
- run the smallest relevant workspace validation slice, preferring `build-service`, `test-service`, and `build-windows` or `build-linux` only when the touched code requires them
- separate automated validation from manual-only release risk

Pay special attention to:
- Windows install and update recovery configuration
- Linux systemd install and update behavior and any remaining real-world validation gaps
- caller identity validation limits and fail-closed fan-control behavior
- shutdown and restore-to-auto semantics
- logging, versioning, and deployment gaps still called out in `docs/ReleasePlan.md`

Return:
## Scope
- what changed and which release concerns apply

## Automated validation
- tasks, tests, and file checks run
- key pass or fail results

## Release findings
- packaged bundle and lifecycle observations
- architecture or safety constraints that were preserved or violated

## Manual follow-up
- platform-specific checks still required before shipping
- any release notes, docs, or diagnostics follow-up

## Ship / hold recommendation
- state clearly whether this slice is ready to merge, ready for manual validation, or blocked
- do not claim full release readiness if platform-specific manual checks were not actually performed
