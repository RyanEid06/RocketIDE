# WP17 Native Debugger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a real standalone DbgEng-backed Rocket debugger to RocketIDE without changing the Rocket compiler/LSP contract.

**Architecture:** A new isolated `RocketIDE.Debugger` project wraps Microsoft DbgX and exposes RocketIDE-owned session models. RocketIDE.Rocket remains responsible for compiler/target/artifact discovery; the WPF app coordinates debug build, validated artifacts, backend state, and debugger panels.

**Tech Stack:** .NET 10, WPF, MSTest, Microsoft.Debugging.Platform.DbgX, existing Rocket compiler/PDB/source-map contract.

**Spec:** `docs/superpowers/specs/2026-09-14-wp17-native-debugger-design.md`

## Global Constraints

- Windows x64 standalone debugger only.
- Do not change Rocket compiler, runtime ABI, LSP, or `rocket-source-map-1`.
- Never synthesize Rocket semantics in the IDE.
- No fake debugger UI: panels reflect only backend data.
- Reject missing/ambiguous source mappings before launch.
- Keep debugger complexity isolated from Core/LSP/compiler services.

---

### Task 1: Project and debugger contracts
- [ ] Add `RocketIDE.Debugger` and `RocketIDE.Debugger.Tests` projects to the solution.
- [ ] Pin DbgX centrally and reference it only from the debugger project.
- [ ] Add debugger state, breakpoint, thread, frame, variable, launch, stop-location, and backend interfaces.
- [ ] Add baseline tests proving project boundaries and model invariants.

### Task 2: Rocket source-map model
- [ ] Add a strict `rocket-source-map-1` parser/resolver that produces unique logical basename -> full workspace path mappings.
- [ ] Reject missing source files and duplicate basenames.
- [ ] Add tests for valid maps, malformed JSON/schema, missing files, and ambiguity.

### Task 3: DbgEng command transport
- [ ] Add an isolated DbgX engine host that loads `DebugEngine`, sends engine requests, captures DML output, and shuts down deterministically.
- [ ] Add safe command builders/parsers for source breakpoints, threads, stack, locals, and current source line.
- [ ] Add parser/encoding tests; never expose a raw debugger console.

### Task 4: Native debug session
- [ ] Implement launch, stop, continue, pause, step-over, step-in, and step-out state transitions.
- [ ] Bind/rebind source breakpoints and refresh real threads/frames/locals after stops.
- [ ] Emit output/state/stopped/terminated events.
- [ ] Add fake-transport state-machine tests.

### Task 5: Rocket debug build/artifacts
- [ ] Extend Rocket command building with `--debug` structured build support without changing ordinary Build.
- [ ] Resolve `.exe`, `.pdb`, `.rocket.map.json` after a successful debug build and reuse/strengthen existing validation.
- [ ] Add Rocket-layer tests for debug build arguments and artifact resolution.

### Task 6: WPF debugger integration
- [ ] Add `DebugViewModel` and wire F5/F9/Shift+F5/F10/F11/Shift+F11 plus Pause through the command registry.
- [ ] Add Debug menu/toolbar entries and a real Debug bottom tab for Threads, Call Stack, and Locals.
- [ ] Navigate stopped frames through existing document navigation.
- [ ] Stop the debugger on workspace/app shutdown and disable conflicting Rocket commands while the debugger is active.
- [ ] Add App tests for command states and source wiring.

### Task 7: Breakpoint editor presentation
- [ ] Add lexical breakpoint marker storage/rendering keyed by full path + one-based line.
- [ ] Make F9 toggle the active caret line and synchronize live sessions.
- [ ] Add rendering/toggle tests.

### Task 8: Distribution, docs, and verification
- [ ] Ensure package/publish output contains the debugger project and DbgX/EngHost assets.
- [ ] Update `DEBUGGER_FEASIBILITY.md`, README, ROADMAP, and distribution docs to record implemented WP17 and the remaining live smoke matrix.
- [ ] Add package tests for debugger assets.
- [ ] Run `scripts/verify.ps1`, publish smoke, then the live tiny-Rocket breakpoint/step/locals acceptance on Windows.
