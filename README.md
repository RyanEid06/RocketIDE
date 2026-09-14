# RocketIDE

RocketIDE is a Windows-only desktop IDE built specifically for the Rocket programming language.

The goal is not to clone Visual Studio or VS Code. RocketIDE aims to provide a focused, polished environment for creating, navigating, editing, diagnosing, building, running, testing, formatting, and maintaining real multi-file Rocket projects.

## Project status

RocketIDE's current implementation roadmap is code-complete through IDE-WP17. Final release acceptance is still gated by the explicitly deferred manual/Codex smoke matrix in [`ROADMAP.md`](ROADMAP.md).

- IDE-WP00 through IDE-WP08: **DONE** with their recorded Windows verification evidence.
- IDE-WP09 through IDE-WP16: implementation and automated Windows verification are complete; the remaining interactive GUI/stress acceptance is intentionally deferred to the final Codex/manual pass.
- IDE-WP17: the standalone native debugger is implemented with Microsoft's DbgX/DbgEng backend and Rocket's existing `--debug` PDB/source-map contract. The merged `main` state at `bf30f98` passed `scripts/verify.ps1` with **348/348 tests**, win-x64 publish, debugger DLL/`EngHost.exe` guards, and the current `windows-ci` package run.
- The current implementation roadmap is code-complete through IDE-WP17. The only planned acceptance work left is the explicitly deferred GUI/live-Rocket smoke matrix recorded in [`ROADMAP.md`](ROADMAP.md) and [`docs/WP11-WP17-CODE-ONLY-FOLLOWUPS.md`](docs/WP11-WP17-CODE-ONLY-FOLLOWUPS.md).

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
