# FINAL-WP07 acceptance and consumer package record

Date: 2026-09-27
Status: package built and exercised; full WP07 certification remains open because of one confirmed IDE-side defect, ten checks still unrun, four blocked gates, and the explicit branch-promotion hold.

## Release identity

- Consumer package version: RocketIDE 1.0.0.
- Embedded informational version: 1.0.0+376854e39be222984452c6c87d289f70e68e364e.
- RocketIDE source: branch codex/rocketide-final-wp06, commit 376854e39be222984452c6c87d289f70e68e364e. This is the separately pushed WP06 branch; it remains unmerged. The disposable build clone is clean at the same SHA.
- Rocket source: master at 1f6ba76f16f3246095d5d573c28d825d8b9367e3. The accepted Rocket 3.5 Eddie tip 6eeb2dcd and WP01 LSP tip 1ad63fea are ancestors.
- Compiler and LSP: rocketc 3.0.0 and rocket-lsp 1.0.0. Build root: C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\w\out\build\windows-release.
- Compiler SHA-256: DA5FF4868D4FEF3656327FD7A268100EAFF6634E0F624E665211305A97BF4628.
- LSP SHA-256: DD0DFAB0BB9EBE24CDDEB1BC373D8A669A51F2E537A4D165DECD47DD7436736B.
- Runtime dependencies verified for the Rocket build: LLVM 22.1.6, Ninja 1.13.1, Clang 22.1.6, MSVC 19.51.36260 x64, raylib 6.0. Rocket release build completed 150/150 Ninja steps and serial CTest completed 312/312, 0 failures (385.09 seconds). The focused language_server_incremental test also passed.
- Host details inherited from the same-day WP06 inventory: Windows 11 Pro build 26200 x64, Intel i7-13620H, 15.7 GB RAM, 1920x1080 at 125% scaling, one display and no VM/alternate display available. The current WMI refresh returned Access Denied, so these host facts were not independently refreshed for this report. Storage class was not captured.
- Acceptance timestamp: 2026-09-27 14:07 UTC / 17:07 Asia/Beirut. GUI and LSP observations were warm-session measurements; no cold-start, memory/CPU, or edit-backlog performance measurement was recorded.

## Automated verification and package

A fresh isolated clone of the WP06 branch ran scripts/verify.ps1 with NuGet restore and audit access. Release build: 0 warnings, 0 errors. Test counts: Core 40, Rocket 143, Infrastructure 63, Debugger 41, App 223; total 510 passed, 0 failed, 0 skipped. Self-contained win-x64 publish and debugger asset guards passed. Saved log: C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\wp07-verify-2026-09-27.log.

scripts/package.ps1 produced a self-contained multi-file Windows x64 archive and extracted copy. ZIP: C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\wp07-release-1.0.0\RocketIDE-win-x64-1.0.0.zip. Size: 134,380,045 bytes. SHA-256: C0D77FF79E1AE98C46E14728BEA4F8F3B538B9EC5F7F8D8179C16853E51B5E4B. The extracted package contains 834 files, no PDBs, RocketIDE.exe, RocketIDE.Debugger.dll, README.txt, and architecture-specific DbgX EngHost files. It does not bundle a Rocket SDK; the consumer must separately install/build Rocket and configure its compiler and language-server paths.

The package was launched from the extracted ZIP. It opened a disposable three-file Rocket project. LSP reached online with 3 files and 18 symbols. Tools > Validate Rocket Environment confirmed the configured paths and versions. IDE Check exited 0; IDE Build produced main.exe and exited 0; Test showed 1 passed, 0 failed, exit 0; Run printed the expected Rocket pilot / score / Fibonacci output and exited 0. Resolve Dependencies created the empty rocket.lock; Dependency Tree and Audit Dependencies then exited 0.

The user profile tool-path update is recorded in C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\wp07-profile-rocket-tools-switch-20260927.json; exact pre-change backup is wp07-profile-rocket-tools-before-20260927.json. Tools > Rocket SDK Settings was saved in the app and Output confirmed the update. TrustedCheckoutRoots remains empty; ProgramArguments is blank; automatic selection remains disabled. No RocketIDE source or Rocket source was edited, committed, pushed, or merged in this work. No Rocket-side defect was found in the full Rocket suite; there is no issue to queue for Eddy from this run.

## Confirmed release blocker

In the packaged app, Tools > Target Information failed with Rocket compiler error R5002: unexpected target argument 'C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\wp07-runtime-20260927\consumer-project'. Rocket 3.0.0 help documents the command as rocketc target [--target alias-or-triple] [--verbose], with no positional project-directory argument. Running rocketc target --verbose from the same project directory succeeded and returned windows-x64 / x86_64-pc-windows-msvc. The RocketIDE source adapter at src/RocketIDE.Rocket/Compiler/RocketAdvancedCommand.cs lines 62-64 appends the project path to every advanced command, including Target. This confirms an IDE-side CLI-adapter defect rather than an upstream Rocket failure. The exact finding and suggested next verification are saved in WP07_TARGET_INFORMATION_FINDING_2026-09-27.md. No code was changed; this needs a focused fix and regression by the requested stronger coding model before the candidate can be called fully release-certified.

## Buffer preservation note

During the packaged-app GUI replay, its inherited lsp-smoke-workspace editor buffer showed a dirty marker and a shortened while condition. The on-disk symbols.rocket file remains byte-for-byte unchanged at C:\Users\Administrator\Desktop\Projects\RocketIDE\artifacts\lsp-smoke-workspace\symbols.rocket; its recorded modification time predates this replay, and the original RocketIDE instance still shows the full source. I did not save or discard the test-instance buffer. The original app and test package remain open.

## Reconciled historical acceptance matrix

Status values describe evidence, not intended outcomes. Earlier reports remain the source for prior PASS rows; WP06 debugger and repair evidence supersedes the pre-repair deferred audit.

| ID | Status | Current reconciliation |
| --- | --- | --- |
| A01 | PASS | Main and origin/main stayed at f0e37d9; WP06 branch stayed at 376854e; isolated build clone clean. |
| A02 | PASS | Fresh 510/510 verify gate and win-x64 publish guard. |
| A03 | PASS | Rocket snippet compiler fixtures 5/5 from prior audit. |
| A04 | PASS | New 1.0.0 package script run completed. |
| A05 | PASS | ZIP hash, required debugger files, README, extraction and launch verified. |
| A06 | PASS | Exact package executable launched from extracted ZIP. |
| A07 | PASS | Disposable multi-file project opened; main.rocket selected with Quick Open. |
| A08 | PASS | Configured LSP online, 3 files, 18 symbols. |
| A09 | PASS | IDE Build succeeded for the disposable package target. |
| A10 | PASS | Direct compiler Check and prior IDE Check routing evidence. |
| A11 | PASS | IDE Run produced expected output and exited 0. |
| A12 | PASS | Expected structured missing-tests fixture error previously captured. |
| A13 | PASS | No product source or existing branch modified; profile update has before/after receipts. |
| A14 | PASS | Prior Check diagnostic routing evidence retained. |
| A15 | PASS | BUG-001 repaired and final WP06 run-output replay passed. |
| A16 | PASS | Current package Test view showed 1 passed, 0 failed, exit 0. |
| A17 | PASS | Stop behavior and process cleanup previously verified on disposable fixture. |
| A18 | PASS | Live diagnostic and exact-range navigation previously verified. |
| A19 | PASS | Definition and references previously verified with live Rocket LSP. |
| A20 | PASS | Rename happy path previously verified on disposable project. |
| A21 | PASS | BUG-003 repair/regression and qualified-import replay passed; the historic exact symptom is not claimed newly reproduced. |
| A22 | PASS | BUG-004 formatting and Undo replay passed on final WP06 package. |
| A23 | PASS | Dirty-buffer indexed search previously verified. |
| A24 | PASS | Replace preview/apply previously verified; conflict/cancel stress remains separate. |
| A25 | PASS | 4 MiB-plus editor/LSP exclusion behavior previously verified. |
| A26 | PASS | 252-file responsiveness smoke previously verified; resource telemetry remains unmeasured. |
| A27 | FAIL | Environment, Resolve, Tree and Audit work with the configured compiler. Target Information reproducibly fails R5002 due the IDE passing an unsupported positional path. |
| A28 | PASS | Dirty-buffer crash recovery previously verified on disposable project. |
| A29 | PASS | Recovery choices, external-change prompt and layout restore previously verified. |
| A30 | BLOCKED | No clean Windows x64 VM or machine without .NET is available in this session. |
| A31 | NOT RUN | Package extraction and launch passed locally; isolated profile update/uninstall cycle was not exercised. |
| A32 | BLOCKED | No alternate display/VM is available to test 100/125/150/200% and multi-monitor cases. |
| A33 | NOT RUN | Ctrl+Shift+O and Ctrl+P paths were observed; full keyboard-only order, screen reader, selection and bracket presentation remain unverified. |
| A34 | PASS | WP06 final package completed real DbgX breakpoint, evaluate, run-to-cursor, continue/pause/thread inspection, restart and stop cycles. |
| A35 | PASS | The final portable package was used for the WP06 live DbgX workflow. |
| A36 | NOT RUN | WP06 measured controlled shutdown; the full idle/LSP/build/run/test/debug/output/dirty/split shutdown matrix was not repeated. |
| A37 | NOT RUN | No LSP edit backlog, CPU/memory stability, or repeated stress telemetry was captured. |
| A38 | BLOCKED | Complete end-to-end acceptance on a separate clean Windows host/VM is unavailable. |
| A39 | NOT RUN | No CI result for exact final source SHA 376854e was verified; older main CI evidence is for a different SHA. |
| A40 | NOT RUN | Complete WP02 save-affinity, restart, sync-race and shutdown replay remains incomplete. |
| A41 | PASS | Quick Open was used on the three-file project; earlier Command Palette filtering evidence retained. |
| A42 | NOT RUN | WP04 full preferences, folding, wrapping, format-on-save and reopen matrix is not complete. |
| A43 | NOT RUN | Split editing was visible, but full shared-document edits, snippet acceptance/caret and Undo matrix were not replayed here. |
| A44 | NOT APPLICABLE | The WP06 implementation packet is complete; this row is not additional future implementation. |
| A45 | BLOCKED | Local package is ready for review, but GitHub merge/push/release promotion is held by the explicit no-push/no-merge boundary. No slim consumer branch or tag was created. |
| A46 | NOT RUN | Full known LSP limitation matrix remains incomplete; prior format-on-save/signature/locals evidence is not a complete replay. |
| A47 | NOT RUN | Several package instances are open, but independent workspace/profile, missing-SDK/LSP, offline and recovery degradation behavior was not fully validated. |

Counts: 31 PASS, 1 FAIL, 10 NOT RUN, 4 BLOCKED, 1 NOT APPLICABLE. The 1.0.0 archive is a usable local freeze candidate for the demonstrated workflow, but it is not a fully WP07-certified or published release. The exact remaining code fix and the environment/acceptance gates are enumerated above; none was silently marked passed.

