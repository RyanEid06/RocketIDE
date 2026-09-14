# WP11-WP17 code-only follow-ups

Date: 2026-09-14

This pass implemented the code for WP11 through WP17. The roadmap statuses remain gated by the existing smoke-test policy; no phase is being marked complete solely from automated checks.

## Deliberately deferred smoke

The following remain `MANUAL SMOKE DEFERRED` for a later pass:

- Launch the WPF application and exercise the WP11 search/replace UI, including dirty-buffer search, replace conflicts, cancellation, and large-result presentation.
- Exercise WP12 with the real Rocket language server and real files around the 4 MiB LSP boundary and 64 MiB editor boundary.
- Run real Rocket CLI commands from the WP13 command surface, including native-risk measurement commands, against a configured SDK.
- Exercise WP14 restore/discard/cancel recovery flows, crash logging, layout restore, DPI/multi-monitor clamping, and forced-process termination in the desktop application.
- Launch the packaged WP15 executable and verify portable behavior on a clean Windows environment.
- Exercise WP16 keyboard/menu accessibility, command-state transitions, bracket highlighting, and screen-reader-visible names in the running WPF application.
- Produce and inspect WP17 debugger artifacts from a real Rocket build and validate the Visual Studio/native CodeView/PDB/source-map path.

No live GUI smoke, real SDK/tool invocation, debugger attachment, or CI-hosted verification was performed in this code-only pass.

## Environment follow-up

- The ordinary NuGet restore path was blocked by the machine's NuGet SSL/authentication failure (`NU1301` / no credentials in the security package). A required elevated retry was unavailable because the usage-limit approval path reported that it could be retried after the reset window.
- Debug and Release builds, the full test suite, and the portable package were nevertheless verified using the already cached package assets. Retry the normal restore and the repository's full clean verification workflow after the usage limit resets.
- The `win-x64` package was produced as `artifacts/package-win-x64/RocketIDE-win-x64-code-only.zip`; its SHA-256 sidecar matched the generated archive. The package was not launched.
- WP17's `ROCKET-UPSTREAM-REQUEST` remains an external follow-up for a standalone redistributable debugger backend/source-map contract; no external request was submitted during this pass.

## Automated evidence from this pass

- Debug solution build: 0 warnings, 0 errors.
- Release solution build: 0 warnings, 0 errors.
- Full solution tests: 287 passed, 0 failed, 0 skipped.
- The Windows process output test was repeated 20 times after making its test collector concurrency-safe; all 20 passed.
- Portable ZIP checks: executable present, README present, SHA-256 matched, and 0 PDB files in the default package.
