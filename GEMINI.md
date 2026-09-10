# Gemini Engineering Contract — RocketIDE

This file is mandatory. Read it before editing any code.

## Mission

Build a production-quality Windows desktop IDE for the Rocket programming language by executing `ROCKET_IDE_IMPLEMENTATION_PLAN.md` in order.

The user is intentionally vibe-coding this project. That means **you are responsible for engineering discipline**. Do not substitute optimistic claims for verification.

## Absolute rules

1. **Rocket is the source of truth.**
   - `rocket-lsp.exe` owns live diagnostics, completion, hover, signature help, definitions, references, rename, semantic tokens, code actions, formatting, workspace symbols, and project analysis.
   - `rocketc.exe` owns check/build/run/test/fmt/new/resolve/tree/audit/coverage/profile/benchmark/target behavior.
   - Never create a competing parser, type checker, formatter, module resolver, diagnostic code system, or semantic model inside RocketIDE.

2. **Never fake a feature to satisfy the UI.**
   - A button that does nothing is not implemented.
   - Hard-coded completion items are not completion.
   - Regex-based semantic errors are not diagnostics.
   - Regex-based rename is not rename.
   - Parsing human compiler prose when JSON is available is not acceptable.

3. **Test-first for non-trivial behavior.**
   - Add or update the test that proves the requirement.
   - Observe the failure.
   - Implement the smallest correct change.
   - Run focused tests.
   - Run the full solution verification gate before claiming the WP complete.

4. **Windows CI is the authority.**
   Google AI Studio is allowed to generate/edit the repository, but it is not proof that a WPF `.exe` works. A WP that affects buildable code is not `[x]` until the Windows GitHub Actions workflow passes, or the same commands have been run successfully on a real Windows development machine and the evidence is recorded.

5. **Do not modify the Rocket compiler repository from this repository.**
   If an IDE feature requires a missing Rocket capability, add an entry under `Upstream Rocket requests` in the implementation plan with prefix `ROCKET-UPSTREAM-REQUEST:`. Continue with graceful degradation where possible. Do not invent incompatible IDE-only behavior.

6. **Preserve project boundaries.**
   - `RocketIDE.Core`: domain models and interfaces; no WPF, no process APIs.
   - `RocketIDE.Rocket`: Rocket-specific LSP/compiler/tool contracts and adapters; no WPF.
   - `RocketIDE.Infrastructure`: Windows/filesystem/process/settings/session implementations; no editor UI.
   - `RocketIDE.App`: WPF composition, views, view-models, AvalonEdit adapters.
   - Tests mirror the owning project.
   No circular references.

7. **Keep files focused.**
   A class should have one reason to change. If a file grows because it contains transport + protocol + UI + state management, split it before adding more behavior.

8. **No blocking UI thread I/O.**
   File reads, workspace scanning, compiler runs, LSP I/O, search, and large operations are async/cancellable. UI dispatch is only for final presentation state.

9. **No machine-specific paths in source control.**
   Never commit a username, drive letter, local checkout path, generated `.rocketc`, `.vs`, `bin`, `obj`, SDK cache, or installed Rocket path.

10. **Security and safety.**
    Opening a project must not execute Rocket code, native programs, package scripts, or registry operations. Source-controlled project content is untrusted input. Only explicit user commands may run/build/test programs.

11. **Respect Rocket LSP bounds.**
    - LSP protocol message: 16 MiB
    - header: 16 KiB
    - one open document: 4 MiB
    - project defaults: 4,096 files / 64 MiB source
    - workspace edit: 1,024 edits
    For source documents above 4 MiB, use RocketIDE Large File Mode. Never send oversized text to `rocket-lsp`.

12. **Structured compiler messages first.**
    On `check`, `build`, and `test`, request `--message-format=json` and consume newline-delimited `rocket-message-1`. Do not scrape diagnostics from English text.

13. **Cancellation must kill process trees.**
    Build/Run/Test/Stop must not leave child compiler/application processes behind. Use Windows Job Objects or another tested equivalent.

14. **LSP stdout is protocol-only.**
    `rocket-lsp` stdout is reserved for `Content-Length` framed JSON-RPC. Server stderr is diagnostic logging and may be shown in the Rocket Output panel.

15. **Progress tracking is evidence-based.**
    When completing a WP:
    - satisfy every acceptance criterion,
    - run its focused tests,
    - run `scripts/verify.ps1`,
    - obtain Windows CI success when required,
    - update the plan ledger,
    - add a short completion note containing evidence,
    - then commit.

## Coding conventions

- Nullable reference types enabled.
- Treat warnings as errors in repository-owned code.
- Prefer immutable records for protocol/domain values.
- Prefer interfaces at process/filesystem/LSP seams so tests do not spawn real tools unless explicitly integration tests.
- `CancellationToken` is required for operations that may wait on disk, process, transport, or workspace analysis.
- Never swallow exceptions. Convert expected operational failures into typed results or user-visible errors; log unexpected failures.
- UI-facing exceptions must not crash the entire IDE when recovery is possible.
- Do not add a dependency when the BCL is enough.
- Do not add an MVVM framework unless the existing architecture demonstrably becomes worse without it.
- Use `System.Text.Json` for RocketIDE-owned JSON.
- Use UTF-8 for source-controlled text.

## Required reading before semantic/tooling work

Read these snapshots under `references/rocket-current/`:

- `docs/LANGUAGE_SERVER.md`
- `docs/TOOLING.md`
- `docs/DIAGNOSTICS.md`
- `editors/visualstudio/README.md`
- `editors/vscode/syntaxes/rocket.tmLanguage.json`
- selected Visual Studio integration sources under `editors/visualstudio/CoreReference/`

These files are reference snapshots from the Rocket repo version used to design this starter. They are not to be edited as RocketIDE source.

## Required completion wording

Never say "done", "complete", "working", "bug free", or equivalent unless verification was actually run and passed. If verification was not available, say exactly what was implemented and exactly what remains unverified.
