# Native debugger architecture

## Decision

Native debugging is supported without changing the Rocket compiler, Rocket LSP, runtime ABI, or `rocket-source-map-1` format. Rocket already owns the native debug contract: an unoptimized `rocketc build ... --debug` produces a Windows executable, CodeView PDB, and Rocket source-map sidecar. The existing Rocket Visual Studio integration is evidence that those artifacts carry source-line, stack-frame, thread, and local-variable information suitable for native debugging.

RocketIDE therefore consumes Rocket's existing artifacts rather than implementing Rocket semantics locally.

## Backend choice

RocketIDE uses Microsoft's `Microsoft.Debugging.Platform.DbgX` / DbgEng stack. The pinned DbgX package targets .NET 10/Windows and hosts the native engine out of process through `EngHost.exe`. `Microsoft.Debugging.Platform.SymSrv` supplies the redistributable engine-side files used by the portable application. The debugger backend is isolated in `src/RocketIDE.Debugger`; WPF, compiler, and LSP projects depend only on RocketIDE-owned debugger contracts.

Alternatives were rejected for the current Windows-only IDE:

- `lldb-dap`: attractive protocol boundary, but weaker fit for the already-proven Windows CodeView/PDB Rocket contract.
- Raw Win32 debugging or direct DbgEng COM interop: unnecessary engine plumbing and a much larger maintenance/safety surface.
- Visual Studio automation/embedding: violates the standalone portable IDE requirement.
- Cosmetic debugger UI: explicitly outside the product contract.

## Implemented architecture

The implementation adds:

- `RocketIDE.Debugger` and `RocketIDE.Debugger.Tests` projects.
- `DbgXCommandTransport`, which owns the public DbgX `DebugEngine` on a dedicated synchronization context, `.noshell` hardening, command execution, debugger output, native pause, and shutdown. DbgX itself owns the out-of-process `EngHost.exe` lifecycle; RocketIDE verifies the required engine assets at publish/package time.
- `RocketNativeDebugger`, which owns session state and exposes launch/stop, continue/pause, step over/in/out, live breakpoints, threads, call stack, locals, current source location, and output through RocketIDE-owned models.
- `RocketDebugSourceMap`, which reads only the frozen `rocket-source-map-1` identity contract and rejects missing/ambiguous source basenames instead of guessing.
- `DbgEngProtocol`, which is the only place that builds/parses the small set of native debugger commands RocketIDE requires. No raw debugger console is exposed.
- A real `rocketc build --debug --message-format=json` launch path. RocketIDE consumes the compiler-reported executable artifact and derives only its required adjacent `.pdb` and `.rocket.map.json`, then passes them through the existing `RocketDebugArtifactValidator`.
- WPF debugger commands and presentation for F5, Pause, Shift+F5, F9, F10, F11, Shift+F11, breakpoint/current-line markers, Threads, Call Stack, Locals, and debugger output. Panels are populated only from a live backend snapshot.

## Source identity and safety

The current Rocket source-map/PDB contract identifies sources by basename in debugger-facing locations. RocketIDE resolves every mapped source against the active Rocket source root before launch and rejects duplicate basenames. Breakpoints are stored as absolute Rocket source path + one-based line, then translated to the debugger's basename/line identity only after source-map validation.

The debugger never changes the Rocket compiler or invents source locations. Build/debug artifacts remain authoritative. `.noshell` disables debugger shell execution; RocketIDE exposes no arbitrary command entry point. Opening a workspace still never launches user code: starting a debug target is an explicit user command.

## Distribution

DbgX uses `EngHost.exe` as a real child process. The native debugger requires the portable release from single-file self-extraction to a self-contained **multi-file** `win-x64` folder/ZIP. No .NET runtime, Visual Studio, or separately installed WinDbg is required, but the application directory must remain intact. Verification/package scripts fail if `RocketIDE.Debugger.dll` or the x64 `EngHost.exe` is missing.

## Current verification and supported limits

The native workflow was validated in [debugger validation](debugger-validation-2026-09-27/DEBUGGER_VALIDATION_REPORT.md), including breakpoints, stepping, threads/stack/locals, run-to-cursor, restart, stop and packaged launch. The full release gate passed 514/514 tests; [release acceptance](release-1.0.0/ACCEPTANCE_REPORT.md) records all current passes and accepted waivers.

Watches/Evaluate accepts ASCII local or parameter identifiers only, limited to 128 characters, 16 watches, 4096 result characters and a two-second inspection deadline. Calls, operators, member access and native debugger commands are rejected. Values come from the stopped native frame; running or stale requests cannot repopulate inspection after a session change.

Duplicate Rocket source basenames remain unsupported under the frozen source-map identity contract and are rejected before launch. The portable app includes the native debugger engine and must remain a complete multi-file directory.
