# RocketIDE

RocketIDE is a Windows-only desktop IDE built specifically for the Rocket programming language.

The goal is not to clone Visual Studio or VS Code. RocketIDE aims to provide a focused, polished environment for creating, navigating, editing, diagnosing, building, running, testing, formatting, and maintaining real multi-file Rocket projects.

## Project status

RocketIDE is under active development through the work packages tracked in [`ROADMAP.md`](ROADMAP.md).

- IDE-WP00 — Repository baseline + Windows CI: **DONE**
- Current next milestone: **IDE-WP01 — Native shell + layout**

A work package is not considered complete because its UI looks finished. Focused tests, `scripts/verify.ps1`, and the Windows CI gate must pass before its status is changed to `DONE`.

## Read this first

1. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) for mandatory engineering and verification rules.
2. Read [`docs/ROCKET_IDE_DESIGN_SPEC.md`](docs/ROCKET_IDE_DESIGN_SPEC.md) for the product architecture and scope.
3. Read [`docs/ROCKET_INTEGRATION_CONTRACT.md`](docs/ROCKET_INTEGRATION_CONTRACT.md) before touching Rocket compiler or language-server integration.
4. Follow [`ROADMAP.md`](ROADMAP.md) in dependency order and update its progress ledger only after verification evidence exists.

## Technology

- Windows x64 first
- C# 14
- .NET 10
- WPF
- AvalonEdit
- MSTest
- `rocket-lsp.exe` for editor semantics
- `rocketc.exe` for compiler and tooling commands

## Core principle

RocketIDE is a **client of Rocket tooling**, not another implementation of Rocket.

Do not create an IDE-local Rocket parser, type checker, semantic model, diagnostic engine, formatter, or package resolver just to make a feature appear to work. Semantic behavior comes from `rocket-lsp`. Build, test, and tooling behavior comes from `rocketc`.

## Development workflow

For each work package:

1. Start from the latest verified repository state.
2. Implement only the current WP and the smallest prerequisite fixes it genuinely requires.
3. Add or update focused tests for behavioral changes.
4. Run the relevant focused tests.
5. Run `scripts/verify.ps1` on Windows.
6. Push the branch/commit and require `.github/workflows/windows-ci.yml` to pass.
7. Review the resulting diff and CI evidence.
8. Update the `ROADMAP.md` ledger to `DONE` only after the gate passes.

## Repository layout

```text
RocketIDE/
  .github/workflows/      Windows CI
  docs/                   architecture and Rocket integration contracts
  references/rocket-current/
                          read-only snapshots from the Rocket repository
  scripts/                verification tooling
  src/RocketIDE.App/      WPF application and presentation layer
  src/RocketIDE.Core/     UI-independent domain models and interfaces
  src/RocketIDE.Rocket/   Rocket LSP/compiler/tool adapters
  src/RocketIDE.Infrastructure/
                          filesystem, processes, settings, recovery, logging
  tests/                  project-aligned automated tests
  CONTRIBUTING.md         engineering rules
  ROADMAP.md              implementation WPs and progress ledger
```
