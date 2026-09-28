# Audit progress and provenance

## Scope and rule set

Followed ROCKETIDE_ALL_DEFERRED_ACCEPTANCE_PROMPT.md in full. The audit was restricted to testing and reporting system/UI bugs. No product, compiler, existing-test, script, dependency, CI, or acceptance-criteria fix was made. Disposable fixtures, generated package output, screenshots, and audit reports were written only below artifacts/deferred-acceptance in the isolated audit worktree.

## Preflight

- Production source checkout: RocketIDE; main and origin/main synchronized at f0e37d9ee9f14a0e5139de484883e0c985687da1.
- Audit checkout: C:\Users\Administrator\.codex\worktrees\rocketide-deferred-acceptance\RocketIDE, branch codex/rocketide-deferred-acceptance, based on that SHA.
- Tracked source state was clean before testing. Existing linked worktree statuses were not accessible under this process identity because Git reported dubious ownership; no global Git safe-directory setting was changed.
- A pre-launch copy and SHA/length manifest of the existing %LOCALAPPDATA%\RocketIDE user state was created under user-state-before. This was to permit exact restoration after testing.
- Current verification-built app SHA-256: 946A35B1905FE0C104F984B3B6F54B778B050724C62F6F523374C2370640A853.
- Toolchain identity: rocketc 3.0.0 / AC43B6E2B016A357499B6F62820927A9334CD5C69A67BF8AAD6DD92A386F7D9F; rocket-lsp 1.0.0 / C5986606E98016589E7BF3611DE60964634D7AF3A8171CCD5F83F4064FBDFE1B. The LSP is from a separate Rocket worktree, not the Rocket production branch.

## Executed evidence

- scripts/verify.ps1: 473/473 passed; Release; zero warnings/errors/skips; self-contained win-x64 publish; debugger DLL and x64/amd64 EngHost checks passed.
- scripts/verify-wp05-snippets.ps1: all five fixtures passed.
- scripts/package.ps1: local portable ZIP created with the SHA recorded in ACCEPTANCE_SUMMARY.md. Package guard checked RocketIDE.exe, RocketIDE.Debugger.dll, and EngHost.
- Disposable project basic-project opened in the native UI. LSP reported 3 files / 12 symbols and IDE Build succeeded.
- Direct compiler Check returned success=true / exit 0. Direct compiler Run printed audit fixture / exit 0. Direct compiler Test on the no-tests fixture returned R5001 / exit 2.
- Native UI reproduced missing Run stdout/exit reporting and the Test panel’s stale running summary. Screenshots are evidence/ui-run-output-missing.png and evidence/ui-test-panel-stuck.png.
- Further native checks: IDE Check exited 0; an unsaved malformed buffer produced R4001 in Problems and navigated to line 2; Go to Definition, Find References and the Rename Symbol happy path succeeded; workspace search found both saved occurrences and a unique dirty-buffer marker; replace preview/apply replaced 2 matches in one file and Undo restored the fixture; Quick Open filtered/opened `src/entities.rocket`; Command Palette filtering ran IDE Check. The malformed buffer was discarded and dirty search/replace edits were undone.
- WP12 large-file check: opened a 4,300,041-byte source, observed both large-file notices and LSP exclusion, edited and saved one character, then undid/saved back to the original length and removed the temporary file.
- Closed the second RocketIDE session normally and restored `%LOCALAPPDATA%\RocketIDE` from the original snapshot. Final verification: 71 expected files, 71 actual files, 0 path/length/SHA-256 mismatches.
- Rows not individually exercised are explicitly NOT RUN or BLOCKED in DEFERRED_TEST_MATRIX.md. Historical integration notes were treated as pointers to criteria, not current evidence.

## State and cleanup

- No test or source changes were made. The app session and generated package are isolated to the audit worktree. The existing original workspace smoke file was observed in a separate editor group and was not edited.
- The audit RocketIDE process and the accidentally opened Visual Studio window were closed normally. The pre-launch RocketIDE LocalApplicationData snapshot was restored after the final UI session; all 71 files match by relative path, length, and SHA-256 (0 mismatches). The exact snapshot manifest remains under user-state-before.
- The accidental OS association launch of Visual Studio happened when the full source path was typed into the Open File dialog address bar. No source was opened or edited there; this operator error is excluded from bug findings.
- Package is not deployed, CI was not triggered, no PR/issues/messages were created, and no merge or push was performed.

## Output inventory

- DEFERRED_TEST_MATRIX.md: criteria, row outcomes and historical-source reconciliation.
- BUG_REPORTS.md: reproducible system/UI bugs only.
- ACCEPTANCE_SUMMARY.md: audit disposition and evidence summary.
- FIX_HANDOFF.md: higher-model repair handoff.
- evidence/ui-run-output-missing.png and evidence/ui-test-panel-stuck.png: native UI screenshots.
- fixtures/basic-project: isolated Rocket project used for check/build/run/test.
- package-20260926: portable ZIP and SHA-256.




## Report-only continuation - 2026-09-26

A17 and A39 were freshly executed/queried and marked PASS. A21/A22 reproduced BUG-003/BUG-004. A28/A29 recovery and A26 bounded large-workspace checks passed. Spot checks were also recorded for A33/A43/A46 and A34/A47; their broad acceptance rows remain NOT RUN. A38 moved to BLOCKED because no clean Windows VM is available. Exact evidence and limits are in evidence/closure-2026-09-26.md. Current matrix totals are 26 PASS, 4 FAIL, 3 BLOCKED, 12 NOT RUN, and 2 NOT APPLICABLE. No product or existing test changes were made.

The complete repair prompt for a higher model is HIGHER_MODEL_BUG_FIX_PROMPT.md. The next task is authorized to repair BUG-001 through BUG-004 and verify them; it must not start WP06 implementation. WP06 remains queued behind the four-bug repair/retest gate and a decision on the remaining deferred acceptance rows.



## Deferred acceptance continuation - 2026-09-27

The remaining 12 rows were revisited where feasible. Partial observations for A33/A34/A35/A36/A40/A42/A43/A46/A47 are documented in evidence/continuation-2026-09-27.md and in their matrix rows; A27/A31/A37 retain specific unrun limits; A35 has a packaged launch/stop smoke but not the complete debugger workflow. No row met its full remaining acceptance criteria, so totals stay 26 PASS, 4 FAIL, 3 BLOCKED, 12 NOT RUN, 2 NOT APPLICABLE. %LOCALAPPDATA%\\RocketIDE was restored from the saved snapshot and verified at 71/71 files with zero path/length/SHA-256 mismatches. No product/tests were changed and WP06 was not started. The A36 partial probe closed the UI but left the audit app and LSP responsive; only those path-verified audit processes were stopped.
