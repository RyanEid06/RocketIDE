# WP17 debugger feasibility

The current Rocket reference contract is native Windows debugging through Visual Studio. The preserved Rocket documentation says a debug build produces an executable, CodeView PDB, and `rocket-source-map-1` sidecar; Visual Studio consumes the native engine for source breakpoints, stepping, call frames, and locals where represented. The source-map contract records source basenames, so duplicate basenames are rejected as ambiguous.

RocketIDE now validates those three artifacts without launching the target in `RocketDebugArtifactValidator`. It checks that the executable, PDB, and sidecar exist, verifies the sidecar format, and reports missing or duplicate source mappings. This is an artifact-safety boundary, not a debugger implementation.

No redistributable native debugger backend or Rocket DAP adapter is present in this repository, and no real Rocket debug session was run during the code-only pass. Embedding or automating Visual Studio would not satisfy the standalone portable IDE requirement.

`ROCKET-UPSTREAM-REQUEST: provide a supported, redistributable Rocket DAP/debug adapter exposing launch, stop, continue, pause, step, breakpoints, threads, call stacks, and locals over the existing PDB/source-map contract.`

Recommendation: keep WP17 deferred until that adapter or another legally redistributable backend is supplied and a tiny Rocket debug build proves breakpoint binding, continue, stepping, call stacks, and locals. No cosmetic debugger UI is shipped.
