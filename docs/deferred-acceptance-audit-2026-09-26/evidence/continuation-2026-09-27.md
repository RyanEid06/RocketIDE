# Deferred acceptance audit continuation - 2026-09-27

## Provenance and boundaries

- Continued the isolated audit checkout `C:\Users\Administrator\.codex\worktrees\rocketide-deferred-acceptance\RocketIDE`, branch `codex/rocketide-deferred-acceptance`, source base `f0e37d9ee9f14a0e5139de484883e0c985687da1`.
- UI target was the existing verification build `artifacts\verify-win-x64\RocketIDE.exe`, SHA-256 `946A35B1905FE0C104F984B3B6F54B778B050724C62F6F523374C2370640A853`; status showed Rocket SDK rocketc 3.0.0 and LSP 1.0.0.
- No product code, existing tests, acceptance criteria, or test scripts were changed. Disposable UI fixture was under `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\audit-continuation\A42-format`.
- No WP06 work was started. All acceptance rows below stay NOT RUN where full row criteria were not met; partial observations are not promoted to PASS.

## Partial observations

- A27: inspected the Tools menu and current SDK status, but did not invoke an advanced SDK command. No structured error behavior was captured.
- A31: package update/uninstall was not run. This needs a disposable Windows user profile and isolated package/install directory so setup effects can be observed safely. A35: extracted the existing portable ZIP under `C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\audit-continuation\A35-package`; its RocketIDE.exe SHA-256 was `946A35B1905FE0C104F984B3B6F54B778B050724C62F6F523374C2370640A853`. The packaged app launched the A17 stop fixture debug target, showed Pause/Step Debug controls, and Stop Debug returned “Rocket debug session stopped.” No usable breakpoint/thread/frame or stack/locals acceptance was reached, so A35 remains incomplete.
- A33: accessibility tree exposed named controls/menu entries and debugger control enabled states. Keyboard-only focus order, screen-reader announcements, selection, and bracket presentation were not completed. Screen-reader acceptance needs a configured screen reader with announcement capture.
- A34: the first debug attempt used a malformed disposable manifest and returned compiler R5001. After correcting the fixture, the audit UI stayed at “Launching Rocket debug target…” for at least 6.5 seconds without a thread/frame; Stop Debug returned “Debugger: Stopped.” Earlier live-debug evidence showed a native stack but locals enumeration failed with Win32 error 87 and the UI reported private symbols required. Full breakpoint/continue/pause/step/thread/stack/locals/output acceptance remains incomplete.
- A36: closed the audit window during the stuck debug-launch sequence. The UI window disappeared, but RocketIDE PID 18040 and rocket-lsp PID 8672 remained responsive after 3 seconds. Their executable paths were checked against the audit app and its LSP before stopping both processes. This is a partial shutdown failure observation; no operation-type shutdown series or graceful-exit confirmation was performed.
- A40: switching to the A42 disposable project showed indexing at 3 files / 7 of 8 symbols and LSP online. Save affinity, restart, synchronization races, stale replies, and bounded shutdown were not covered.
- A42: enabled Word Wrap and Format on Save; the preference file reflected both settings. A long comment visibly wrapped and function folding collapsed/expanded. Saving a deliberately noncanonical buffer displayed “Format on Save is unavailable for 'main.rocket'; saving without formatting.” The full editor preference/reopen and productivity matrix was not completed.
- A43: an unsaved disposable-buffer edit displayed the Yes/No/Cancel “Unsaved changes” prompt. Escape dismissed it; Ctrl+Z then restored the buffer. Earlier evidence recorded separate group carets and Function snippet insertion/undo. Tab/Enter snippet acceptance and the complete final-caret/undo matrix remain untested.
- A46: the format-on-save fallback and unresolved-call signature-help absence were observed. Debug locals remained unavailable with Win32 error 87/private-symbol requirement. Semantic-token provenance and the whole known-limit matrix remain unverified.
- A47: prior closure evidence recorded standalone-file mode and a second audit executable process without a second RocketIDE window. Missing compiler/LSP, offline behavior, and recovery degradation were not completed.
- A37: no stress workload or process resource measurements were taken; a repeatable workload and CPU/memory/latency telemetry over the defined duration are still needed.

## User-state restoration and ledger

- Restored `%LOCALAPPDATA%\RocketIDE` from `artifacts\deferred-acceptance\user-state-before\RocketIDE` after the audit UI was closed and the audit-owned processes were stopped.
- Compared against `user-state-before\manifest.json`: 71 expected files, 71 actual files, 0 path/length/SHA-256 mismatches, 0 extra files.
- Final matrix totals remain 26 PASS, 4 FAIL, 3 BLOCKED, 12 NOT RUN, 2 NOT APPLICABLE (47 total). The 12 NOT RUN rows are A27, A31, A33-A37, A40, A42, A43, A46, and A47.
- Existing BUG-001 through BUG-004 remain unchanged. A36 shutdown observation is partial and does not alter the finding ledger without a complete reproducible acceptance replay.