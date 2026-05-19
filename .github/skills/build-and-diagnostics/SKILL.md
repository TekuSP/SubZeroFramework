---
name: build-and-diagnostics
description: 'Validate SubZeroFramework builds and diagnose build failures. Use when choosing the right VS Code task, checking service or client compile scope, debugging Uno/WinUI XAML failures, or deciding when task output is not enough and full Visual Studio build output is needed.'
argument-hint: 'Describe the build failure, validation slice, or project area to check.'
---
# Build and Diagnostics

Use this skill when you need to validate a SubZeroFramework change or turn a vague build failure into a concrete next step.

## Read first
- [WorkToBeDone.md](../../../WorkToBeDone.md)
- [README.md](../../../README.md)
- [Architecture.md](../../../Architecture.md)

## Use this for
- choosing between `build-service`, `build-windows`, `build-linux`, and `test-service`
- validating the smallest useful scope before widening to a full build
- debugging Uno or WinUI XAML failures
- deciding when task output is too shallow and a fuller IDE build log is required
- keeping routine validation on workspace tasks instead of ad hoc PowerShell commands

## Procedure

### 1. Pick the smallest useful validation slice
- Use `build-service` for service-only, core, contract, or service-management compile checks.
- Use `test-service` for service, core, contract, or shared logic changes that already have regression coverage.
- Use `build-windows` when the Uno client, view models, bindings, or XAML changed.
- Use `build-linux` when the change touches shared cross-platform code, transport, or Linux-sensitive behavior.

### 2. Prefer workspace tasks first
Use the repo's defined tasks before reaching for manual shell commands. If no task covers the scenario, keep any fallback shell usage minimal and targeted.

### 3. Treat XAML failures differently
If a Uno or WinUI XAML build fails and the task output is vague, ask for the full Visual Studio build output.

That output usually surfaces the real UXAML or WMC parser error, including the actual file and line, more clearly than the task output.

### 4. Report the failure in repo terms
Call out:
- which task was chosen and why
- the first actionable project, file, and line if available
- whether the failure is a compile error, analyzer error, or XAML parser error
- whether the next step is code change, broader validation, or a Visual Studio log request

### 5. Do not over-validate by default
For documentation-only or file-existence-only changes, a content check may be enough. For code changes, prefer the narrowest task that proves the touched surface.

## Output format
Return a concise summary with:
1. the task or validation slice chosen,
2. the first actionable failure or success signal,
3. whether full Visual Studio build output is needed,
4. the next recommended validation step.
