# RocketIDE final implementation audit

Date: 2026-09-14
Baseline audited: merged `main` at `bf30f98` (`RocketIDE-main (9).zip`)

## Baseline evidence

Before this cleanup, merged `main` passed Windows `scripts/verify.ps1` with **348/348 tests**, self-contained win-x64 publish, `RocketIDE.Debugger.dll` verification, and bundled `amd64/EngHost.exe` verification. GitHub `windows-ci` on the same merged state also passed and produced both verification and portable-package artifacts.

## Repository audit results

- Project boundaries remain intact: Rocket semantics stay in `rocket-lsp`/`rocketc`; DbgX is isolated behind `RocketIDE.Debugger`.
- No source-controlled machine-specific absolute paths were found.
- Project/XAML/XML references and local Markdown links were structurally valid in the audited snapshot.
- Distribution scripts already enforce the WP17 multi-file debugger assets.
- Obsolete pre-WP17 feasibility scaffolding and stale “debugger deferred / Windows verification pending” documentation were identified for removal/update.
- The CI workflow still referenced Node-20-era action majors even though the current GitHub runner warned and forced Node 24 compatibility; the audit updates those official action majors.

## Defects fixed by this audit patch

1. **Breakpoint command state:** F9/Toggle Breakpoint could be enabled on a non-`.rocket` active tab even though the handler intentionally did nothing. Command availability now requires an active Rocket document and refreshes when the active tab changes.
2. **Debugger frame navigation:** frame double-click could navigate twice, with the second navigation using stale pre-selection frame data. The UI now relies only on the debugger's refreshed `Stopped` event/location after `SelectFrameAsync`.
3. **Explicit debugger Stop race:** interrupting an in-flight DbgEng `g/p/t/gu` request can fault that request. Explicit Stop now treats that interruption as part of stopping, continues authoritative transport teardown, and prevents the abandoned execution task from overwriting the terminal state.
4. **Stale production/version wording:** WP09-specific user/error strings are replaced with timeless RocketIDE capability wording.
5. **Dead feasibility model:** the unused Core `DebuggerFeasibility` placeholder and its placeholder-only test are removed now that the real backend exists.
6. **CI maintenance:** official GitHub actions are advanced to current Node-24-era majors (`checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7`).

## Still intentionally pending

No GUI/manual evidence is invented by this audit. The remaining Codex/manual acceptance matrix is maintained in `docs/WP11-WP17-CODE-ONLY-FOLLOWUPS.md`, including the real tiny-Rocket WP17 breakpoint/stepping/threads/stack/locals/output/package smoke.

## Required verification for this audit patch

Because this patch changes production debugger/command behavior and CI configuration, run Windows `scripts/verify.ps1` after applying it. Then commit/push only if the full suite/publish gate is green, and require the updated `windows-ci` run to pass before considering the final audit merged.
