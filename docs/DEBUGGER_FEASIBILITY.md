# WP17 standalone debugger feasibility and implementation

## Decision

WP17 is feasible without changing the Rocket compiler, Rocket LSP, runtime ABI, or `rocket-source-map-1` format. Rocket already owns the native debug contract: an unoptimized `rocketc build ... --debug` produces a Windows executable, CodeView PDB, and Rocket source-map sidecar. The existing Rocket Visual Studio integration is evidence that those artifacts carry source-line, stack-frame, thread, and local-variable information suitable for native debugging.

RocketIDE therefore consumes Rocket's existing artifacts rather than implementing Rocket semantics locally.

## Backend choice

RocketIDE uses Microsoft's `Microsoft.Debugging.Platform.DbgX` / DbgEng stack. The pinned DbgX package targets .NET 10/Windows and hosts the native engine out of process through `EngHost.exe`. `Microsoft.Debugging.Platform.SymSrv` supplies the redistributable engine-side files used by the portable application. The debugger backend is isolated in `src/RocketIDE.Debugger`; WPF, compiler, and LSP projects depend only on RocketIDE-owned debugger contracts.

Alternatives were rejected for the current Windows-only IDE:

- `lldb-dap`: attractive protocol boundary, but weaker fit for the already-proven Windows CodeView/PDB Rocket contract.
- Raw Win32 debugging or direct DbgEng COM interop: unnecessary engine plumbing and a much larger maintenance/safety surface.
- Visual Studio automation/embedding: violates the standalone portable IDE requirement.
- Cosmetic debugger UI: explicitly forbidden by WP17.

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

DbgX uses `EngHost.exe` as a real child process. WP17 therefore changes the portable release from single-file self-extraction to a self-contained **multi-file** `win-x64` folder/ZIP. No .NET runtime, Visual Studio, or separately installed WinDbg is required, but the application directory must remain intact. Verification/package scripts fail if `RocketIDE.Debugger.dll` or the x64 `EngHost.exe` is missing.

## Verification and remaining acceptance evidence

The merged `main` state at `bf30f98` passed Windows `scripts\verify.ps1` with 348/348 tests, self-contained win-x64 publish, `RocketIDE.Debugger.dll` verification, and `amd64\EngHost.exe` verification. The current GitHub `windows-ci` run also passed and produced both verification and portable-package artifacts.

The only WP17 acceptance still intentionally deferred is the interactive tiny-Rocket smoke requested for the later Codex/manual GUI pass: prove real breakpoint binding, continue/pause, step over/in/out, source navigation, threads, call stack, locals, output, stop, and packaged-launch behavior. Do not mark that live evidence passed until it is actually exercised.

Known contract limitation: duplicate Rocket source basenames cannot be debugged safely under the frozen current source-map identity model and are rejected before launch.
