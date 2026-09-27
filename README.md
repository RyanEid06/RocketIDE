# RocketIDE

RocketIDE is a Windows-only desktop IDE built specifically for the Rocket programming language.

The goal is not to clone Visual Studio or VS Code. RocketIDE aims to provide a focused, polished environment for creating, navigating, editing, diagnosing, building, running, testing, formatting, and maintaining real multi-file Rocket projects.

## Project status

RocketIDE 1.0.0 for Windows x64 is published as a portable package. FINAL-WP07 is closed with **34 acceptance checks passed and 13 explicitly waived/accepted**. The full local verification gate passed **514/514 tests**; package replay and public download verification are recorded in the [acceptance report](docs/wp07-2026-09-27/ACCEPTANCE_REPORT.md).

- `main` contains the full developer source, tests, documentation and all fixes through FINAL-WP07, including the Target Information CLI adapter regression fix.
- [`consumer`](https://github.com/RyanEid06/RocketIDE/tree/consumer) contains only the released app folder, a short README, release metadata, checksums and required Git attributes.
- [Download RocketIDE 1.0.0](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip). Extract the entire ZIP and open RocketIDE.exe; this release has no installer wizard.

The source and consumer release tags preserve the frozen 1.0.0 identities. Subsequent branch organization and documentation updates do not change the published binaries. See [branch layout](docs/BRANCH_LAYOUT.md) for the current repository arrangement; earlier acceptance reports record the branch names at release time.

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
- Microsoft DbgX/DbgEng for standalone native Rocket debugging

## Core principle

RocketIDE is a **client of Rocket tooling**, not another implementation of Rocket.

Do not create an IDE-local Rocket parser, type checker, semantic model, diagnostic engine, formatter, or package resolver just to make a feature appear to work. Semantic behavior comes from `rocket-lsp`. Build, test, and tooling behavior comes from `rocketc`.

Workspace-local Rocket binaries are treated as untrusted project content. If developing Rocket itself, explicitly trust that checkout once under **Tools > Rocket SDK Settings** before RocketIDE may probe or start its checkout-local `rocketc.exe` / `rocket-lsp.exe`; trust is stored per-user, not in the repository.

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
  src/RocketIDE.Debugger/ native debugger transport, protocol, session state
  tests/                  project-aligned automated tests, including debugger tests
  CONTRIBUTING.md         engineering rules
  ROADMAP.md              implementation WPs and progress ledger
```
