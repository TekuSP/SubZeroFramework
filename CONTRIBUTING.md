# Contributing

Thanks for taking an interest. This is a small project with a specific quirk: it drives real hardware, so
some rules below are stricter than they might look for a desktop app.

## Prerequisites

- **.NET 10 SDK**
- **Windows**: Visual Studio 2022+ (or `dotnet` CLI) with the WinUI/Windows App SDK workload
- **Linux**: the Skia desktop head builds with the CLI alone
- A **Framework laptop** if you want to exercise anything hardware-related. Most UI work does not need one
  — without EC hardware the app runs and shows its recovery screen.

## Building

```
dotnet build SubZeroFramework/SubZeroFramework.csproj -f net10.0-windows10.0.26100 -c Debug
dotnet build SubZeroFramework/SubZeroFramework.csproj -f net10.0-desktop            -c Debug
dotnet build SubZeroFramework.Service/SubZeroFramework.Service.csproj               -c Debug
dotnet test  SubZeroFramework.Tests/SubZeroFramework.Tests.csproj
```

The app is a single project with **two target frameworks**, and both must build. It is easy to write XAML
or platform code that satisfies one head and breaks the other, so always build both before opening a PR.

## The bar for a change

**Zero warnings, zero errors, on both target frameworks.** This is not aspirational — it is the standing
gate, and CI enforces it. A PR that adds a warning will not be merged.

All tests must pass. If you change behaviour, add or update a test.

### Project analyzers

The repo ships its own Roslyn analyzers (`SubZeroFramework.Analyzers`, rule prefix `SZF`) that encode
conventions the compiler cannot. Don't suppress one to get green — if a rule fires, it usually means the
pattern is wrong. If you genuinely believe a suppression is correct, justify it in the
`[SuppressMessage]` attribute itself.

Two conventions worth knowing before your first PR:

- **No revision counters.** Never bump an integer purely to re-raise `PropertyChanged`. Store the derived
  value and assign it; the MVVM Toolkit's value comparison suppresses redundant notifications for you.
- **Quantities go through `IUnitFormattingService`**, in both directions, including slider minimums,
  maximums and values. The service side keeps canonical units; display units are a client-only concern.

## Hardware safety

The service can write fan duty to the embedded controller. Treat that code with more care than the rest:

- Fans must be restored to EC automatic control on stop, crash, and shutdown. If you touch the shutdown,
  fatal-exit, or preview-watchdog paths, read
  [`SubZeroFramework/Docs/FanSafetyShutdownChecklist.md`](SubZeroFramework/Docs/FanSafetyShutdownChecklist.md)
  first and re-run it afterwards.
- Fan control follows a **Stage → Preview → Apply** model. Preview actuation is volatile and guarded by a
  watchdog that reverts it. Don't add a path that writes duty outside that model.
- If you cannot test a hardware change on a real Framework laptop, say so explicitly in the PR. That is
  fine and useful — it is not fine to leave it unsaid.

## Pull requests

- Keep them focused. One concern per PR.
- Match the surrounding code: its naming, its comment density, its idioms.
- Explain *why*, not just *what*. The what is in the diff.
- Note anything you could not verify.

## AI Usage Notice

Contributions written with AI assistance are welcome, on one condition: **you have reviewed and understood
the change, and you stand behind it.** Submitting output you have not read is not contributing — it moves
the review burden onto someone else. The same standard applies to the maintainer; see the
[AI Usage Notice](README.md#ai-usage-notice) in the README.

## Where things are

- [`docs/ReleasePlan.md`](docs/ReleasePlan.md) — current scope, gating decisions, and open work. Read this before
  proposing something large; it may already be a deliberate decision.
- [`docs/Architecture.md`](docs/Architecture.md) — how the client and service fit together.
- [`SubZeroFramework/Docs/`](SubZeroFramework/Docs/) — IPC authorization posture, telemetry UI guide, and
  the fan-safety checklist.
- [`CHANGELOG.md`](CHANGELOG.md) — what shipped when.
