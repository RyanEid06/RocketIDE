# RocketIDE deferred acceptance bug reports

Scope: confirmed system and UI behavior only. No fixes were made.

## BUG-001 — Run completes without showing program output or exit result

- Priority: P2
- Area: IDE-WP10 Run / Output
- Classification: UI/system integration defect
- Reproduced: Yes, on the audit-worktree Release build
- App SHA-256: 946A35B1905FE0C104F984B3B6F54B778B050724C62F6F523374C2370640A853
- Compiler: rocketc 3.0.0, SHA-256 AC43B6E2B016A357499B6F62820927A9334CD5C69A67BF8AAD6DD92A386F7D9F
- Fixture: artifacts/deferred-acceptance/fixtures/basic-project

### Reproduction

1. Open the disposable basic-project folder in RocketIDE.
2. Confirm status shows target basic-project and the project LSP indexed 3 files / 12 symbols.
3. Build the project successfully.
4. Click the visible Run toolbar button.
5. Observe the toolbar controls disable during execution, then re-enable.
6. Inspect Output after completion.

### Expected

The Output pane includes program stdout and a terminal command result, because the identical compiler invocation emits program output and exits 0.

### Actual

The IDE Output pane continues to show the previous Rocket Build header, linker command, and build-succeeded entry. It does not show the program’s “audit fixture” output or a Rocket Run exit line. Screenshot: evidence/ui-run-output-missing.png.

### Independent control

Direct invocation against the same fixture:

- Command: rocketc run <fixture>
- Output: built ...main.exe (cache hit); command line for main.exe; audit fixture
- Exit: 0

### Handoff notes

The UI action was confirmed by transient disabled Run/Build controls and active Stop control, followed by normal control restoration. Check the command output/progress and completion path before changing code. Relevant areas to inspect include MainWindow.RocketCommands.cs ExecuteRocketCommandAsync/HandleRocketCommandOutput and the output-buffer presentation. Do not infer the root cause from the current evidence alone.

## BUG-002 — Test panel remains “Test run started…” after a terminal compiler error

- Priority: P2
- Area: IDE-WP10 Test / Tests
- Classification: UI/system integration defect
- Reproduced: Yes, on the audit-worktree Release build
- Fixture: artifacts/deferred-acceptance/fixtures/basic-project; it intentionally has no tests directory

### Reproduction

1. Open the disposable basic-project folder in RocketIDE.
2. Invoke Test for the active target.
3. Wait for the command to finish; the Run/Test/Stop toolbar controls return to idle.
4. Open the TESTS panel.

### Expected

The panel reaches a terminal state that communicates failure and the command’s nonzero exit, or otherwise clearly states that no test result is available. It must not represent a completed command as still running.

### Actual

The grid remains empty and the footer says “Test run started…” after the controls return to idle. This persisted for more than seven seconds after invocation. Screenshot: evidence/ui-test-panel-stuck.png.

### Independent control

Direct invocation against the same fixture:

- Command: rocketc test <fixture> --message-format=json
- Output: structured diagnostic R5001, “package test directory does not exist”
- Exit: 2

The IDE Output view captured for this run also showed only prior LSP/Build entries, not the compiler diagnostic or terminal exit. That output symptom overlaps BUG-001; do not count it as a separate finding.

### Handoff notes

TestsViewModel.BeginRun initializes the footer to “Test run started…” and Apply changes it on test-summary. MainWindow.RocketCommands.cs owns command completion and routes structured output. Inspect how process errors/nonzero exits without a test-summary are presented and how command output reaches the Output view. This report does not prescribe a code change.

