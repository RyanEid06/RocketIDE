# RocketIDE deferred acceptance coverage matrix

Audit date: 2026-09-26  
Audited source: f0e37d9ee9f14a0e5139de484883e0c985687da1  
Audit branch: codex/rocketide-deferred-acceptance  
Baseline branch/ref: main / origin/main, synchronized at audit start  
Current app: audit-worktree Release verification publish, SHA-256 946A35B1905FE0C104F984B3B6F54B778B050724C62F6F523374C2370640A853  
Compiler: rocketc 3.0.0, SHA-256 AC43B6E2B016A357499B6F62820927A9334CD5C69A67BF8AAD6DD92A386F7D9F  
LSP: rocket-lsp 1.0.0 from the separately built rocket-lsp-fuzzy-symbols worktree, SHA-256 C5986606E98016589E7BF3611DE60964634D7AF3A8171CCD5F83F4064FBDFE1B

Outcome values are exactly PASS, FAIL, BLOCKED, NOT RUN, or NOT APPLICABLE. A PASS applies only to the specific row and evidence stated.

| ID | Historical criterion / check | Outcome | Evidence and limits |
|---|---|---|---|
| A01 | Read-only preflight: production branch/ref, checkout status, candidate isolation | PASS | main and origin/main matched f0e37d9ee9f14a0e5139de484883e0c985687da1; clean tracked checkout; isolated audit branch based on that SHA. Existing linked worktree status was inaccessible because Git ownership differed; no global safe.directory setting was changed. |
| A02 | Current full Windows verification gate | PASS | scripts/verify.ps1: Release build, 0 warnings/errors; Core 40, Rocket 143, Debugger 22, Infrastructure 63, App 205; 473/473 passed, 0 failed, 0 skipped; self-contained win-x64 publish and debugger asset guards succeeded. |
| A03 | WP05 bundled snippet compiler fixtures | PASS | scripts/verify-wp05-snippets.ps1 with the hash-identified compiler: main, fn, impl, match, testmain all checked successfully (5/5). This is compiler validation, not snippet insertion/undo GUI acceptance. |
| A04 | Portable release package creation | PASS | scripts/package.ps1 Release completed to the unique audit output root and produced the portable ZIP and SHA-256 file. |
| A05 | Package content guards / artifact identity | PASS | ZIP SHA-256 e2540ca1b8b79697313057c5f2a6552326054317ab5bc885eb970a8ac7819f98. Script verified nonempty RocketIDE.exe, RocketIDE.Debugger.dll, and x64/amd64 EngHost.exe before ZIP creation. This is not a clean-machine launch. |
| A06 | Launch the audit-worktree app | PASS | Native RocketIDE window observed from artifacts/verify-win-x64/RocketIDE.exe; no Visual Studio or VS Code launch was required for RocketIDE itself. |
| A07 | Open a project directory and source file | PASS | Disposable basic-project fixture opened; Explorer showed rocket.toml, src/entities.rocket, src/main.rocket; two editor groups remained visible. No source file in the protected production checkout was edited. |
| A08 | Rocket LSP project startup and workspace indexing | PASS | Native status showed rocket-lsp initialized, LSP: project · 3 files · 12 symbols, target basic-project. Only startup/index status was checked; semantic requests were not accepted by this row. |
| A09 | WP10 GUI Build | PASS | Native Build produced “build succeeded” for the disposable main.exe target. This does not establish Run, Stop, or Test behavior. |
| A10 | WP10 direct compiler Check and structured success response | PASS | rocketc check main.rocket --message-format=json exited 0 with success=true on the disposable fixture. IDE Check result routing was not verified. |
| A11 | WP10 direct compiler Run and program output | PASS | rocketc run on the disposable fixture exited 0 and printed “audit fixture”. This is CLI evidence only. |
| A12 | Direct compiler Test error response for this no-tests fixture | PASS | rocketc test returned structured R5001 diagnostic “package test directory does not exist” and exit 2. This is the expected fixture precondition failure and supports the UI reproduction; it is not a passing IDE Test result. |
| A13 | Product/compiler/test source remains unchanged | PASS | Audit was report-only. No source, existing test, script, dependency, CI, or acceptance-criteria file was edited. Tracked status was clean before audit reports were written. |
| A14 | IDE Check diagnostics and Problems routing | NOT RUN | The direct CLI check passed, but the IDE menu Check outcome and diagnostic navigation were not established. |
| A15 | IDE Run streams program output and exit result to Output | FAIL | Toolbar Run was observed disabling while the process ran and re-enabling afterward. Output still showed only earlier LSP/Build entries; it omitted both the CLI-proven “audit fixture” stdout and the Run exit line. See BUG-001 and evidence/ui-run-output-missing.png. |
| A16 | IDE Test shows a terminal PASS/FAIL/summary result | FAIL | After invoking Test against the no-tests package, the Test panel remained “Test run started…” with an empty result grid after the command ended and controls re-enabled. Direct CLI produced R5001/exit 2. See BUG-002 and evidence/ui-test-panel-stuck.png. |
| A17 | IDE Stop terminates the owned child-process tree | NOT RUN | No controlled long-running fixture was started and stopped. |
| A18 | Live diagnostics and exact source-range navigation | NOT RUN | No invalid edit was introduced in the disposable workspace. |
| A19 | WP09 Go to Definition / Find References against live Rocket LSP | NOT RUN | Not exercised. |
| A20 | WP09 Rename and all-or-nothing server workspace edits | NOT RUN | Not exercised. |
| A21 | WP09 server-provided Quick Fix / code actions | NOT RUN | Not exercised. |
| A22 | WP09 Format Document and formatter capability behavior | NOT RUN | Not exercised. |
| A23 | WP11 dirty-buffer workspace search and replace | NOT RUN | Not exercised. |
| A24 | WP11 replace preview, conflict detection, cancellation, and result stress | NOT RUN | Not exercised. |
| A25 | WP12 source around/above 4 MiB stays editable and outside LSP | NOT RUN | No large source fixture was created. |
| A26 | WP12 large-workspace responsiveness | NOT RUN | No workload or latency measurement was taken. |
| A27 | WP13 configured-SDK advanced commands and structured failure handling | NOT RUN | No dependency, target, format, coverage, profile, or benchmark command was invoked. |
| A28 | WP14 forced IDE kill with dirty buffers | NOT RUN | Recovery behavior was deliberately not disturbed during this pass. |
| A29 | WP14 restore/discard/defer recovery, external-change conflicts, layout restore | NOT RUN | No crash or external-change decision flow was executed. |
| A30 | WP15 clean Windows x64 machine with no .NET runtime | BLOCKED | No separate clean-machine/VM environment was available in this session. Local publish success does not substitute for this gate. |
| A31 | WP15 external SDK setup and portable folder update/uninstall behavior | NOT RUN | Package was built but not extracted/launched under an isolated machine profile. |
| A32 | WP16 100/125/150/200% DPI and multi-monitor matrix | BLOCKED | Display scaling/monitor details were not available from the permitted Windows metadata queries; no alternate display environment was attached. |
| A33 | WP16 keyboard command states, focus order, accessible names, screen-reader exposure, selection and bracket presentation | NOT RUN | No systematic keyboard, accessibility-tree contract, screen-reader, or visual-style matrix was completed. |
| A34 | WP17 live DbgX debugger against a tiny Rocket debug target | NOT RUN | No debug-build/breakpoint/continue/pause/step/threads/stack/locals/output/stop interaction was performed. |
| A35 | WP17 debugger workflow from the packaged app | NOT RUN | Package assets were checked, but no packaged debugger launch was performed. |
| A36 | Shutdown while build/run/test/debug/LSP/output work is active | NOT RUN | No bounded shutdown-under-load series was run. |
| A37 | LSP edit backlog, cancellation, memory/CPU, concurrent-operation and repeatability stress | NOT RUN | No performance or prolonged stress measurements were taken. |
| A38 | Full clean-machine end-to-end workflow from package through recovery and debugger | NOT RUN | This combines multiple unrun gates and cannot be inferred from the baseline suite. |
| A39 | GitHub Windows CI / uploaded verification and package artifacts | NOT RUN | No CI workflow was triggered or queried for this audit. |
| A40 | FINAL-WP02 lifecycle, document sync, save affinity, restart and shutdown acceptance | NOT RUN | Current full unit/integration baseline is recorded at A02; the historic user-visible lifecycle replay was not repeated. |
| A41 | FINAL-WP03 Quick Open, symbols, Outline, history, and Command Palette | NOT RUN | Not exercised in this pass. |
| A42 | FINAL-WP04 editor productivity, preferences, folding, wrap and format-on-save | NOT RUN | Not exercised in this pass. |
| A43 | FINAL-WP05 split editing, independent presentation, dirty close, snippet acceptance/undo | NOT RUN | Two groups were visible, but independent state, close behavior, snippet insertion, completion, and undo were not exercised. |
| A44 | FINAL-WP06 future implementation packet | NOT APPLICABLE | This audit does not start the separately planned future WP06 implementation. Existing WP17 live debugger acceptance remains in scope at A34–A35 and is NOT RUN. |
| A45 | FINAL-WP07 release certification / merge / publish decision | NOT APPLICABLE | This is a report-only audit on an isolated branch; no release certification, merge, push, or external issue was authorized. |
| A46 | Previously documented Rocket LSP support limits: format-on-save, incomplete-call signature help, native locals, semantic-token provenance | NOT RUN | Historical integration notes are not current acceptance evidence. These exact flows were not replayed against the present source/toolchain combination. |
| A47 | Missing compiler/LSP, standalone-file, multi-instance, offline and recovery degradation paths | NOT RUN | No removal, configuration reset, second instance, offline transition, or degradation scenario was performed. |

## Reconciled counts

47 rows total: 13 PASS, 2 FAIL, 2 BLOCKED, 28 NOT RUN, 2 NOT APPLICABLE.

All documented WP09–WP17 manual deferrals in ROADMAP.md and docs/WP11-WP17-CODE-ONLY-FOLLOWUPS.md map to A14–A39, A46, or A47. The FINAL-WP02 through FINAL-WP05 visible-feature deferrals map to A40–A43. FINAL-WP06 and FINAL-WP07 exclusions are explained at A44–A45. A PASS on automated verification or packaging is not full GUI acceptance or release readiness.

