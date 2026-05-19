---
name: service-lifecycle-ui
description: 'Implement or debug SubZeroFramework service lifecycle controls. Use when editing Settings or Warnings and Issues button enablement, packaged helper readiness, install/update/uninstall/restart/shutdown/autorun gating, or post-action refresh behavior.'
argument-hint: 'Describe the lifecycle UI state, gating bug, or action flow to review.'
---
# Service Lifecycle UI

Use this skill when a Settings or Warnings and Issues change affects how service readiness, package availability, or lifecycle actions are presented to the user.

## Read first
- [Architecture.md](../../../Architecture.md)
- [FunctionalitySpecification.md](../../../FunctionalitySpecification.md)
- [README.md](../../../README.md)
- [SubZeroFramework.Service/README.md](../../../SubZeroFramework.Service/README.md)

## Use this for
- Settings lifecycle button enablement
- Warnings and Issues remediation actions
- packaged helper discovery versus installed-service state
- readiness text or `InfoBar` messaging for service actions
- post-install or post-uninstall refresh behavior

## What this workflow protects
- packaged helper availability is not the same thing as installed service registration
- lifecycle actions should stay on the packaged service-management path, not normal gRPC mutation flow
- the UI should refresh from service state after install or uninstall instead of waiting for the page to reopen

## Procedure

### 1. Separate the two state questions
Treat these as distinct checks:
- **Is the service installed or registered?**
- **Is the packaged helper available for lifecycle operations?**

Do not collapse them into one readiness flag.

### 2. Gate each action from the correct state
- Gate **restart**, **shutdown**, **autorun**, and **uninstall** on service registration being present.
- Gate **install** on packaged helper available and service not installed.
- Gate **update** on packaged helper available and service already installed.

### 3. Refresh state after install or uninstall
After an install or uninstall action completes, re-query `IFrameworkServiceControlClient.GetInfo()` so button state, readiness text, and related UI messaging update without reopening the page.

### 4. Keep the UI honest about capability
- If the packaged helper is missing, explain that install or update is unavailable even if the rest of the page loads.
- If the service is not installed, disable actions that require registration instead of letting them fail late.
- Keep lifecycle result messaging on standard reachable surfaces such as `InfoBar`.

### 5. Preserve the privilege boundary
The UI should surface lifecycle actions, but the privileged work should still happen through the packaged service executable in service-management mode.

Do not move install, update, restart, shutdown, uninstall, or autorun into ordinary gRPC command flow.

## Output format
Return a concise summary with:
1. the lifecycle states distinguished,
2. the action-gating matrix applied,
3. whether `GetInfo()` refresh behavior was preserved,
4. any remaining readiness or privilege-boundary risk.
