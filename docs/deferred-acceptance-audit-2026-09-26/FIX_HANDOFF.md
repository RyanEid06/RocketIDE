# Repair handoff for a higher model

## Request

The user has authorized a separate higher-model task to investigate, repair, and verify all four confirmed UI/system defects BUG-001 through BUG-004. The complete implementation prompt is [HIGHER_MODEL_BUG_FIX_PROMPT.md](HIGHER_MODEL_BUG_FIX_PROMPT.md). This audit itself remained report-only and made no product fixes.

Do not treat the skipped historical matrix as an implementation backlog. Reproduce each reported defect first, inspect the existing command/output and test-result contracts, add narrowly scoped regression coverage only after implementation is authorized, and preserve the current 473-test baseline and package guards.

## Source and reproduction

- Audit source SHA: f0e37d9ee9f14a0e5139de484883e0c985687da1.
- Audit branch: codex/rocketide-deferred-acceptance.
- Disposable project: artifacts/deferred-acceptance/fixtures/basic-project.
- Run: click visible Run toolbar control. Repro app SHA is recorded in DEFERRED_TEST_MATRIX.md. Expected direct control: rocketc run <fixture> prints “audit fixture” and exits 0. UI Output remained at older LSP/Build entries, without stdout or a Run exit line.
- Test: invoke Test on the same project, which has no tests directory. Direct control: rocketc test <fixture> --message-format=json emits R5001 and exits 2. IDE controls return to idle while Tests remains empty with “Test run started…”.
- Screenshots: evidence/ui-run-output-missing.png; evidence/ui-test-panel-stuck.png.
- BUG-003 reproduction: open the disposable A21 fixture at `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\audit-closure\A21-quickfix` in the exact app/LSP versions in DEFERRED_TEST_MATRIX.md. With indexed `math.rocket` exporting `doubled` and R4002 on `main.rocket`, Edit → Quick Fixes reports “LSP: no server-provided code actions.” Inspect request/context, server response, and UI filtering; the failing boundary is unlocalized.
- BUG-004 reproduction: in that same disposable project, enter valid noncanonical `pub fn doubled(value:Int)->Int:` / `return value*2` in `math.rocket`, invoke Edit → Format Document, and observe that neither line changes. The buffer remains dirty; do not save the audit edit. Inspect the formatting request, server response, and application of edits.
- Potential inspection points (not asserted root causes): src/RocketIDE.App/MainWindow.RocketCommands.cs command completion and HandleRocketCommandOutput; src/RocketIDE.App/ViewModels/TestsViewModel.cs BeginRun/Apply; Output buffer and view binding.

## Repair acceptance requested

1. Reproduce all four reported bugs on an unchanged build before editing.
2. Verify every terminal outcome is visible and truthful for success, nonzero exit, cancellation, and compiler launch failure; specifically verify stdout and final exit are surfaced for Run.
3. Verify Test leaves a terminal result for a structured compiler diagnostic/nonzero process even when no test-summary message arrives; verify a subsequent test run clears and replaces prior rows/status.
4. Preserve streamed output, diagnostics, cancellation, Stop/process-tree behavior, command availability, and all-or-nothing UI state.
5. Run focused regression coverage and scripts/verify.ps1; rerun the package script with a new unique output root and verify its checksum and debugger asset guards.
6. Do not claim manual release acceptance from unit tests or local packaging. Clean-machine, alternate-DPI, accessibility, full semantic editing, active-shutdown, and complete live debugger checks remain outside these repairs and retain their matrix outcomes until separately tested.

## Short kickoff prompt

“Follow `C:\Users\Administrator\.codex\worktrees\rocketide-deferred-acceptance\RocketIDE\docs\deferred-acceptance-audit-2026-09-26\HIGHER_MODEL_BUG_FIX_PROMPT.md` in full. Fix and verify BUG-001 through BUG-004; keep the deferred audit evidence and WP01 lanes intact. Do not start WP06 implementation.”



## Audit closure status update - 2026-09-26

A17 Stop, A26 bounded large-workspace indexing, A28/A29 recovery and layout flows, and A39 CI/artifact metadata now PASS. A15/A16/A21/A22 FAIL and are tracked as BUG-001 through BUG-004. See the coverage matrix and evidence/closure-2026-09-26.md. Twelve acceptance rows remain NOT RUN; A30/A32/A38 are BLOCKED; A44/A45 remain scope exclusions. The next task is authorized to repair all four bugs. Do not treat bug repair as complete historical acceptance or release certification. WP06 is queued behind the repair/retest gate and a decision on the remaining deferred rows.



## Deferred acceptance continuation - 2026-09-27

The follow-up audit did not change BUG-001 through BUG-004 or their repair scope. It recorded partial, incomplete observations for A33/A34/A35/A36/A40/A42/A43/A46/A47; all 12 remaining rows stay NOT RUN pending completion of their full acceptance criteria. See DEFERRED_TEST_MATRIX.md and evidence/continuation-2026-09-27.md for row evidence, limitations, and environment needs. A36's UI-close probe left the audit executable and LSP responsive and should be replayed independently before opening a separate shutdown finding. The saved RocketIDE user profile was restored and manifest-verified. Do not start WP06 until the existing bug repair/retest gate and deferred-row decision are resolved.
