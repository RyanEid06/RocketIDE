# WP09-WP17 final manual/Codex acceptance follow-ups

Date: 2026-09-14

The implementation roadmap through WP17 is merged into `main`. Automated Windows verification on merged commit `bf30f98` passed with **348/348 tests**, self-contained win-x64 publish, `RocketIDE.Debugger.dll` verification, and bundled `amd64/EngHost.exe` verification. The corresponding GitHub `windows-ci` run also passed and produced both the verification folder and portable-package artifacts.

This document now tracks only evidence that is intentionally **not** claimed by automation. These items are not implementation TODOs unless the smoke test exposes a real defect.

## Deferred manual/Codex acceptance

- **WP09 — navigation/refactoring:** exercise Go to Definition, Find References, Rename, Quick Fix/code actions, and Format Document against the real Rocket LSP; confirm exact source ranges and all-or-nothing workspace edits.
- **WP10 — build/run/test/output:** run real Check/Build/Run/Test/Stop workflows against configured Rocket targets and verify streamed output, diagnostics, and child-process-tree termination.
- **WP11 — search/productivity:** exercise dirty-buffer search, replace preview/apply conflict detection, cancellation, navigation, and a large-result/stress case.
- **WP12 — large files/performance:** exercise files around the 4 MiB LSP cutoff and larger local-editing paths; confirm large files stay editable locally, do not enter LSP, and remain responsive.
- **WP13 — advanced tooling:** run supported advanced Rocket SDK commands from the IDE against a configured SDK and confirm structured output/error handling.
- **WP14 — reliability/recovery:** force-kill the IDE with dirty buffers, restore/discard/defer recovery, test external file changes/conflict overwrite protection, and check layout restoration.
- **WP15 — distribution:** unpack the CI/release portable ZIP on a clean Windows x64 machine without a preinstalled .NET runtime, launch `RocketIDE.exe`, configure an external Rocket SDK, and verify update/uninstall-by-folder behavior.
- **WP16 — polish/accessibility:** exercise keyboard/menu command states, high-DPI/multi-monitor behavior, focus order, accessibility names/screen-reader exposure, selection styling, and bracket highlighting.
- **WP17 — native debugger:** with a tiny real Rocket `--debug` target, prove source-breakpoint binding, F5 continue, Pause, F10/F11/Shift+F11 stepping, stopped-source navigation, Threads, Call Stack, Locals, debug output, explicit Stop, and the same flow from the packaged build.

## Evidence already complete

- Ordinary NuGet restore succeeds; the earlier code-only `NU1301`/credential limitation is obsolete.
- Full merged-main solution build succeeds.
- Full merged-main test suite: 348 passed, 0 failed, 0 skipped.
- Self-contained win-x64 publish succeeds.
- Debugger publish guards find `RocketIDE.Debugger.dll` and `amd64/EngHost.exe`.
- GitHub `windows-ci` on merged `main` succeeds and uploads both the verification artifact and portable ZIP/checksum artifact.
- WP17 no longer has a standalone-debugger upstream blocker: RocketIDE consumes Microsoft's redistributable DbgX/DbgEng backend while Rocket remains authoritative for `--debug` EXE/PDB/`rocket-source-map-1` artifacts.

If a manual check fails, record the exact reproduction and fix only the demonstrated defect; do not expand roadmap scope during the acceptance pass.
