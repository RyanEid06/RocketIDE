# FINAL-WP02–WP05 RocketIDE integration evidence

**Status: WP02–WP05 merged and pushed to RocketIDE production `main`.** The verified integration branch was fast-forwarded without rewriting any source history. Published acceptance commit: `f5fb2d0c6e7c4dea9b0c451ea0f8ce4c2fdeca3b`; its code is `c374cf86bd1976a610afecf723f4c6bf4c4c3779` plus evidence only. The final gate passed 473 tests, zero warnings/errors and x64 publish. Signature ordering, recovery, supported signature GUI and representative active-work shutdown have been verified. The support limitations below remain explicit. This final documentation follow-up changes no product code. No WP06 work was started.

## Current candidate, 2026-09-26

### Branches and toolchain

- RocketIDE production is `main`, verified live at `062b78fe2d12675b86fd44ccf3c0e24e98b81d01`; origin is `https://github.com/RyanEid06/RocketIDE.git`. The source WP histories and worktrees remain preserved. Integration merge ancestry is recorded below.
- Symbol contract/ranking fix: `bfbb690`; caret crash/completion routing fix: `7a6663b`. Earlier integration head was `8439fb0`. View restoration fix: `7d9c41b`. Shutdown recovery and save-affinity fix: `e3590b086efe5ce0596db4ca7b0140cfa3442a0c`.
- The user explicitly authorized a narrow Rocket LSP repair on a separate branch. Rocket `codex/rocket-lsp-fuzzy-symbols` at `f1086f3e9f57678a39608a30df4fa73021af701f` contains fixes `c57de76` and `f1086f3` atop WP01 tip `1ad63fea58abecaea632ec6ab57e741fbf165c1b`. WP01 and its worktree were unchanged. Rocket master remains `10f295dd000b93fe50d254be21fedffd17892aeb`. No Rocket push or production merge occurred.
- Compiler: `Rocket/out/w/lsp/out/build/windows-release/rocketc.exe`, version 3.0.0, SHA-256 `AC43B6E2B016A357499B6F62820927A9334CD5C69A67BF8AAD6DD92A386F7D9F`.
- LSP: `Rocket/out/worktrees/rocket-lsp-fuzzy-symbols/out/build/lsp-symbols-stage0/rocket-lsp.exe`, version 1.0.0, SHA-256 `C5986606E98016589E7BF3611DE60964634D7AF3A8171CCD5F83F4064FBDFE1B`. This build includes WP01 document symbols/folding and the explicit bounded fuzzy contract. The version string alone does not distinguish it from older incompatible binaries.
- The user saved these SDK paths; persistence and an online session were observed after relaunch. Workspace output trust remained unchecked.

### Confirmed defects and repairs

- Production folding was unwired; each editor view now owns a controller/manager fed by authoritative LSP ranges. Coordinates are restored only against matching current ranges. Same-version split requests, stale sessions/versions, disposal and cancellation are covered.
- The LSP forced-stop race marked completion before the process-tree kill finished; deterministic concurrent regression failed before the fix and passed after serialization.
- The old server omitted `plctrl` matches and arbitrarily limited results to 1,024. The authorized server fix searches its complete index with bounded deterministic top-200 selection and snapshot generations. Rocket's focused `language_server` test was 1/1 baseline, failed the new regression before repair, and passed 1/1 after repair.
- RocketIDE requires `experimental.rocketWorkspaceSymbolSearch` version 1, maxResults 200, generation `rocket/projectStatus`. Unsupported servers report an explicit error. The parser rejects oversized, null, missing-generation and mixed-generation responses. Navigation rechecks generation/workspace/session after selection.
- Client display-path reranking could put `PlayerController` ahead of exact `Player`. A failing regression confirmed it; remote results now retain server order. File symbols still use local fuzzy filtering of authoritative document symbols.
- GUI Tab at the end of a buffer crashed with offset 12 on length 8: AvalonEdit had already moved the caret before an additional relative update. Tab/outdent and pair deletion now set absolute saved offsets. Both Tab and pair-deletion regressions failed before repair; focused EditorKeyBehavior tests passed 7/7. Modified shortcuts are left to their owners.
- Completion Tab/Enter was intercepted by generic indentation/newline handling. An open completion popup now receives those keys. Selected snippet acceptance was visibly replayed with Tab in the upper group and Enter in the lower group, with `$0` and one-step undo.
- **Review repair verified:** requested shutdown saves could fail/timeout while final cleanup still deleted recovery. The exit now checkpoints the requested buffers before saving, serializes periodic/exit recovery writes, retains the checkpoint on failure, and marks the session unclean. Shutdown conflicts/errors preserve recovery without opening an unbounded nested modal. Focused coordinator tests passed 3/3.
- **Review repair verified:** attaching/restoring a host synchronously reported intermediate caret events into the stored view, replacing selection/scroll. A real host reproduction showed selection 3,6 becoming 9,0. The host now suppresses state reports during restoration and restores selection before caret.
- **Review repair verified:** signature requests raced the 75 ms document-change debounce. Commit `c374cf8` adds a per-document flush before signature requests, preserving the existing transport/path gate; caller cancellation does not drop the scheduled change. Three additional regressions cover promotion/transport completion, cancelled waits and didChange-before-signature ordering. Only signature requests flush; generic feature flushing would deadlock the format-on-save path that already owns the document gate. Review approved this scope.

### Current automated and package evidence

- Environment: Windows, .NET SDK 10.0.401. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1` passed Release with 0 warnings/errors; Core 40, Rocket 143, Debugger 22, Infrastructure 63, App 205 = **473/473**; win-x64 publish succeeded. This includes all current code repairs through `c374cf8`. The production ref was fetched again and remains an ancestor with no production-only commits.
- Initial sandboxed NuGet restore errors (`NU1301`/`NU1900`, TLS authentication) were resolved by rerunning unchanged with normal NuGet access.
- `scripts/package.ps1 -OutputRoot artifacts/integration-wp02-wp05-package -Version integration-wp02-wp05` succeeded. ZIP SHA-256 independently verified: `e2ecf7fa9226368ce55e6c72b7d5b983ad7a6f55a26e3370921fbd9e11d0d2bf`. 834 entries; RocketIDE.exe 287,744 bytes, RocketIDE.Debugger.dll 78,848, amd64/EngHost.exe 42,808, runtime configuration present. Both selected executables are PE machine 0x8664 (x64); the debugger dependency also ships x86/arm64 helpers. Repackaged after the signature repair; hash and contents independently verified.
- `scripts/verify-wp05-snippets.ps1` passed all five compiler fixtures: main, fn, impl, match, testmain.
- `git diff --check` passed after the review repairs.

### Review and recovery replay

- Independent review confirmed save-affinity failure: `ConfigureAwait(false)` in save orchestration resumed UI-owned AvalonEdit mutation on a worker thread. A real STA Dispatcher/AvalonEdit regression failed before repair and passed after SaveAsync retained its caller context. Queued change processing remains asynchronous. Reviewer approved the narrow fix; no further important finding in the reviewed paths.
- Focused scheduler/recovery/production-host tests passed 10/10. Host test covers independent caret/selection, backward selection, measured scroll extent/offsets and rebinding; it failed before repair and passed afterward.
- Deliberate generated-file conflict GUI replay on the final publish: selected Save at exit with dirty split text and changed disk content. Process exited 242 ms after the Save decision. Disk retained external text; recovery JSON contained the exact unsaved editor text; session cleanShutdown was false. Relaunch offered recovery with a conflict warning; Yes restored the text in both horizontal views. Normal save then displayed the overwrite decision, succeeded after confirmation, made both views clean and removed recovery.json. No new crash log appeared.

### Real protocol, latency, cancellation and memory

- Ignored harness: `artifacts/lsp-smoke/LspSmoke.csproj`, using production RocketIDE clients. Separate generated fixtures avoid editing user source.
- 1,200-extra-struct fixture: initialize 70 ms; initial empty workspace query exactly 200 in 1,937 ms including indexing; Player 2 in 5 ms; plctrl 2 in 5 ms. Symbol generation 2 matched project status; after didChange, generation 3 matched. Document symbols 1,204 in 144 ms; folding 1,204 in 5 ms; stop 8 ms.
- Active cancellation run: initialize 148 ms; active symbol query cancelled in 68 ms; subsequent empty query 200 in 3,159 ms including indexing; Player/plctrl 5/4 ms. Generations remained consistent after revision. Document symbols 1,204 in 150 ms, folds 1,204 in 8 ms, stop 10 ms.
- 100 alternating symbol queries took 725 ms; server private bytes changed from 25,067,520 to 25,464,832; peak working set 42,086,400. This short run is not a leak proof or long-duration stress test.
- Separate shutdown during an active symbol request took 7 ms; pending request ended with JsonRpcResponseException, harness exit 0.
- Quick Open production benchmark: 2,391 visible files, 46 ms cold/30 ms warm; bounded result queries 4–6 ms; result cap 250, scan cap 10,000; generated directories excluded and precancelled query cancelled. UI saturation/large remote filesystem performance is not established by this local measurement.
- Two views share one logical synchronization subscription and one semantic client/delta cache/gate. Per-view controllers can issue separate semantic-token presentation requests; the client does not promise equal-version request deduplication. This does not send duplicate logical didChange notifications or introduce IDE semantic analysis. No competing parser or semantic source scan was introduced. Full server recomputation profiling remains unmeasured; no speculative optimization was made.
- Real lifecycle harness extensions reused the production client with separate 1,200-symbol fixtures: stop while initialization was still pending took 19 ms; stop with workspace symbols, document symbols, folding, completion and semantic-token requests all still pending took 6 ms. All pending requests settled with JsonRpcResponseException; harness exit 0. These are protocol checks, not GUI timings.

### Fresh GUI observations

All observations used the candidate publish in the ignored RocketIDE smoke workspace, never a protected Rocket workspace.

| Flow | Observation |
| --- | --- |
| Launch, SDK, workspace/file open | PASS; persisted toolchain paths, LSP online, source in Explorer/editor |
| Quick Open | PASS; one-file fixture listed and Open navigated |
| Workspace symbols | PASS; empty query 200; plctrl returned PlayerController and field; fresh selection navigated; stale generation rejected with search-again message |
| File symbols and history | PASS; main selected at line 1, Alt+Left returned line 3 and Alt+Right restored line 1 |
| Outline | PASS; displayed authoritative 1,203 top-level entries; a later small-fixture double-click selected main in the editor |
| Command Palette | PASS; 42 commands, Check Rocket target routed and succeeded with exit 0 |
| Split views | PASS vertical and horizontal; same-file shared edits/dirty state; independent caret, selection, scroll and collapsed fold; closing dirty secondary retained text in primary |
| Folding | PASS authoritative markers; collapsing one view did not collapse the other |
| Snippets/completion | PASS menu insertion, selected completion Tab/Enter in both groups, `$0`, single undo; modifier policy has automated coverage; OS Windows-key shortcut not driven by automation |
| Diagnostics | PASS; temporary invalid text produced live diagnostics shared across views; undo restored valid text |
| Semantic colors | Visible in both views; semantic-token provenance not separately isolated from syntax coloring |
| Format on Save | Unsupported formatter fallback observed: saving without formatting, both views clean and disk correct; transactional/concurrent edits covered by tests, real formatter not advertised |
| Debugger markers | PASS F9 line marker visible in both views; program built/launched and printed Hello, Rocket!; source breakpoint/locals NOT accepted: stopped in native exit code with missing private-symbol locals |
| Recovery | PASS conflict replay restored exact unsaved shared text and horizontal layout; subsequent launch also visibly restored distinct selections. Selection/scroll rebinding has a real production-host regression; large-scroll restart was not separately replayed |
| Active shutdown | PASS representative GUI routes: Run 243 ms, Test 183 ms, dirty split/snippet Save 402 ms; owned processes gone and sessions clean. Earlier debugger/LSP close 637 ms; failed-save conflict 242 ms with recovery deliberately retained |
| Word wrap and hover | PASS; long line wrapped in both views when focused; authoritative print hover tooltip observed in both views |
| Signature help | PASS supported complete-call trigger in both views: inserting the missing comma in add(1 2) produced the authoritative add signature tooltip. Empty print() remains an upstream limitation: raw protocol returns no signatures even after synchronized didChange and a delay |

Format-on-save and wordWrap were restored to false after their smoke tests. The old LspSmoke.exe dialog was a harness exception against the earlier incompatible master-based server; later harness runs exited normally. The actual editor caret crash is separately identified and fixed above.

### Active-work shutdown detail and limits

- Run: finite fixture paced by the Windows Sleep API (50 ms, at most 1,000 lines) kept both rocketc and its child running. Close input UTC ms 1790438828832; process exit 1790438829075: **243 ms**. IDE, LSP, compiler and program PIDs were all absent afterward; session cleanShutdown true.
- Test: UI showed TESTS (1), RUNNING, with compiler and target processes present. Close input 1790438898910; exit 1790438899093: **183 ms**. All four owned PIDs were absent afterward.
- Active snippet: Function snippet inserted in the lower view with first placeholder selected and shared dirty text. Exit Save decision 1790439245256; exit 1790439245658: **402 ms**. Exact snippet text persisted; cleanShutdown true; IDE/LSP absent; no new crash log.
- Heavy output: a finite 10-million-line fixture completed, the output view remained usable, and the IDE working set was 306,724,864 bytes afterward. The run finished before the close observation, so this is output-load evidence, not shutdown-under-heavy-output evidence. Automatic approval review rejected a proposed billion-iteration increase as unnecessarily resource intensive; it was not executed. The safer paced fixture above verified active process-tree teardown.
- Bounded output-active follow-up: 100-line bursts with a 50 ms sleep and an overall 100,000-line cap. Output was visibly flowing, Run was disabled/Stop enabled, compiler and target were live, and IDE working set was 271,970,304 bytes. Close input 1790439533167; exit 1790439533346: **179 ms**. All four owned PIDs disappeared; session cleanShutdown true. This safely exercises output draining and process teardown while output is active.
- Startup/analysis and concurrent feature requests were checked with the real protocol harness above. Format-on-Save is not advertised by this LSP, so real formatter-active exit is unsupported; transactional/concurrent-save and timeout behavior are exercised by the automated scheduler/lifetime tests. These representative observations do not claim every possible operation overlap or a synthetic OS/native hang was manually reproduced.

### Reconciliation and policy

- Fresh fetch and live symref on 2026-09-26 still identify main at `062b78fe2d12675b86fd44ccf3c0e24e98b81d01`; production-only commits: zero. The code candidate is 17 commits ahead. No new semantic reconciliation is needed.
- GitHub branch metadata: main protected=false, required checks empty, active branch rules empty; merge commits allowed. Repository CONTRIBUTING permits the recorded full gate on a real Windows machine as acceptance evidence. No mandatory pull-request policy was found.
- All protected/source worktrees remained clean at the final status check. Rocket fix and WP01 SHAs remain as recorded above.
- Final independent review of `c374cf8` reported no remaining known critical/high blocker and recommended no further code change. Review explicitly retained the same-version semantic request and non-exhaustive shutdown measurement limitations above.
- Final pre-merge gate on `f5fb2d0` again passed **473/473**, Release 0 warnings/errors and x64 publish. Candidate tree: `6a993936349a624f8b5bd9fb834e49ba1d1a7a3b`.
- Production was fast-forwarded from `062b78f` to `f5fb2d0`. Initial atomic push returned HTTP 408; a live-ref check proved no remote change. Retrying with HTTP/1.1 succeeded. Fresh fetch/live checks proved local main, origin/main and the live main SHA all equalled `f5fb2d0c6e7c4dea9b0c451ea0f8ce4c2fdeca3b`, ahead/behind **0/0**. The integration branch was published at the same SHA. All source branches/worktrees remain preserved; Rocket was not pushed.
- Final evidence-only follow-up, post-merge verification, package provenance and final remote SHA synchronization are reported in the task completion record so this document need not contain a self-referential commit hash.

### Remaining limitations and final integration steps

- Unsupported real formatting, empty-call signature behavior, native source-debugger locals, isolated semantic-color provenance, exhaustive overlap shutdown and full duplicate-work profiling remain limited as explicitly described above. Supported WP02–WP05 routing and the confirmed integration defects have been checked; no known critical/high integration defect remains from the completed reviews.
- The fixed LSP remains a separately branched toolchain dependency. Rocket production and WP01 are unchanged; the task forbids pushing Rocket.
- The published integration is the baseline for the next separately authorized work package, subject to the explicit upstream/tool-support limitations above. No next package was begun.

## Historical pre-fix evidence — NOT CURRENT ACCEPTANCE EVIDENCE

## Scope and preflight

- Repository: `C:\Users\Administrator\Desktop\Projects\RocketIDE`; `origin` is `https://github.com/RyanEid06/RocketIDE.git`. Live `origin/HEAD` identifies `main` as the production branch. The separate Rocket repository and Rocket FINAL-WP01A/B were excluded.
- The read-only preflight found all four registered worktrees clean, including the root WP03 checkout, the in-repo WP02 checkout, and the external WP03 and WP05 checkouts. The existing stash was preserved. All six relevant local branches matched their tracking and live remote SHAs with ahead/behind `0/0` at preflight.
- Preflight SHAs: `main` `062b78fe2d12675b86fd44ccf3c0e24e98b81d01`; `codex/rocketide-final-wp02-wp07` `8013d109632687687ca99612cdde881421aa192c`; `codex/rocketide-final-wp345-seam` `63c462a22f63e84e3ececde4a447f704b48c639f`; WP03 `23fbb86c0f948fef45bdc7fda2f45c1c6fc187ef`; WP04 `e4c1f1dcecccf0d223ee7468e3a0563a706e190f`; WP05 `6561264e5acc820a8560f65fca2d36b889be3a5f`.
- Ancestry showed the `wp02-wp07` branch contains one WP02-only lifecycle commit after `main`; no WP06/WP07 implementation was included. The seam descends from it. WP03 and WP04 each add one commit from that seam; WP05 adds two commits.

## Baselines and merge ancestry

| Source | SHA | `scripts/verify.ps1` baseline |
| --- | --- | --- |
| WP02 (`wp02-wp07`) | `8013d10` | Release build 0 warnings/errors; 370/370 tests; publish assets present |
| WP03 | `23fbb86` | First full run: build passed, but the hung fake-LSP shutdown test failed once (`ForcedKillCount` 0 versus 1). The isolated test passed 8 reruns; a second unchanged full run passed 390/390, 0 warnings/errors, publish assets present. The original failure remains part of baseline evidence. |
| WP04 | `e4c1f1` | Release build 0 warnings/errors; 417/417 tests; publish assets present |
| WP05 | `6561264` | Release build 0 warnings/errors; 395/395 tests; publish assets present |

The dedicated branch `codex/rocketide-final-wp02-wp05-integration` started at `main` and preserved published source history. Merge commits: `438fee0` seam/WP02, `2fa93aa` WP03, `728301b` WP04, and `744d8fd` WP05. The WP04 merge reconciled `MainWindow.xaml`, retaining WP03 command routes and WP04 editor controls. The WP05 merge reconciled `MainWindow.Debugger.cs`, `MainWindow.RocketIntegration.cs`, `MainWindow.xaml`, and `MainWindow.xaml.cs`, preserving WP03 navigation/command routing, WP04 Quick Open, and WP05 split editing/snippets. The auto-merged `MainWindowViewModel.cs` was reviewed. No source branch or worktree was rewritten or deleted.

## Confirmed integration fixes

- WP04's folding controller was unreachable from the production editor host. Each WP05 editor view now wires the typed LSP folding provider into its own AvalonEdit folding controller and manager. Per-view collapsed state is captured for recovery; only exact coordinates from current authoritative ranges are restored. The provider now allows simultaneous same-version split-view requests, rejects stale version/session responses, and observes application shutdown.
- The WP02 force-stop path marked a client as stopped before its synchronous process-tree kill completed. A deterministic concurrent test failed before the fix and passed after serializing completion; the focused shutdown tests passed 3/3.
- The WP03 workspace-symbol parser previously silently truncated responses at 1,024 entries. It now reports an incomplete-response error at that bound, and the picker displays the failure. File and workspace symbol navigation now checks document/workspace and LSP-session freshness after selection. These safeguards do **not** prove complete fuzzy search when the server filters by substring or truncates below the client bound.

## Integrated automated acceptance

- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1` on the current source: .NET SDK 10.0.401; Release build succeeded with **0 warnings and 0 errors**; Core 40/40, Rocket 141/141, Debugger 22/22, Infrastructure 63/63, App 194/194, **460/460 total**. Self-contained win-x64 publish passed with nonempty `RocketIDE.exe` and `amd64/EngHost.exe`.
- The first sandboxed restore failed with NuGet `NU1301`/TLS authentication errors. The unchanged gate passed with normal NuGet access. This was an environment restore failure, not a test failure.
- `git diff --check` passed on the source changes.
- `scripts/package.ps1 -OutputRoot artifacts/integration-wp02-wp05-package -Version integration-wp02-wp05` passed. ZIP: `artifacts/integration-wp02-wp05-package/RocketIDE-win-x64-integration-wp02-wp05.zip`; 834 entries; SHA-256 `b94766790facd118acd252229bfd8fc5fe453b1181675cfa2cb1271283e5e3b8`, independently matched against the `.sha256` file. The ZIP contains `RocketIDE.exe` (287,744 bytes), `RocketIDE.Debugger.dll` (78,848 bytes), `amd64/EngHost.exe` (42,808 bytes), and `RocketIDE.runtimeconfig.json`. Both executables have PE machine `0x8664` (x64).

## Outstanding acceptance and blockers

- **Authoritative fuzzy workspace-symbol completeness: FAIL / confirmed.** After the user directed a binary-only check in the Rocket project folder, the newer Release binaries were located at `C:\Users\Administrator\Desktop\Projects\Rocket\out\w\lsp\out\build\windows-release` (`rocketc 3.0.0`, `rocket-lsp 1.0.0`). A real LSP session against a five-symbol RocketIDE fixture returned `PlayerController` for empty and `Player` queries, but **zero results for `plctrl`**. A second RocketIDE fixture with 1,200 extra structs returned **exactly 1,024** raw symbols for an empty query while `textDocument/documentSymbol` returned 1,203 top-level symbols; the empty response began at `Symbol0331`, omitting earlier candidates. The 1,024-entry server cap confirms that retrieving the empty-query prefix and ranking it in the IDE cannot guarantee completeness. A bounded, complete selection contract from Rocket's authoritative symbol index is required; no IDE-side parser or source scan was added. This is a known high-severity WP03 acceptance gap and blocks the production merge.
- **Rocket toolchain smoke: PARTIAL PASS.** With `ROCKET_COMPILER` set to the current `rocketc 3.0.0` binary, `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-wp05-snippets.ps1` passed all five `check --message-format=json` fixtures (`main`, `fn`, `impl`, `match`, and `testmain`). The compiler SHA-256 was `AC43B6E2B016A357499B6F62820927A9334CD5C69A67BF8AAD6DD92A386F7D9F`; the `rocket-lsp 1.0.0` binary SHA-256 was `258FC9ABD906F542D420423B2B7812A9F2AA745C6A69C57F815D60910F6F21FB`. A one-off client at ignored `artifacts/lsp-smoke/LspSmoke.csproj`, using RocketIDE's `RocketLanguageClient`, `SymbolClient`, and `FoldingClient`, was run with `dotnet run --project artifacts/lsp-smoke/LspSmoke.csproj --configuration Release -- <rocket-lsp.exe> artifacts/lsp-smoke-workspace` and then with an added `1200` argument. The small fixture completed initialize in 79 ms, `workspace/symbol` in 0–7 ms, document symbols in 4 ms, and folding in 1 ms. It advertised workspace symbols and folding. On the 1,200-extra-symbol fixture, initialize took 75 ms, empty workspace symbols took 1,610 ms and returned 1,024 entries, `Player` took 3 ms and returned two, `plctrl` took 4 ms and returned zero, document symbols took 156 ms and returned 1,203 top-level entries, and folding took 5 ms and returned 1,203 ranges. These were protocol observations, not full IDE GUI acceptance. No Rocket source, branch, or WP01A/B file was edited or built.
- **GUI manual smoke: mostly MANUAL SMOKE DEFERRED.** The built app launched and showed the editor and folding margin. It automatically restored a previous out-of-scope workspace, so the window was closed without using that workspace. Workspace/file open, Quick Open, symbol navigation/fuzzy query, Outline, history, Command Palette, productivity actions, word wrap, Format-on-Save, folding behavior, split editing and independent presentation, dirty-view close, snippets, semantic features, debugger markers, recovery, and active-work shutdown were not observed in the RocketIDE scope. Startup visibility is not evidence for those flows.
- **Performance and lifetime matrix: INCOMPLETE.** A one-off benchmark of the production `QuickOpenSearchService` and `WorkspaceFileSystem` on the RocketIDE repository scanned 2,391 visible files in 46 ms cold and 30 ms warm. The empty query returned the 250-result cap in 6 ms; `MainWindow` returned 19 results in 4 ms; `plctrl` returned 10 results in 4 ms; a pre-cancelled query threw cancellation. Production caps are 10,000 scanned files and 250 results, and the filesystem excludes `.git`, `.rocketc`, `.vs`, `bin`, `obj`, and `out` directories. This local repository measurement does not establish responsiveness on larger workspaces or under UI load. The real LSP timings above cover two synthetic RocketIDE fixtures. Idle LSP stop took 13 ms; in a separate run, stop during an active `workspace/symbol` request took 7 ms, and the request completed with `JsonRpcResponseException`. Query cancellation, memory, symbol revisions, and the complete five-second shutdown matrix with real LSP/debugger/compiler work remain unmeasured. The deterministic fake-LSP shutdown race is fixed; these narrower checks do not replace the full matrix.

No production-branch merge or push has occurred. The candidate is **not yet a verified production baseline** for the next work package. Reconcile with current `main`, rerun the full gate, and merge/push only after the authoritative symbol contract and remaining acceptance are resolved within the authorized repository boundary.
