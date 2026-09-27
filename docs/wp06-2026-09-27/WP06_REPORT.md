# FINAL-WP06 verification and delivery

WP06 acceptance passed on 2026-09-27. The inherited incremental-symbol gate was resolved in its own Rocket branch, the real DbgX debugger foundation was repaired before feature work, and the supported debugger workflow was implemented and replayed on the final portable package. The full IDE gate passed **510/510**, with zero failures, skips, build warnings or errors. Original profile, audit files, fixtures and existing lanes were preserved.

This is WP06 completion only. No merge, PR, release certification or WP07 execution occurred. After verification, the user authorized pushing the completed IDE work to a separate branch in the RocketIDE repository. The companion Rocket prerequisite remains local; both branches remain unmerged.

## Source reconciliation and integration dependencies

| Lane | Verified source / result |
| --- | --- |
| Original IDE main | `f0e37d9ee9f14a0e5139de484883e0c985687da1`, unchanged and clean |
| Existing IDE repair lane | `0de2e223a60911d5d4269ee82af276c4cea10d7a`, includes repair source `db99bddad43a9b35b29e77c108f7d6d72a7cb63b`; unchanged and clean |
| WP06 IDE lane | `codex/rocketide-final-wp06`, managed checkout `C:\Users\Administrator\.codex\worktrees\rocketide-final-wp06\RocketIDE`, based on the verified repair HEAD above |
| Original Rocket master | `10f295dd000b93fe50d254be21fedffd17892aeb`, unchanged and clean |
| Companion BUG-003 source | `c2268ca1c241c8d20777000364bc1488fa6cb1ee`, retained in the new prerequisite lane; original lane unchanged |
| Prerequisite local commit | `91c8b1430d94c4e27c14a55b536eaa5a57fe518c`, `test(lsp): reconcile incremental symbol generation oracle`, branch `codex/rocketide-wp06-incremental-gate` |

The companion prerequisite commit is a separate integration dependency, not an IDE commit or a merged change. It includes the earlier BUG-003 LSP repair through ancestry. The final GUI replay explicitly selected the fresh companion LSP rather than assuming the user's restored SDK configuration contained that repair.

Entry and exit evidence: [entry-worktrees.json](evidence/entry-worktrees.json), [live-default-branches.txt](evidence/live-default-branches.txt), [final-preserved-lanes.json](evidence/final-preserved-lanes.json). All 13 pre-existing lanes retain their entry HEAD and exact dirty-path state. In particular, the audit lane's five modified and three untracked paths were not staged, committed or changed by WP06. The new WP06 lane is intentionally excluded from the preserved-lane comparison. All 46 entries of the prior repair evidence manifest were verified at entry.

## Prerequisite gate closure

The inherited `language_server_incremental` failure was reproduced: 32 failed assertions. Instrumentation retained 33 actual before/after workspace-symbol response pairs. Thirty-two differed only in `data.rocketGeneration`; one empty response was identical. Names, kinds, locations, ordering and all other semantic data remained equal.

The narrow test-only repair normalizes only that generation field for semantic equality and retains dedicated assertions for generations 2 and 3 across edited, cached, filtered and repeated requests. It does not weaken symbol semantics or modify LSP implementation. The affected CTest run passed **6/6**, zero failures/skips, exit 0: `language_server`, `analysis_queue`, `language_server_analysis`, `language_server_incremental`, `workspace_state`, `formatter` (41.83 seconds). Independent read-only review found no issue in the prerequisite fix.

Evidence: [original failure](evidence/incremental-original-red.log), [actual instrumented responses](evidence/incremental-instrumented-red.log), [response comparison](evidence/incremental-response-comparison.json), [six-test gate](evidence/prerequisite-six-tests.log).

## Foundation repairs and supported workflow

The first real source breakpoint remained deferred because the target PDB had not been explicitly loaded. Loading the local target symbols established binding. A later real Pause/thread inspection stalled in remote symbol lookup; restricting symbols to the matching local artifacts and using the installed DbgX `StartDirectory` option made inspection bounded and reproducible. Thread selection now resets frame scope. A native frame is never assigned its caller's Rocket source location; when `ln @rip` omits source, only a top Rocket stack frame supplies the current location. Exited process records are not treated as active targets.

The DEBUG panel now has Inspection, Breakpoints and Watches / Evaluate tabs. Breakpoints expose enabled state, source, line, Bound/Unbound/Error/Disabled, details, navigation, removal and Remove All. Disabled breakpoints do not bind or block a different target. The panel and editor use the same enabled breakpoint state.

Watches/Evaluate use native DbgEng evaluation of ASCII local/parameter identifiers only: at most 128 characters, 16 watches, 4096 result characters. Calls, operators, member access and native debugger commands are rejected before transport. Inspection is serialized and bounded to two seconds; a watch batch shares that deadline. Running or stale frame results cannot repopulate the UI after execution, Stop, Restart or a newer inspection.

Restart captures the original target and a copy of its tool settings, stops/disposes the previous session, invokes the debug build again (a valid compiler cache hit is allowed), then reapplies user breakpoints. Run to Cursor uses a native one-shot breakpoint; user breakpoints remain separate. Stop has an independent bounded cleanup path. Command tests cover idle, launching, running, stopped, terminating, terminated and faulted states.

The final slow-evaluation replay exposed one additional native lifecycle defect. Killing the stalled EngHost followed by `DebugEngine.Dispose()` caused DbgX to spawn a replacement host. A real native regression reproduced one remaining host. Inspection of installed DbgX confirmed that Dispose releases interfaces while its request loop can recover the engine; `ShutdownAsync` ends that loop. The transport now awaits `ShutdownAsync(250)` on its engine context before Dispose. The regression passed, the complete gate was rerun, and a new final package was produced. Per-instance process leases remain in place so delayed cleanup cannot kill a subsequent session's host.

Evidence: [foundation regression failures](evidence/foundation-tests-red.log), [foundation2 live thread stack](evidence/foundation2-paused-thread-stack.png), [native leak regression red](evidence/native-stalled-cleanup-red.log), [native leak regression green](evidence/native-stalled-cleanup-green.log), [review record](REVIEW_RECORD.md). Additional regressions cover cancellation during construction/launch, disposal of late factory results, post-stop inspection deadlines, stale watch results, ownership and temporary-breakpoint cleanup.

## Expression feasibility

| Real native expression / condition | Observed result |
| --- | --- |
| `seed` in main | `int64 0n21` |
| `value` parameter in twice | `int64 0n21` after its prologue |
| Caller frame then thread 0 | Caller `seed=21`; selecting thread 0 resets to the top frame and `value=21` |
| `answer` after the call | `int64 0n42` |
| Missing identifier | Explicit unavailable / could not resolve result |
| Invalid native expression `(` | Native syntax error in feasibility probe |
| `pair` aggregate local | Native `<CLR type>*` address |
| `pair.left` / `pair->left` | Native aggregate/member type information unavailable |
| `total` in aggregate fixture | Scalar 30 works |
| Evaluation while running | Native request queues until stop; product rejects it and disables Evaluate/Refresh |

The current Rocket PDB does not provide useful aggregate field information for this fixture. Aggregate/member evaluation remains a separate upstream debug-info enhancement. It is not advertised in the shipped UI and does not block the proven scalar identifier subset. No second Rocket evaluator was introduced.

Evidence: [frame/evaluation probe](evidence/dbg-frame-evaluation-probe.log), [member feasibility](evidence/dbg-member-evaluation-line8-probe.log), [final Evaluate/watch](evidence/accepted-evaluate-watch42.png), [missing symbol](evidence/accepted-missing.png), [member rejection](evidence/accepted-member-rejected.txt).

## Automated verification

| Check | Result |
| --- | --- |
| Fresh repaired IDE baseline | 487/487; Core 40, Rocket 143, Debugger 22, Infrastructure 63, App 219; zero skips/warnings/errors; publish guards passed |
| Final full `scripts/verify.ps1` | **510/510**; Core 40, Rocket 143, Debugger 41, Infrastructure 63, App 223; zero failures/skips; 0 warnings, 0 errors; exit 0; publish and amd64 EngHost guards passed |
| Real native final workflow probe | Two complete cycles; source BP9, evaluation 21, RTC10/answer42, Continue/Pause/thread inspection, pending RTC16 cancelled by Pause, actual `bl` contains only user BP9; Stop 367/359 ms; `HasTemporaryBreakpoint=False`; exit 0 |
| Source/document whitespace check | `git diff --check` passed for source and authored documents; raw captured evidence is excluded from whitespace lint and retained byte-for-byte |
| Final portable package | Created in a new output directory; all four required binaries nonempty and individually hashed |

Evidence: [baseline gate](evidence/ide-baseline-verify.log), [final full gate](evidence/wp06-full-verify-final.log), [native final probe](evidence/native-workflow-final-probe.log), [probe source](evidence/native-workflow-final-probe.cs.txt), [package log](evidence/package-final.log), [asset manifest](evidence/final-package-assets.json).

Earlier 507/509 gates and intermediate packages remain historical evidence; the authoritative final result is 510/510 and `package-final` below. Full Rocket compiler, ASAN, clean-machine, cross-DPI, all historical deferred rows and release/CI certification were not claimed or run as WP06 acceptance.

## Direct final-package acceptance

The final application was operated through the supported `@oai/sky` native UI bridge. Disposable fixtures were used; no original fixture was edited. Screenshots and accessibility trees accompany the observations. The bottom panel was resized to 379 pixels in the temporary profile for readable inspection; this is not a multi-DPI/default-layout certification.

| Case | Observed final-package result | Evidence |
| --- | --- | --- |
| Launch, artifact discovery, source BP | Build validates EXE/PDB/map; BP9 is Bound; source highlight and seed21 agree | `accepted-bp9.*`, debug output |
| Step in/over/out | F11 -> line3, F10 -> line4/value21, Shift+F11 -> line9 | `accepted-step-in.txt`, `accepted-step4.*`, `accepted-step-out.txt` |
| Threads, stack, frame, locals | Caller frame shows seed21; thread0 restores top frame/value21 | `accepted-caller-frame.txt`, `accepted-thread.txt` |
| Run to Cursor hit | Ctrl+F10 stops at line10 with answer42 | `accepted-rtc10.*` |
| Continue/Pause | Running clears inspection values; Pause stops with native stack; Evaluate/Refresh are unavailable while running | `accepted-running.*`, `accepted-pause.txt` |
| Watches/Evaluate | answer42; missing symbol unavailable; member expression rejected; watch invalidated on run/Stop; Remove Watch removes row | `accepted-evaluate-watch42.*`, `accepted-missing.*`, `accepted-member-rejected.txt`, `accepted-watch-removed.txt` |
| Breakpoint management | Disable removes active glyph; enable rebinds; navigation, Remove and Remove All work; line15 ambiguity shows Error/details; inactive binding becomes Unbound | `accepted-disable.png`, `accepted-enable.txt`, `accepted-navigate.txt`, `accepted-remove.txt`, `accepted-remove-all.txt`, `accepted-error.png` |
| Restart | Select rocket.toml, invoke Restart, return to original main source BP9; old watch value is refreshed for new session | `accepted-other-active-file.png`, `accepted-restart-original-ready.*` |
| Slow evaluation | Suspend only verified owned engine; Evaluate pending while UI changes tab in 463 ms; deadline faults session; no debuggee or replacement EngHost remains | `accepted-slow-responsive.*`, `accepted-slow-timing.json`, `accepted-slow-fault.*`, `accepted-after-slow-processes.json` |
| Fault during pending RTC | Terminate verified owned engine during RTC16; explicit engine-disconnected status; no debuggee/EngHost remains; Restart recovers | `accepted-rtc16-running.*`, `accepted-rtc-fault.*`, `accepted-disconnect-processes.json`, `accepted-recovered.txt` |
| Launch cancellation | Stop during Restart yields restart-cancelled state; subsequent Restart hits BP9 | `accepted-launch-cancel.*`, `accepted-restart-after-cancel.txt` |
| Pending RTC cancellation | Pause RTC16, Continue, program exits normally rather than stopping at16; native probe independently checks actual breakpoint table after Pause | `accepted-rtc-paused.txt`, `accepted-rtc-cancel-resumed.txt`, `accepted-natural-exit-no-temp.png`, `native-workflow-final-probe.log` |
| Stop and repeated shutdown | Stop leaves zero debuggee/EngHost; after two further real debug sessions, close while stopped removes window within523ms, below5sec; no product processes remain | `accepted-stop.*`, `accepted-stop-processes.json`, `accepted-exit-check-repeated.png`, `accepted-repeated-debug-shutdown.json`, `accepted-repeated-exit-processes.json` |
| BUG-001 | Build exit0; Run stdout `WP06 repair replay` and exit0 visible without manual output scrolling | `accepted-bug001-build.png`, `accepted-bug001-run.*` |
| BUG-002 | No-tests directory reports R5001, exit2, no-summary final message; Test returns idle and works again | `accepted-bug002-test.*`, `accepted-bug002-rerun.*` |
| BUG-003 | LSP offers Import src.math; applying adds import and qualifies call; Problems clears; Undo restores unsaved edit | `accepted-bug003-offer.png`, `accepted-bug003-applied.*`, `accepted-bug003-undo.txt` |
| BUG-004 | Shift+Alt+F changes math.rocket to canonical spacing; Undo restores exact source | `accepted-bug004-before.png`, `accepted-bug004-formatted.*`, `accepted-bug004-undo.*` |

Evidence names beginning `accepted-` identify the final package. Earlier names beginning `final-` mostly belong to the superseded reviewed candidate: filenames alone are not pass claims. For example, `final-rtc15-hit` records the attempted ambiguous location, and `final-after-slow-processes.json` records the reproduced leak. Their replacement passing observations are identified above. Some accessibility snapshots lag a rendered frame; use the corresponding screenshot and subsequent stable snapshot together. The first immediate shutdown sample saw the window still present at42ms; the later bounded polling measurement proves523ms rather than treating the first sample as a success.

The historic BUG-003 symptom is not newly claimed reproduced. This replay verifies the repaired import/qualification behavior and retained regression coverage, consistent with the preceding repair report.

## Toolchain, PDB and package identity

Compiler: fresh LLVM-enabled MSVC Release build at `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\rocket-wp06-prerequisite\out\build\wp06-llvm\rocketc.exe`, version3.0.0, LLVM22.1.6, SHA256 `78C7FCA30C32B0CC1B896AF25D31C402485BDE995AF84792844EDC5E92B8AB55`. Compiler source is unchanged from the WP01 lane; no compiler implementation change was needed.

Companion LSP: `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\rocket-wp06-prerequisite\out\build\wp06-gate\rocket-lsp.exe`, SHA256 `879627A2D2289F03182BA00D9F85F0BF90A6EA7CD10A4316FF7AD9754258F479`. DbgX package version: `20260622.1.0`.

The tiny debug-proof fixture uses twice(seed), scalar locals and a bounded Sleep loop. Its final EXE/PDB/map hashes exactly match the originally inspected debug artifacts; GUI Restart invoked the compiler and obtained valid cache hits. PDB GUID `{A0BF5953-9445-6332-4C4C-44205044422E}`, age1; logical source `rocket:\source\main.rocket`; private local records value, doubled, seed, answer, tick. EXE SHA256 `05108A5CEB6D856D6A813383A13C0B3FE607890D005C7881CC9B2C677B2B68C6`; PDB `72F9B3FC00A42D0161913967CD2CD449A35BBAEE95AA95B996E00CBEE16C0EF3`; map `4EE02EB4F171C7C41A3D61C736A93281678665AF6E26B203242DDE352C000DFA`.

Final portable archive: `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\wp06-20260927\package-final\RocketIDE-win-x64-wp06-20260927-final.zip`, SHA256 **`1cfc7333ed90ce3c6898240662bba822144933b358e0d454de5a0628b22f217c`**. Extracted replay directory is the sibling `replay-final\RocketIDE-win-x64-wp06-20260927-final`.

| Final binary | Bytes | SHA256 |
| --- | ---: | --- |
| RocketIDE.exe | 287744 | `ABA20EEA4EE4E58669B8079526C85A82D9AAEFCEC1CB01D5E537FAE3E0BB4127` |
| RocketIDE.dll | 800256 | `89EAB0CC22A7B7DC2B10C3C8A98BD97A1CB1FD0D9051E0FC05917701860B77FF` |
| RocketIDE.Debugger.dll | 108032 | `49D1D79D8EE1EBBB0A0E5BCCA75800BC89567FE97DD152E2076FE6E5FBF07DCA` |
| amd64/EngHost.exe | 42808 | `8CE65BF2B8F3D6870B1A2CA49E705970428DD7D39FA303D53FB399691D36031B` |

The apphost hash alone is not the source identity; product/debugger DLL hashes above identify the final changed code. The package was built from the final tested working tree immediately before its local delivery commit. The retained large PDB dump and archive are indexed in [external-artifact-manifest.json](evidence/external-artifact-manifest.json); fixture sources and smaller diagnostic evidence are committed here. The local attributes preserve raw evidence/fixture bytes: native debugger output contains intentional trailing spaces and captured source dumps may contain final blank lines. Those captures are not reformatted to satisfy source whitespace lint.

## Restoration, remaining scope and handoff

The original profile at `C:\Users\Administrator\AppData\Local\RocketIDE` was backed up before GUI changes and restored after the last replay. **71/71 files** have the original paths, lengths and SHA256; zero mismatches. The test profile and original backup remain outside Git under the task evidence root. All10 audited evidence files and9 disposable source/config fixture files retain their hashes; all13 existing lanes retain HEAD/status. Final process inventory is empty.

Evidence: [profile restoration](evidence/profile-restoration.json), [preservation verification](evidence/preservation-verification.json), [lane preservation](evidence/final-preserved-lanes.json), [final processes](evidence/final-product-processes.json), [restoration procedure](evidence/restore-and-verify.ps1.txt). The evidence manifest in this report directory indexes retained evidence; private profile contents and build binaries are excluded from Git.

Completed: prerequisite reconciliation and focused repair, mandatory native debugger proof before/after, supported WP06 implementation, final full gate/package/UI replay, prior repair replay and preservation. No remaining WP06 blocker was observed. Deferred: aggregate/member debug information and the broader historical/clean-machine/release acceptance belonging to WP07. These are not marked passed by this report.

The IDE delivery commit uses subject `complete RocketIDE debugger workflow`. Its SHA and the authorized `codex/rocketide-final-wp06` remote branch are recorded by the task's final Git receipt; the separate prerequisite SHA is above. Both branches remain unmerged. The Rocket prerequisite is not authorized for push. WP07 may consume this report, the prior deferred matrix, and both integration dependencies only after a new explicit authorization. No WP07 work has started.
