# Repair handoff for a higher model

## Request

Investigate and repair only confirmed UI/system defects BUG-001 and BUG-002 in BUG_REPORTS.md. The user’s current instruction to this audit was report-only, so this audit deliberately made no fixes. Obtain separate authorization before editing product code if the higher-model task does not carry implementation authorization.

Do not treat the skipped historical matrix as an implementation backlog. Reproduce each reported defect first, inspect the existing command/output and test-result contracts, add narrowly scoped regression coverage only after implementation is authorized, and preserve the current 473-test baseline and package guards.

## Source and reproduction

- Audit source SHA: f0e37d9ee9f14a0e5139de484883e0c985687da1.
- Audit branch: codex/rocketide-deferred-acceptance.
- Disposable project: artifacts/deferred-acceptance/fixtures/basic-project.
- Run: click visible Run toolbar control. Repro app SHA is recorded in DEFERRED_TEST_MATRIX.md. Expected direct control: rocketc run <fixture> prints “audit fixture” and exits 0. UI Output remained at older LSP/Build entries, without stdout or a Run exit line.
- Test: invoke Test on the same project, which has no tests directory. Direct control: rocketc test <fixture> --message-format=json emits R5001 and exits 2. IDE controls return to idle while Tests remains empty with “Test run started…”.
- Screenshots: evidence/ui-run-output-missing.png; evidence/ui-test-panel-stuck.png.
- Potential inspection points (not asserted root causes): src/RocketIDE.App/MainWindow.RocketCommands.cs command completion and HandleRocketCommandOutput; src/RocketIDE.App/ViewModels/TestsViewModel.cs BeginRun/Apply; Output buffer and view binding.

## Repair acceptance requested

1. Reproduce both failures on an unchanged build before editing.
2. Verify every terminal outcome is visible and truthful for success, nonzero exit, cancellation, and compiler launch failure; specifically verify stdout and final exit are surfaced for Run.
3. Verify Test leaves a terminal result for a structured compiler diagnostic/nonzero process even when no test-summary message arrives; verify a subsequent test run clears and replaces prior rows/status.
4. Preserve streamed output, diagnostics, cancellation, Stop/process-tree behavior, command availability, and all-or-nothing UI state.
5. Run focused regression coverage and scripts/verify.ps1; rerun the package script with a new unique output root and verify its checksum and debugger asset guards.
6. Do not claim manual release acceptance from unit tests or local packaging. Clean-machine, large-file, recovery, accessibility, full semantic editing, active-shutdown, and live debugger checks remain outside these two repairs and retain their matrix outcomes until separately tested.

