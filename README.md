# RocketIDE

RocketIDE is a Windows-only desktop IDE built specifically for the Rocket programming language.

The goal is not to clone Visual Studio or VS Code. The goal is to provide the smallest polished environment that lets a Rocket developer create, navigate, edit, diagnose, build, run, test, format, and maintain real multi-file Rocket projects without needing another editor.

## Read this first

1. Read [`GEMINI.md`](GEMINI.md). It contains mandatory engineering rules for any AI agent working in this repository.
2. Read [`docs/ROCKET_IDE_DESIGN_SPEC.md`](docs/ROCKET_IDE_DESIGN_SPEC.md). It defines what RocketIDE is and is not.
3. Execute [`ROCKET_IDE_IMPLEMENTATION_PLAN.md`](ROCKET_IDE_IMPLEMENTATION_PLAN.md) in order.
4. Update the progress ledger in the implementation plan after every accepted work package.
5. Do not mark a work package complete because the UI looks correct. Its tests and Windows CI gate must pass.

## Technology

- Windows x64 first
- C# 14
- .NET 10
- WPF
- AvalonEdit
- MSTest
- `rocket-lsp.exe` for editor semantics
- `rocketc.exe` for compiler/tool commands

## Core principle

RocketIDE is a **client of Rocket tooling**, not another implementation of Rocket.

Do not create an IDE-local Rocket parser, type checker, semantic model, diagnostic engine, formatter, or package resolver to make features appear to work. Semantic behavior comes from `rocket-lsp`. Build/test/tooling behavior comes from `rocketc`.

## Starter status

The repository skeleton is intentionally minimal. It provides project boundaries, a WPF shell, CI, verification scripts, current Rocket tooling reference material, and the full implementation roadmap. The functional IDE is built incrementally through the work packages.
