# RocketIDE confirmed deferred-acceptance bug repair

## Authorization and objective

The user explicitly authorized the next implementation turn to fix all confirmed bugs from the 2026-09-26 deferred acceptance audit. This prompt authorizes diagnosis, narrow product changes, focused regression tests, and verification for BUG-001 through BUG-004. Do not stop at a repair proposal.

Use a capable higher-reasoning model. Read this prompt, `BUG_REPORTS.md`, `DEFERRED_TEST_MATRIX.md`, `evidence/closure-2026-09-26.md`, and `FIX_HANDOFF.md` in full before making changes. The original audit prompt at `C:\Users\Administrator\.codex\visualizations\2026\09\25\01a0d930-5d27-7da2-bfe5-ccfafd2b4f16\ROCKETIDE_ALL_DEFERRED_ACCEPTANCE_PROMPT.md` governed the previous report-only audit; this repair task is separately authorized by the user's subsequent request.

## Scope

Repair exactly these four reproduced product defects:

1. **BUG-001 / A15 — Run output.** IDE Run completed without showing fixture stdout (`audit fixture`) or a final Run exit result in Output, although direct compiler Run printed the text and exited 0.
2. **BUG-002 / A16 — Test result state.** IDE Test on the no-tests fixture received compiler R5001 / exit 2, but the Tests panel remained empty and said “Test run started…” after controls returned to idle.
3. **BUG-003 / A21 — Quick Fix.** On the exact audit app/LSP versions, an indexed public `doubled` declaration in `math.rocket` did not produce a server-provided Quick Fix for the R4002 missing-name diagnostic in `main.rocket`.
4. **BUG-004 / A22 — Format Document.** On the exact audit app/LSP versions, Edit → Format Document left valid but noncanonical Rocket source unchanged.

Reproduce each defect on an unchanged build before editing. Use the exact app, compiler, and LSP identities plus disposable fixture locations in the matrix and closure evidence. Inspect the full app-to-process/LSP request/response path before assigning root cause. Preserve the audit fixture originals and the user's existing `%LOCALAPPDATA%\RocketIDE` state; use backups and verify restoration.

Work on an issue-specific isolated branch/worktree based on audited RocketIDE source SHA `f0e37d9ee9f14a0e5139de484883e0c985687da1`. Do not alter the existing `codex/rocketide-deferred-acceptance` evidence worktree or its uncommitted audit artifacts. Keep changes in the RocketIDE repository when possible. If BUG-003 or BUG-004 is proven to require a change in the companion Rocket compiler/LSP, isolate that work in a separate issue-specific branch/worktree; do not modify or disturb the existing Rocket WP01A/WP01B or `rocket-lsp-fuzzy-symbols` lanes. Record repository, branch, exact dependency SHA, tests, and integration implications for every such change.

No push, merge, release, CI trigger, public issue, or external message is authorized. Do not begin WP06 implementation in this repair task.

## Repair requirements

- Trace every finding to its actual failure boundary. Do not suppress diagnostics, hide errors, or invent a success summary to make the UI look complete.
- Preserve truthful terminal states and streamed output for success, nonzero exit, cancellation, and process-launch failure. Specifically ensure Run stdout/stderr and final exit status are visible in Output.
- Ensure Test always leaves a terminal, truthful result when a process exits without the usual summary payload, including structured compiler diagnostics such as R5001. A subsequent run must clear/replace stale rows and status.
- For Quick Fix and formatting, inspect whether request construction, document/version/range mapping, server capability/response, and UI edit application are correct. Apply edits safely and on the intended buffer; preserve unsaved changes and cancellation behavior.
- Add narrowly scoped regression coverage for all four bugs. Keep existing tests and acceptance criteria intact; do not broaden scope into unrelated deferred rows.
- Preserve Stop/process-tree ownership, diagnostic routing, LSP operation cancellation, menu/command availability, package contents, and existing user state.

## Verification

1. Reproduce all four issues on the unmodified build and save fresh evidence.
2. Add and run focused regression tests that fail before and pass after each fix.
3. Run `scripts/verify.ps1` from the clean isolated RocketIDE repair worktree. Report exact test counts, warnings/errors, publish result, and debugger asset guards.
4. Rebuild/repackage to a new unique output directory; verify package SHA-256 and required `RocketIDE.exe`, `RocketIDE.Debugger.dll`, and x64 `amd64/EngHost.exe` assets.
5. Replay all four GUI repros on the repaired binary and report what is directly observed. A test suite pass does not replace GUI verification.
6. Keep clean-machine, alternate-DPI, full accessibility, broad stress, complete debugger, and other matrix gaps at their current outcomes unless actually executed with direct evidence. Do not claim full deferred acceptance or release certification.

## Deliverables

Write a concise repair report with per-bug root cause, changed files, regression tests, GUI replay evidence, full-gate/package results, exact source and dependency SHAs, remaining risks, and a WP06 readiness recommendation. Recommend moving to WP06 only after all four confirmed bugs pass the repaired GUI replay and focused/full verification. State clearly that this is a bug-repair checkpoint, not full historical acceptance or release certification.
