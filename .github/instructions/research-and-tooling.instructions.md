---
name: "Research and Tooling Preferences"
description: "Use when researching code, documentation, packages, build steps, test validation, or source references in SubZeroFramework. Prefer GitHub and other structured sources, workspace tasks and build tools, and avoid ad hoc PowerShell commands when other options exist."
---
# Research and Tooling Preferences

- Start with the repo’s maintained sources of truth before improvising:
  - [copilot-instructions.md](../copilot-instructions.md)
  - [docs/ReleasePlan.md](../../docs/ReleasePlan.md)
  - [docs/Architecture.md](../../docs/Architecture.md)
  - [docs/FunctionalitySpecification.md](../../docs/FunctionalitySpecification.md)
- Before implementing a fix or feature, find the most authoritative source you can for the issue: existing repo docs, the relevant source file or companion repo, official documentation, GitHub source or examples, or other structured references.
- Prefer to ground implementation decisions in those authoritative sources instead of guessing from memory or starting with ad hoc experimentation.
- When looking for source code, examples, package guidance, or implementation context, prefer GitHub sources, official documentation, MCP-backed sources, and workspace search tools over ad hoc shell exploration.
- If a local companion repo is already documented, prefer that source directly instead of trying to discover it through shell commands.
- For build and validation work, prefer the workspace tasks and other build tools already defined by the repo instead of manual PowerShell commands.
- Do not use ad hoc PowerShell commands for routine search, build, test, or file-editing work when workspace tools, tasks, GitHub sources, or other structured sources can accomplish the same goal.
- If no non-PowerShell option can complete the task, explain the reason briefly and keep the PowerShell usage minimal and targeted.
- Never use terminal commands to edit workspace files when normal file-editing tools are available.
