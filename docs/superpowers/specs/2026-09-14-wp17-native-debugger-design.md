# WP17 Native Debugger Design

## Goal

Complete IDE-WP17 with a real standalone Windows x64 Rocket debugger inside RocketIDE. The IDE consumes Rocket's existing `rocketc build --debug` artifacts and never duplicates compiler/LSP semantics.

## Existing Rocket contract

Rocket already emits unoptimized native executables for `--debug`, adjacent CodeView PDBs, and `rocket-source-map-1` sidecars. PDBs contain Rocket functions, executable line records, locals/constants, and native call-frame data. The frozen debug path uses logical `rocket:\source\<basename>.rocket` file records, so duplicate source basenames are rejected before launch. The sidecar remains the authoritative logical-to-workspace source identity map.

No Rocket compiler, runtime ABI, LSP, or source-map changes are part of WP17.

## Backend

Create `RocketIDE.Debugger`, targeting `net10.0-windows10.0.19041.0`, referencing Microsoft's `Microsoft.Debugging.Platform.DbgX` package. DbgX hosts DbgEng out-of-process through `EngHost.exe`. All DbgX interaction is isolated behind `IRocketNativeDebugger`; App/Core/Rocket code depends only on RocketIDE-owned debugger models.

The adapter enables DbgEng source-line support, configures the workspace source path, sets native source breakpoints using DbgEng source-line expressions, and uses the real engine for continue, pause, step-over, step-in, step-out, threads, stack frames, locals, and source location. DbgEng command output is parsed only as a transport representation of debugger-native data; it is never used to infer Rocket language semantics.

## Session flow

1. Save dirty documents.
2. Discover `rocketc` and the active Rocket executable target.
3. Run `rocketc build <target> --debug --message-format=json`.
4. Resolve the expected `.exe`, `.pdb`, and `.rocket.map.json` artifacts.
5. Parse and validate the sidecar, resolve all sources, and reject missing or duplicate-basename mappings.
6. Create the DbgX engine/session.
7. Configure line/source support and launch the executable under DbgEng.
8. Bind stored Rocket source breakpoints.
9. Continue to the first user breakpoint or program termination.
10. On every stop, refresh real threads, top call stack, locals, and source position and navigate the editor to that position.

Only one debug session may exist at a time. Normal Run/Build/Test commands are disabled while debugging. App/workspace shutdown stops the session.

## UI

Add real debugger commands:

- F5: Start Debugging / Continue
- F9: Toggle Breakpoint
- Shift+F5: Stop Debugging
- F10: Step Over
- F11: Step Into
- Shift+F11: Step Out
- Pause command/button while running

Add a Debug bottom tab containing Threads, Call Stack, and Locals views. The panels are backed only by live debugger data and expose explicit idle/running/stopped/terminated/error states. Double-clicking a Rocket stack frame navigates to its exact resolved source line.

Breakpoint state is stored by full workspace source path + one-based line. Editor rendering shows breakpoints lexically in the left margin without changing the document.

## Safety and degradation

- Executable targets only; libraries cannot start a debug session.
- Reject missing/malformed artifacts and ambiguous duplicate basenames before engine launch.
- No command shell construction from arbitrary user text. Debugger commands are generated from validated numeric IDs/lines and resolved source basenames.
- No extension loading or arbitrary debugger-console UI is exposed.
- Debugger failure never damages Build/Run/LSP; failures terminate only the debug session and are reported in Output.
- Optimized builds are not used for Start Debugging. Locals absent from PDB are displayed as unavailable rather than invented.
- Interactive stdin is not part of WP17, matching Rocket's existing Visual Studio debug workflow; stdout/stderr/debugger output is surfaced where DbgEng exposes it.

## Tests

Add a dedicated `RocketIDE.Debugger.Tests` project. Unit tests cover source-map resolution, breakpoint command encoding, DbgEng output parsing, session state transitions, duplicate basenames, malformed artifacts, and command availability. App tests cover debugger command wiring and navigation. Windows verification must build/test/publish. The final manual/Codex acceptance uses a tiny real Rocket program to prove breakpoint binding, continue, stepping, call stack, locals, threads, and stop.

## Acceptance

WP17 is implemented only when RocketIDE has a real DbgEng-backed session with no cosmetic fake debugger paths, automated verification is green, and the remaining live Rocket smoke test is explicitly identified for the final GUI/Codex pass.
