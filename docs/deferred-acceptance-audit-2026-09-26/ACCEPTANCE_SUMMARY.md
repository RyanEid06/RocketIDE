# RocketIDE deferred acceptance audit summary

Audit completed as a report-only pass on 2026-09-26. The complete row-by-row reconciliation is in DEFERRED_TEST_MATRIX.md; confirmed findings are in BUG_REPORTS.md; next-model repair context is in FIX_HANDOFF.md.

## Result

The audit is **not accepted**. The current baseline gate and package creation pass, but four WP09/WP10 UI failures were reproduced and many historical manual criteria remain not run. A successful automated suite is not GUI acceptance.

| Outcome | Rows |
|---|---:|
| PASS | 26 |
| FAIL | 4 |
| BLOCKED | 3 |
| NOT RUN | 12 |
| NOT APPLICABLE | 2 |
| Total | 47 |

## Verified

- Source branch/ref was synchronized at f0e37d9ee9f14a0e5139de484883e0c985687da1; an isolated audit branch was used.
- scripts/verify.ps1 passed 473/473 tests, 0 failed, 0 skipped, Release build with 0 warnings/errors, and self-contained win-x64 publish.
- scripts/verify-wp05-snippets.ps1 passed all five snippet compiler fixtures.
- scripts/package.ps1 created a portable ZIP with SHA-256 e2540ca1b8b79697313057c5f2a6552326054317ab5bc885eb970a8ac7819f98. The script’s debugger assembly and x64/amd64 EngHost presence checks passed.
- A native RocketIDE session opened a disposable project, initialized rocket-lsp and successfully built the fixture. Direct compiler Check and Run passed.
- Native IDE Check returned exit code 0. Live diagnostics reached Problems and navigated to the malformed fixture location. Go to Definition, Find References, Rename Symbol, dirty-buffer workspace search, replace preview/apply, Quick Open, and Command Palette filtering had passing happy-path checks; limits are detailed per row in the matrix.
- A 4,300,041-byte source file opened in large-file mode, showed Rocket LSP disabled above its 4 MiB document limit, and supported local edit/save. The temporary source file was removed after restoring the edited byte.
- Forced-kill recovery on the hash-identified audit app found one unsaved buffer and restored a unique marker from the disposable project; only the acceptance path is covered at A28.
- Discard/defer recovery, the external-change overwrite warning with No preserving the disk version, and two-group/open-tab restoration passed the tested A29 paths.
- A bounded 252-file generated workspace indexed as 254 files / 2,009 symbols in the UI. Two editor groups retained independent caret positions; a Function snippet inserted and undid successfully. These narrow checks do not close the broader FINAL-WP05 acceptance row.
- Live-debug smoke opened the Debug panel with threads/stack visible; native locals reported unavailable. Full breakpoint/continue/step/packaged-debug workflow remains unverified. A second app process created no second UI window; degradation coverage is incomplete.

## Confirmed bugs

- BUG-001: IDE Run finishes without showing fixture stdout or a Run exit result in Output, even though direct compiler Run prints the expected text and exits 0.
- BUG-002: IDE Test leaves an empty grid and “Test run started…” after a nonzero compiler result and after the toolbar returns to idle.
- BUG-003: Edit → Quick Fixes reports “no server-provided code actions” for an R4002 missing-name diagnostic even though the public declaration is indexed in another project file.
- BUG-004: Edit → Format Document leaves a valid noncanonical disposable source buffer unchanged.

## Blocked acceptance

- Clean Windows x64 launch without .NET and the clean-machine distribution workflow were unavailable.
- Full clean-machine E2E (A38) is also blocked by the missing VM. Physical display DPI/multi-monitor acceptance could not be established from this environment.

## Work not performed

The 12 remaining NOT RUN rows are A27, A31, A33–A37, A40, A42–A43, and A46–A47. A26's bounded large-workspace check passed. A30/A38 clean-machine checks and A32 alternate-display checks remain BLOCKED. A17 Stop and A39 GitHub CI/artifact checks passed in the report-only continuation. See the matrix for each row's evidence and limits.

No product source, tests, scripts, dependencies, CI configuration, or acceptance criteria were modified. Audit reports remain uncommitted in the isolated audit worktree; no production ref or remote repository changed. The package was not launched on a clean machine; this is not a release sign-off.




## Continuation - 2026-09-27

The 12 previously NOT RUN rows were revisited where feasible; partial evidence (including a packaged debugger launch/stop smoke) and concrete remaining environment needs are recorded in the updated matrix and [continuation evidence](evidence/continuation-2026-09-27.md). None completed its full acceptance criteria, so exact totals remain 26 PASS, 4 FAIL, 3 BLOCKED, 12 NOT RUN, 2 NOT APPLICABLE (47). A36 partially observed that the UI closed during a debug-launch sequence while the audit RocketIDE/LSP processes remained responsive; this was not promoted to a confirmed bug without a complete replay. Saved RocketIDE user state was restored and verified with 71 files and zero manifest mismatches. No product/test changes or WP06 work occurred.
