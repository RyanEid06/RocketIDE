# FINAL-WP06 implementation plan

The user has authorized this plan's scope through the WP06 start prompt. Entry reconciliation and the prerequisite gate are recorded in the execution evidence; no push, merge, release work or WP07 is authorized.

## Evidence-led design

The live foundation replay established source breakpoints, stepping, thread/frame inspection and bounded exit. Repairs load the target PDB, report deferred/ambiguous binding honestly, configure the actual DbgX working-directory option, restrict symbols to the matching local artifacts and reset frame scope when selecting a thread. Native frames must not inherit a caller's source location, and exited process records must not be treated as live.

Real native evaluation supports simple local and parameter identifiers in the selected stopped frame. Aggregate members fail because the current PDB does not describe their fields. The UI will explicitly offer debugger identifiers, not Rocket language evaluation. It will reject operators, calls, members, commands and oversized input before transport. Missing and unavailable identifiers remain visible as unavailable results.

Use the existing DEBUG bottom panel with Inspection, Breakpoints and Watches/Evaluate tabs. Enabled breakpoints retain one source of truth in DebugViewModel; disabling removes the active editor glyph and shows Disabled in the panel. Bound/error state is refreshed from the native session; inactive sessions clear stale binding. Navigation remains available independently of live mutation.

All stopped inspection/mutation uses a single guarded backend operation with a two-second deadline. Up to 16 watches, 128 characters per identifier and 4096 characters per result. A watch refresh shares one deadline. Reject evaluation while running. UI requests carry a revision so a late result cannot appear after stepping, frame changes, Stop or Restart. Timeout aborts the owned session rather than leaving queued native requests alive. Stop remains available during inspection and has its own bounded cleanup, including engine-host disposal.

Restart snapshots the original input target and tool settings, stops/disposes the old session, rebuilds with that configuration and reapplies current user breakpoints. It does not follow a newly selected editor. Run to Cursor uses a native one-shot breakpoint and removes any remaining temporary breakpoint on every stop, cancellation, Stop, Restart or fault; user breakpoints remain separate.

## Ordered checkpoints

1. Add failing protocol/backend regressions for exited records, frame attribution, disabled breakpoints, evaluation validation/state/timeout and temporary-breakpoint cleanup. Implement and pass them.
2. Add failing view-model/command regressions for all seven states, watch limits/staleness, breakpoint changes and restart identity. Implement the panel and command wiring with async handlers and no UI-thread blocking.
3. Run affected suites; publish a new intermediate package and directly exercise features. Repair demonstrated defects before full verification.
4. Run scripts/verify.ps1, package into a unique final directory and record hashes for the apphost, product DLL, debugger DLL and EngHost.
5. Replay mandatory debugger smoke and every WP06 feature, including cancellation/fault/slow evaluation and repeated shutdown; replay BUG-001 through BUG-004 using the companion LSP. Record observed versus unobserved cases explicitly.
6. Fresh independent review, remedy findings, restore the user's profile with exact manifest verification, verify original lanes/audit unchanged, retain evidence, and commit explicit paths locally only after acceptance. Commit subject: complete RocketIDE debugger workflow.

Aggregate field debug information remains an upstream enhancement outside this task; useful identifier evaluation does not depend on it.
