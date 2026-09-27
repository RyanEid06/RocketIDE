# BUG-001–BUG-004 repair checkpoint — 2026-09-27

> Historical record: results, branch names, pending statements and commands below describe this document's original checkpoint. The work was subsequently integrated and released. See [current documentation](../README.md) and [release acceptance](../release-1.0.0/ACCEPTANCE_REPORT.md) for today's status.


All four repaired behaviors passed direct GUI replay in the newly packaged app. The RocketIDE full gate passed **487/487 tests**, zero warnings/errors, publish, and debugger asset guards. This is a local bug-repair checkpoint; historical deferred acceptance and release certification remain incomplete. WP06 was not started.

## Source and dependency identities

| Component | Isolated branch / source commit | Base |
|---|---|---|
| RocketIDE | `codex/rocketide-bug001-004` / `db99bddad43a9b35b29e77c108f7d6d72a7cb63b` | Audited `f0e37d9ee9f14a0e5139de484883e0c985687da1` |
| Companion Rocket LSP | `codex/rocketide-bug003` / `c2268ca1c241c8d20777000364bc1488fa6cb1ee` | Audited fuzzy-LSP source `f1086f3e9f57678a39608a30df4fa73021af701f` |

IDE checkout: `C:\Users\Administrator\.codex\worktrees\rocketide-bug001-004\RocketIDE`.
Companion checkout: `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\rocket-bug003`.
Companion executable: `out\build\bug003\rocket-lsp.exe`, version 1.0.0, SHA-256 `48D7AAB7F9D34D08323228B7485E2942DB2309C9EDE918D595B5FFAACC1E184D`. Built Release with MSVC, LLVM disabled, Ninja, `-j2`; pinned local raylib source was copied into the isolated checkout. No existing Rocket lane was modified.

Compiler used throughout: `C:\Users\Administrator\Desktop\Projects\Rocket\out\w\lsp\out\build\windows-release\rocketc.exe`, version 3.0.0, SHA-256 `AC43B6E2B016A357499B6F62820927A9334CD5C69A67BF8AAD6DD92A386F7D9F`; its unchanged lane HEAD is `1ad63fea58abecaea632ec6ab57e741fbf165c1b`. That HEAD identifies the inspected checkout; this task did not rebuild the compiler or independently establish its build provenance.

Unchanged audit LSP: `C:\Users\Administrator\Desktop\Projects\Rocket\out\worktrees\rocket-lsp-fuzzy-symbols\out\build\lsp-symbols-stage0\rocket-lsp.exe`, SHA-256 `C5986606E98016589E7BF3611DE60964634D7AF3A8171CCD5F83F4064FBDFE1B`.

## Findings, fixes, and observed replay

| Bug | Fresh unchanged-build evidence and failure boundary | Repair and packaged GUI result |
|---|---|---|
| BUG-001 / A15 | Run stdout and exit result existed below the Output viewport; scrolling exposed both. The process transport had delivered them. | `Views/OutputListBox.cs` and `MainWindow.xaml` follow streamed batches after layout, retaining bounded history. After Build then Run, **audit fixture** and **Rocket Run exited with code 0.** were directly visible without scrolling. |
| BUG-002 / A16 | No-tests fixture returned R5001 / exit 2 while Tests still said **Test run started…**. | `TestsViewModel.cs` finalizes every run truthfully, preserves compiler errors, marks unfinished rows, and clears prior state at BeginRun. `MainWindow.RocketCommands.cs` drains pending message presentation before completion and always releases running state. Completed task references are removed during streaming. Packaged Test showed **Test process exited with code 2. No test summary received. R5001: package test directory does not exist…** with idle controls; a second invocation also completed. The debugger caller was adjusted to the renamed async routing method only. |
| BUG-003 / A21 | **The historic initial “no action” symptom did not reproduce.** Fresh unchanged GUI offered `Import math`. Applying it left an unresolved call; protocol replay proved the action inserted only an import. It also used `math` where the package requires `src.math`. | Companion `src/language_server.cpp` preserves resolved dependency aliases, derives package-relative names for unqualified declarations, inserts a missing import, and qualifies the exact diagnostic token. Existing imports get qualification only; stale ranges and non-file requests are rejected. Packaged GUI offered **Import src.math — rocket-lsp**, applied `import src.math` plus `return src.math.doubled(21)`, and the Problems count cleared. Original fixture remained unsaved and Undo restored it. This establishes the repaired end-to-end behavior, not a proven explanation of the old initial-menu failure. |
| BUG-004 / A22 | Exact LSP did not advertise standard document formatting; that request returned -32601. It advertised and returned `source.format.rocket` instead. Original Format Document left noncanonical valid source unchanged. | Capabilities, `NavigationClient`, session coordinator, and explicit Format command now use a narrowly advertised edit-only fallback; standard formatting remains preferred. Explicit formatting flushes pending document changes and retains snapshot checks. Only a single safe same-document action is accepted. GUI `Shift+Alt+F` changed `pub fn doubled(value:Int)->Int:` / `return value*2` to canonical spacing, left the tab unsaved, and Undo restored the noncanonical text. |

Baseline and replay screenshots/accessibility trees are in [evidence](evidence). Quick Fix was tested with the A21 fixture at `RocketIDE-Build\audit-closure\A21-quickfix`; Run/Test used the original audit `artifacts\deferred-acceptance\fixtures\basic-project`.

## Regression and gate evidence

- Unchanged IDE baseline: **473/473**. The initial five repair regressions failed before implementation, then passed. Expanded focused coverage: **14/14**. Tests cover actual WPF viewport tailing/history, R5001/nonzero exit, missing summary without invented success, cancellation, launch failure, unfinished rows, rerun reset, standard-format preference, advertised-capability gating, unsafe actions and cancellation.
- Final `scripts/verify.ps1`: **Core 40 + Rocket 143 + Debugger 22 + Infrastructure 63 + App 219 = 487**, zero failures/skips, **0 warnings / 0 errors**. Publish and `RocketIDE.Debugger.dll` / x64 `amd64/EngHost.exe` guards passed. See [final-verify.log](evidence/final-verify.log).
- Companion C++ missing-import regression failed on original source and passed after repair. Added nested-package, existing-import, and non-file URI cases in `tests/language_server_tests.cpp`. Real stdio replay applies returned edits, verifies diagnostics clear, rejects stale ranges, and verifies existing-import qualification; [red](evidence/quickfix-protocol-red.log), [green](evidence/quickfix-protocol-green.log), [replay script](evidence/verify_quickfix.py), and [transcript](evidence/verify_quickfix.json).
- Companion focused CTest: **5/5** (`language_server`, `analysis_queue`, `language_server_analysis`, `workspace_state`, `formatter`). Broader six-test run: **5/6**, with `language_server_incremental` failing. Rebuilding that test against unchanged audited source reproduced **the same 32 failing assertions**; final and baseline failure lists are identical. These comparisons include generation-bearing workspace-symbol responses; the unrelated inherited gate was not weakened or fixed. See [baseline comparison](evidence/incremental-baseline-comparison.json) and both complete logs. No full compiler/ASAN/release gate is claimed.
- Independent read-only review caught task retention, non-file URI handling, and dependency-alias preservation; all were corrected and the reviewer confirmed no outstanding actionable findings.

## Portable package

New package: `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\bug-repair-2026-09-27\package-reviewed\RocketIDE-win-x64-bug001-004-reviewed-20260927.zip` (**138,407,377 bytes**).

SHA-256: **`40292447c03a0b3f197bce326eb025cca1a8ed8afeea50c22cd5df5194f75115`**.

GUI replay used its extracted `replay-reviewed\RocketIDE-win-x64-bug001-004-reviewed-20260927\RocketIDE.exe`. Required nonempty assets: EXE 287,744 bytes, Debugger DLL 78,848 bytes, x64 EngHost 42,808 bytes. Full hashes are in [reviewed-package-assets.json](evidence/reviewed-package-assets.json). The EXE apphost hash is unchanged from the audit; the **product DLL** hash identifies the rebuilt app: `480C731343F4552D230C3DCAAA845893CD1483D5E315AB15350AE79CDE3B0B6A`.

The portable package does not bundle a Rocket SDK. Replaying BUG-003 requires selecting the companion LSP above in Rocket SDK Settings. The user's original SDK selection was restored after testing. Integrating only the IDE commit will not deliver the Quick Fix repair.

## Preservation and remaining scope

All **71 original profile files** were restored with matching SHA-256 hashes and exact file count; all **10 audit evidence files** matched their pre-task hashes. All three exercised fixture source files matched their pre-replay hashes; A21 main also retains the recorded audit hash. No source edit was saved during GUI replay. Temporary profile/logs and originals remain backed up locally outside Git. The audit worktree remains at `d2e82e59a40c5296ca53c4ddf70a2d5edd210536` with its original uncommitted audit changes; original compiler and fuzzy-LSP lanes remain clean at their recorded SHAs. See [preservation-results.json](evidence/preservation-results.json).

No push, merge, release, CI trigger, public issue, external message, or WP06 implementation occurred. Clean-machine, alternate-DPI, full accessibility, broad stress, complete debugger, and all unrelated matrix rows retain their previous outcomes. Stop/process-tree and cancellation behavior are covered by the unchanged full suite and focused state tests; no new broad GUI stop/debugger certification is claimed.

**WP06 recommendation: hold.** The four repair replays and IDE gate pass, but review/integration must account for both repositories and the inherited companion incremental-test gate before advancing. The original audit matrix is deliberately unchanged.
