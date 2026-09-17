# RocketIDE Final Roadmap Implementation

> **Status:** Approved final-cycle roadmap. Execute exactly one named package or
> checkpoint at a time. Every package requires its own tests, review, manual
> evidence where applicable, focused commit, push, and SHA synchronization proof.
> A later package is not implicitly authorized by completing an earlier one.

**Goal:** Finish RocketIDE as the focused, reliable primary IDE for Rocket and
Scroll2Roll development, then stop speculative feature expansion. The final
product must provide fast authoritative language feedback, bounded shutdown,
complete everyday navigation and editing, useful native debugging, safe recovery,
and reproducible release evidence.

**Architecture:** RocketIDE remains a Windows-native .NET 10 WPF/AvalonEdit
application. Rocket compiler and `rocket-lsp` remain the sole authorities for
Rocket parsing, semantic analysis, diagnostics, formatting, symbols, folding,
navigation, refactoring, and other language intelligence. RocketIDE consumes
typed protocols and owns presentation, document/view coordination, process
lifecycle, recovery, and debugger workflow.

**Historical baseline:** Root `ROADMAP.md` records IDE-WP00 through IDE-WP17.
Those work packages are historical and are not renumbered or reopened by this
cycle.

## 1. Frozen starting state

Record these values again at execution time before creating a branch. If they
have changed, stop and reconcile the plan instead of silently using stale SHAs.

| Repository/lane | Branch | Approved baseline on 2026-09-17 |
|---|---|---|
| Rocket shared default | `master` | `10f295dd000b93fe50d254be21fedffd17892aeb` |
| Rocket 3.5 / Eddie | `rocket35/eddie` | `2a03b7358399d70efac5a9bb056851c0ff3521d4` |
| RocketIDE shared default | `main` | `d73b5c3c70bea547542a14ef2c2740e0a8184e37` |

The Eddie branch already exists locally and remotely. Do not create a second
`work/rocket-3.5` branch and do not describe Eddie's branch as unpushed.

Recommended new branches/worktrees:

```text
Rocket repository:
  Ryan FINAL-WP01: codex/rocketide-final-wp01-lsp
  Eddie Rocket 3.5: rocket35/eddie              # existing branch/worktree

RocketIDE repository:
  Ryan FINAL-WP02 through FINAL-WP07: codex/rocketide-final-wp02-wp07
```

Use distinct build directories for every Rocket worktree. Never share CMake
output between the Rocket LSP lane and Rocket 3.5 lane.

## 2. Final-cycle rules

1. Never recreate Rocket language semantics inside RocketIDE.
2. Never modify Rocket source from a RocketIDE package.
3. Never mix Rocket 3.5 rendering/runtime work into FINAL-WP01.
4. Preserve existing behavior unless a requirement explicitly changes it.
5. Start every behavior change with a failing focused test.
6. Keep commits focused; stage explicit paths and never use `git add -A`.
7. Run focused tests, affected regression tests, formatting/diff checks, and the
   package's full gate before committing.
8. Manual/native acceptance requires direct observation. Automation or process
   startup alone is not GUI evidence.
9. An upstream or environment blocker must be recorded with exact reproduction;
   it is never silently counted as a pass.
10. After each pushed package, prove local HEAD, tracking branch, and live remote
    SHA match and report ahead/behind state.
11. Do not add a Git GUI, plugin marketplace, embedded AI assistant, full
    terminal emulator, alternate Rocket parser/compiler/LSP, arbitrary editor
    grids, or unrelated Visual Studio features.
12. Opening a workspace or source file must never execute Rocket/native code or
    package scripts.

## 3. Mandatory entry gate

Before production changes in FINAL-WP01 or FINAL-WP02, capture the current
baseline. This is a preflight gate, not a new feature package and not permission
to fix failures outside the active package.

### Automated baseline

Run in RocketIDE:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

The accepted 2026-09-17 baseline is:

- .NET SDK 10.0.401;
- Release build: 0 warnings, 0 errors;
- tests: 350 passed, 0 failed, 0 skipped;
- self-contained win-x64 publish succeeded;
- published `RocketIDE.exe`, `RocketIDE.Debugger.dll`, and x64/amd64
  `EngHost.exe` exist and are non-empty.

NuGet `NU1301`, `NU1801`, or `NU1900` caused by restricted TLS/network access is
environment evidence, not source evidence. Retry the official gate with normal
NuGet access; do not weaken repository warning/audit policy to hide it.

### Existing-behavior smoke

Attempt and record before adding features:

- launch the current published IDE;
- configure/discover the real Rocket SDK;
- open a real multi-file workspace;
- Check, Build, Run, Test, and Stop;
- navigation/refactoring/formatting against the real LSP;
- workspace search/replace including a dirty buffer;
- forced recovery/external-change behavior;
- current native debugger launch, breakpoint, continue, pause, stepping,
  threads, stack, locals, output, stop, and post-debug shutdown.

If native GUI automation is unavailable, record `MANUAL SMOKE DEFERRED` with the
exact bridge/tool failure. Production work may continue only when the failure is
external and the affected behavior is repeated at the earliest available manual
checkpoint. A demonstrated product defect must be triaged before building on the
affected subsystem.

## 4. Package schedule

```text
Rocket — Ryan
FINAL-WP01A  LSP worker, telemetry, fixtures, discovery cache
      |
FINAL-WP01B  dependency-aware incremental analysis + protocol prerequisites
      |
      +--> merge to Rocket master only after full Rocket gates

RocketIDE — Ryan
FINAL-WP02  lifecycle, synchronization, shutdown
      |
FINAL-WP03  symbols, Outline, Command Palette
      |
FINAL-WP04  editor/workspace quality-of-life
      |
FINAL-WP05  controlled two-group split + bundled snippets
      |
FINAL-WP06  debugger proof and completion
      |
FINAL-WP07  deferred acceptance and final release gate

Rocket — Eddie
rocket35/eddie proceeds in its isolated worktree
      |
      +--> integrate/rebase after FINAL-WP01 lands
      |
      +--> full Rocket verification and final toolchain artifact
```

FINAL-WP01 and FINAL-WP02 through FINAL-WP06 may proceed in parallel because
they use different repositories/worktrees. FINAL-WP07 waits for the final Rocket
toolchain containing both FINAL-WP01 and the accepted Rocket 3.5 integration.

All known Rocket protocol prerequisites for FINAL-WP03 through FINAL-WP06 must
be audited during FINAL-WP01. If a genuinely new prerequisite is discovered
after FINAL-WP01 is merged, use a new focused Rocket branch and compatibility
gate; do not silently reopen or mutate the closed FINAL-WP01 branch.

---

# FINAL-WP01 — Incremental Rocket Analysis and LSP Prerequisites

## Goal

Replace synchronous broad workspace rebuilds on normal edits with coalesced,
dependency-aware analysis while preserving compiler-equivalent results and
keeping the protocol loop responsive.

## Ownership

- Repository: `Rocket`
- Branch: `codex/rocketide-final-wp01-lsp`
- Base: freshly verified `master`; expected approved baseline is
  `10f295dd000b93fe50d254be21fedffd17892aeb`
- Forbidden: RocketIDE changes and Rocket 3.5 rendering/runtime changes

## Confirmed current behavior

The current server handles JSON-RPC messages on one synchronous loop.
`textDocument/didChange` mutates the overlay and calls `rebuildSnapshot()`
before the server can read another message. `rebuildSnapshot()` rediscovers
workspace sources, lexes/parses the snapshot, loads module graphs, performs
semantic analysis, and publishes diagnostics. `didSave` performs another
rebuild even when its text already matches the analyzed overlay.

Generation numbers alone cannot solve stale work: a newer edit or cancellation
cannot be received while the synchronous rebuild blocks the protocol loop.

## FINAL-WP01A — Responsive analysis infrastructure

### Required implementation

1. Add a dedicated analysis worker/queue behind the protocol session.
2. Keep JSON-RPC input/output responsive while analysis runs.
3. Capture immutable analysis inputs: workspace/configuration generation,
   document overlays and versions, discovery state, and dependency roots.
4. Coalesce rapid edits so only the newest required state is analyzed.
5. Use monotonically increasing generations and publish diagnostics/snapshots
   only when the completed generation is still current.
6. Make shutdown cancel queued work and join the worker within a bounded time.
7. Cache workspace discovery and invalidate it only for workspace-folder,
   manifest/project, watched create/delete/rename, configuration, or toolchain
   changes.
8. Replace misleading telemetry with real counters for generation, reparsed
   files, semantically analyzed files, invalidated files, cache hits/misses,
   elapsed time, and stale/cancelled generations.
9. Make unchanged `didSave` a no-op for semantic analysis. A changed save must
   enter the same generation pipeline as other authoritative source changes.
10. Preserve protocol framing and serialize all output safely.

### Required tests

- the server accepts a newer edit while an older fake analysis is blocked;
- only the newest generation publishes diagnostics;
- a rapid burst is coalesced and does not create an unbounded queue;
- shutdown completes with queued/running fake analysis;
- unchanged save does not launch a second semantic pass;
- discovery cache invalidates only on defined events;
- telemetry equals instrumented work rather than root-count estimates.

### Checkpoint gate

- protocol remains responsive during a controlled slow analysis;
- no stale diagnostic publication is possible;
- existing LSP tests pass;
- new concurrency tests pass under repeated execution;
- focused sanitizers/race-capable checks are used where supported;
- commit only FINAL-WP01A files with subject:

```text
add responsive LSP analysis scheduling
```

## FINAL-WP01B — Dependency-aware reuse and protocol completion

### Required implementation

1. Introduce focused workspace-state and dependency-graph components instead of
   expanding `language_server.cpp` into a larger monolith.
2. Maintain normalized module identity, source version/hash, tokens, parsed AST,
   imports, reverse dependencies, exported semantic/interface fingerprint,
   semantic result, and diagnostics where ownership permits safe reuse.
3. Reparse the changed module and invalidate only the required reverse-
   dependency closure.
4. Reuse unrelated module work.
5. Preserve a safe full-analysis fallback when cache invariants cannot be proven.
6. Compare incremental output with a clean full analysis for every correctness
   fixture.
7. Audit and test `workspace/symbol` and `rocket/projectStatus`.
8. Implement authoritative `textDocument/documentSymbol` and
   `textDocument/foldingRange` if absent, using compiler/parser structures rather
   than regex or editor heuristics.

The existing loader builds a flattened module graph and semantic pass. Any new
cache/reuse API must be an explicit compiler boundary with clear ownership and
lifetime; do not retain pointers into temporary parser or semantic state.

### Performance fixtures

Cover at minimum:

- one-file project;
- ordinary multi-file package;
- many unrelated modules;
- deep dependency chain;
- fan-in/fan-out graph;
- rapid edit burst;
- private function-body change;
- exported/public interface change;
- manifest/import edit;
- large source comparable to `compiler/src/main.rocket`.

### Acceptance

- typical normal-edit feedback is under 250 ms on the recorded reference machine;
- a large-workspace leaf edit with a small affected closure completes under one
  second on that machine;
- unrelated modules are not reparsed or semantically analyzed;
- incremental and clean-full diagnostics/symbol results are equivalent;
- rapid edits cannot create an obsolete backlog;
- document symbols and folding ranges pass real protocol tests;
- the complete required Rocket compiler/LSP/native matrix passes;
- benchmark environment, cold/warm results, SHAs, and raw counters are recorded;
- commit with subject:

```text
implement dependency-aware LSP analysis
```

After review and full verification, merge FINAL-WP01 into Rocket `master`. Prove
local, upstream, and live remote SHAs match before allowing Rocket 3.5 to update
onto the new baseline.

---

# FINAL-WP02 — Lifecycle, Synchronization, and Shutdown

## Goal

Guarantee correct document ordering and a single bounded application-exit
deadline even when DbgX, LSP, compiler, run, test, output, or persistence work is
hung.

## Requirements

1. Introduce an application-lifetime coordinator with one cancellation source.
2. Stop accepting new work when shutdown begins.
3. Give the complete exit path one monotonic deadline: goal two seconds normally,
   hard maximum five seconds under forced teardown.
4. Budget debugger, LSP, child-process, output-drain, and persistence cleanup
   inside that deadline; independent timeout layers may not accumulate past it.
5. Replace the debugger synchronization context's unbounded `Thread.Join()` with
   bounded teardown. Never use `Thread.Abort`.
6. Do not dispose queue/native state still in use by an abandoned worker; define
   a safe forced-detach path and ensure owned `EngHost.exe`/debuggee processes are
   terminated where ownership is known.
7. Apply explicit LSP startup, shutdown-request, process-exit, restart, and
   disposal deadlines. Kill the owned process tree after graceful timeout.
8. Ensure compiler/run/test processes remain in the existing Windows Job Object
   ownership model and are terminated on exit.
9. Add `FlushAsync(path, currentDocument, token)` or an equivalent serialized
   scheduler contract so save performs:

```text
flush newest pending didChange
-> await its serialized transport write
-> persist current document
-> send didSave for the same text/version
```

10. Serialize open/close, workspace switch, LSP restart, toolchain change, and
    shutdown through a focused coordinator rather than new scattered `async void`
    flows.
11. Batch high-volume output on the UI dispatcher, preserve order, bound history,
    and trim in chunks.

## Acceptance

- pending edit plus immediate save always sends matching `didChange` before
  `didSave`;
- a hung fake DbgX transport and a hung fake LSP cannot exceed the global exit
  deadline;
- active compiler/run/test/debug process trees are gone after exit;
- recovery/session persistence either completes inside its budget or records a
  clear bounded failure without blocking exit;
- rapid output does not perform one WPF collection mutation per line;
- focused tests and `scripts/verify.ps1` pass;
- manual normal/hung/active-process shutdown matrix is recorded;
- commit subject:

```text
harden RocketIDE lifecycle and shutdown
```

---

# FINAL-WP03 — Symbols, Outline, and Command Palette

## Goal

Add authoritative symbol navigation and one discoverable command surface without
duplicating actions or Rocket semantics.

## Requirements

1. Extend typed capability negotiation for workspace symbols, document symbols,
   and folding support.
2. Add a focused `SymbolClient` and typed DTOs for both flat `SymbolInformation`
   and hierarchical `DocumentSymbol` responses.
3. Add cancellable/fuzzy Go to Symbol in Workspace with bounded result handling.
4. Add Go to Symbol in File and an Outline tree from the same authoritative
   document-symbol result.
5. Discard stale symbol responses when active document/version changes.
6. Add a Command Palette over `RocketCommandRegistry`; execute existing command
   objects/state and show existing gestures rather than duplicating handlers.
7. Actually request `rocket/projectStatus`, parse the typed response, and render
   useful status while handling older/unsupported servers.
8. Add bounded back/forward location history for definition/reference/symbol
   jumps.

## Acceptance

- real workspace/file symbols navigate to exact LSP ranges;
- Outline and file-symbol picker agree;
- cancellation and stale responses cannot navigate the wrong document;
- Command Palette invokes representative editor, workspace, build, and debugger
  commands through the registry;
- no regex/source scan is used for symbol discovery;
- offline/older LSP states degrade cleanly;
- commit subject:

```text
add authoritative symbol navigation
```

---

# FINAL-WP04 — Editor and Workspace Productivity

## Goal

Complete the everyday editing workflow without adding another language engine.

## Requirements

1. Render LSP `textDocument/foldingRange` results in AvalonEdit; discard stale
   results and never infer Rocket block structure in the IDE.
2. Add Toggle Line Comment using stable Rocket editor configuration only.
3. Add duplicate, move up/down, and delete line/selection with correct selection
   preservation and one logical undo unit.
4. Maintain bounded Reopen Closed Editor history.
5. Add Zoom In, Zoom Out, Reset Zoom, and persisted font-size baseline.
6. Add persisted word-wrap preference.
7. Add opt-in Format on Save using this exact ordering:

```text
flush pending changes
-> request authoritative format
-> validate/apply edits transactionally
-> synchronize formatted version
-> save
-> didSave
```

8. Persist only explicit editor preferences: zoom/font size, word wrap, format on
   save, and line-number visibility if made configurable.
9. Add Collapse All and Reveal Active File without destroying unrelated Explorer
   expansion state.
10. Move Quick Open discovery/ranking off the UI thread, with cancellation,
    bounded results, fuzzy ranking, recent/open weighting, and existing generated
    directory exclusions.

## Acceptance

- all editing commands undo in one step and preserve selections;
- preferences survive restart with sane defaults;
- Format on Save cannot lose text or violate LSP ordering;
- Quick Open remains interactive on the recorded large Rocket workspace;
- Explorer reveal expands only the required ancestors;
- focused tests and manual editor smoke pass;
- commit subject:

```text
complete RocketIDE editor productivity
```

---

# FINAL-WP05 — Controlled Split Editing and Bundled Snippets

## Goal

Support at most two editor groups and a small compiler-tested bundled snippet
catalog without introducing arbitrary layouts, user plugins, or duplicate dirty
buffers.

## Scope

- supported layouts: one group, vertical split, horizontal split;
- user-defined snippet files are out of scope for this final cycle;
- recursive/unlimited grids are out of scope.

## Requirements

1. Keep document text/version/dirty state single-source-of-truth in the existing
   document store/view model.
2. Introduce explicit editor-group and editor-view state. Each view owns caret,
   selection, scroll position, folding state, focus, and selected tab; two views
   of one document share text and dirty state only.
3. Add Split Right, Split Down, Close Group, Focus Next Group, and Move Active
   Editor to Other Group.
4. Route editor commands/navigation predictably to the focused group, preferring
   an existing target view when appropriate.
5. Persist split orientation, group membership, selected tab per group, and
   per-view state without duplicating recovered unsaved text.
6. Add a lightweight bundled snippet engine supporting trigger/name,
   description, body, numbered tab stops, and `$0`.
7. Source every Rocket snippet from current syntax/examples and compile-test
   complete constructs with the authoritative compiler.
8. Expose snippets through completion and/or Command Palette using one insertion
   implementation and one-step undo.

## Acceptance

- vertical/horizontal split and collapse to one group work;
- edits in either view update the same dirty buffer;
- closing one view cannot discard another view's unsaved document;
- focus-sensitive commands act on the correct view;
- session/recovery restore contains exactly one unsaved text source;
- completion, hover, diagnostics, folding, and debugger markers work in both
  views;
- snippets traverse tab stops, cancel safely, and undo in one step;
- commit subject:

```text
add controlled split editing and snippets
```

---

# FINAL-WP06 — Debugger Proof and Completion

## Goal

Prove the existing DbgX path first, then add only debugger features supported by
the real Rocket debug artifacts.

## Mandatory live proof before feature work

With a tiny real Rocket `--debug` program, prove artifact discovery, launch,
breakpoint binding, continue, pause, step over/in/out, threads, call stack,
locals, output, stop, and bounded post-debug IDE shutdown. Fix demonstrated
foundation defects before adding UI.

## Expression feasibility gate

Before promising Watches or Evaluate:

1. probe the exact expression forms supported by current Rocket PDB/debug info
   through DbgEng;
2. test at least local identifiers, parameters, simple member access, invalid
   input, unavailable symbols, and evaluation while running;
3. document the supported subset as debugger expressions, not full Rocket
   language evaluation;
4. if useful evaluation cannot be supported without compiler/debug-info changes,
   record a separate upstream Rocket blocker and do not ship deceptive UI.

## Requirements after the gates pass

1. Add a Breakpoints panel with enabled state, source, line, bound/unbound/error
   state, enable/disable, remove, remove all, and navigation.
2. Keep editor markers and panel state synchronized.
3. Add bounded Watches using the proven evaluation subset; refresh only while
   stopped and after step/frame changes.
4. Add Evaluate using the same backend and validation as Watches.
5. Add Restart Debugging with bounded stop, same target/configuration, appropriate
   rebuild behavior, and restored user breakpoints.
6. Add Run to Cursor through a temporary one-shot breakpoint and guarantee its
   removal on hit, cancel, stop, restart, or fault.
7. Audit command state for idle, launching, running, stopped, terminating,
   terminated, and faulted sessions.

## Acceptance

- the mandatory original debugger smoke passes before and after changes;
- no old debuggee or `EngHost.exe` leaks after Stop/Restart/exit;
- breakpoint panel and glyph state agree;
- Watches/Evaluate never claim unsupported Rocket semantics;
- slow evaluation does not freeze WPF;
- Run to Cursor leaves no hidden breakpoint;
- FINAL-WP02 shutdown deadline holds after heavy debugging;
- commit subject:

```text
complete RocketIDE debugger workflow
```

---

# FINAL-WP07 — Final Acceptance and Release

## Goal

Close all deferred evidence, fix only demonstrated defects, and produce a
reproducible final RocketIDE release against the integrated Rocket toolchain.

No new large feature is introduced here.

## Required integrated inputs

Record before testing:

- RocketIDE commit SHA;
- Rocket commit SHA containing FINAL-WP01 and accepted Rocket 3.5 integration;
- Rocket toolchain version and package SHA-256;
- Windows version, CPU, memory, storage class, DPI/display setup;
- UTC/local timestamp and whether each measurement is cold or warm.

## Automated gate

Run `scripts/verify.ps1` with ordinary NuGet restore/audit access, then
`scripts/package.ps1`. Record per-project test counts, total failures/skips,
published asset checks, ZIP filename/size, and SHA-256.

## Deferred historical acceptance

Directly exercise and record:

- IDE-WP09 navigation, references, rename, code actions, formatting;
- IDE-WP10 Check/Build/Run/Test/Stop;
- IDE-WP11 dirty-buffer search/replace, conflicts, cancellation, stress;
- IDE-WP12 >4 MiB and large-workspace responsiveness;
- IDE-WP13 configured-SDK advanced commands;
- IDE-WP14 forced crash, recovery decisions, external changes;
- IDE-WP15 clean-machine portable launch;
- IDE-WP16 100/125/150/200% DPI, keyboard, accessibility;
- IDE-WP17 real DbgX workflow;
- every FINAL-WP02 through FINAL-WP06 user-visible feature.

## Performance and stress

1. Measure initial analysis, syntax/semantic edit latency, rapid-edit backlog,
   memory/CPU stability, files reparsed/reanalyzed, Quick Open, and symbols.
2. Repeat shutdown while idle, LSP starting/online/hung, build/run/test active,
   debugger running/stopped/hung, large output active, dirty files open, and split
   editor active. No run may exceed the five-second forced deadline.
3. Exercise dirty close, workspace switch, external modification, crash recovery,
   forced kill, split-view recovery, format-on-save failure, and failed workspace
   edit rollback. Zero silent data loss is allowed.

## End-to-end workflow

On a representative clean Windows x64 environment:

1. unpack and launch the final package;
2. configure the exact recorded Rocket SDK;
3. open a real multi-file workspace;
4. Quick Open, workspace symbol, file symbol, Outline, and navigation history;
5. edit with fast diagnostics, completion, hover, signature help, folding, and a
   bundled snippet;
6. split the editor and edit one shared document safely;
7. format, rename, search/replace, build, run/stop, and test;
8. debug with breakpoints, Watches/Evaluate when supported, Run to Cursor,
   Restart, and Stop;
9. close within the deadline;
10. reopen and verify session/recovery state.

## Final release decision

RocketIDE is finished only when:

- no critical/high defect remains;
- every requirement has evidence or an explicit accepted upstream limitation;
- no dead UI or placeholder represents unsupported behavior;
- no IDE-side Rocket semantic fallback exists;
- README, integration contract, distribution documentation, shortcuts, and
  root roadmap match the product;
- RocketIDE feature branch is merged to `main` only after this gate passes;
- local `main`, `origin/main`, and live remote SHA match with zero ahead/behind;
- the final release artifact and checksum are retained.

Suggested final commit subject:

```text
finalize RocketIDE release hardening
```

## 5. Definition of future work

After FINAL-WP07, RocketIDE is feature-complete for current Rocket and Scroll2Roll
needs. New roadmap work requires observed usage evidence. Conditional/data
breakpoints, advanced visualizers, user snippet files, more than two editor
groups, plugins, Git UI, or specialized future project tooling are not blockers
for this release.

The final priority remains: fast authoritative feedback, reliable shutdown,
safe editing and recovery, useful debugging, and no hidden architectural hacks.
