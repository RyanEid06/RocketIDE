# Rocket Integration Contract for RocketIDE

This is a concise integration checklist derived from the current Rocket repository snapshot under `references/rocket-current/`. When this file disagrees with the copied Rocket docs, the copied Rocket docs and actual tool behavior win. When the Rocket repository evolves, refresh this snapshot and update the IDE integration deliberately.

## Language server

Executable: `rocket-lsp.exe`  
Protocol: LSP 3.17 over stdin/stdout  
Framing: `Content-Length` headers  
Logging: stderr only  
Positions: UTF-16 negotiated by LSP  
Text synchronization: incremental (`change = 2`), full-content accepted as bounded compatibility path

Important limits:

| Item | Current limit |
|---|---:|
| LSP protocol message | 16 MiB |
| Header | 16 KiB |
| Open document | 4 MiB |
| Project source files | 4,096 |
| Project source bytes | 64 MiB |
| Workspace edit | 1,024 edits |

Required standard capability consumers:

- completion
- hover
- signature help
- definition
- references
- prepare rename / rename
- semantic token full/delta
- code action
- whole-document formatting
- workspace symbols
- published diagnostics
- incremental synchronization

Rocket custom messages available to the IDE:

- notification: `rocket/analysisStatus` — consumed by the current session-status path.
- request: `rocket/projectStatus` — typed model available; request/consumer is added only when a later WP needs project-status data.

Important semantic rule: `rocket-lsp` uses compiler lexer/parser/HIR/types and unsaved overlays. Do not duplicate that model in RocketIDE.

## Diagnostics

Compiler/LSP diagnostic codes are stable `Rdddd` identities. Current broad categories include:

- `R1001` lexical
- `R1002` indentation
- `R1003` resource limit
- `R2001` syntax
- `R3001`..`R3005` module/import/dependency
- `R4001`..`R4005` semantic/name/control/match/arity
- `R4101`..`R4106` concurrency/ownership/async constraints
- `R5001`..`R5007` package/tool/registry integrity/capability
- `R6001`..`R6005` target/toolchain/configuration
- `R9001` internal compiler invariant

Do not key IDE behavior off English wording when the stable code or protocol field exists.

## Compiler messages

For `check`, `build`, and `test`, request:

```text
--message-format=json
```

stdout becomes newline-delimited `rocket-message-1` objects. Known reasons in the current integration include:

- `diagnostic`
- `build-finished`
- `test-started`
- `test-finished`
- `test-summary`

Common fields used by the existing Visual Studio integration:

```text
schema
reason
level
code
message
command
success
artifact
cache
name
status
exitCode
passed
failed
expectedFailures
selected
span.file
span.line
span.column
```

Source span line/column in compiler messages are one-based. LSP ranges are protocol ranges and must be handled separately.

## Package targeting

Normal project layout created by `rocketc new`:

```text
project/
  rocket.toml
  src/
    main.rocket
  tests/
    smoke_test.rocket
```

IDE target discovery follows nearest-ancestor `rocket.toml`. Without one, an active `.rocket` document may be a standalone target.

Generated package artifacts live under `.rocketc`; this directory is ignored by default.

## Tool commands for IDE exposure

Core:

```text
rocketc check
rocketc build
rocketc run
rocketc test
rocketc fmt
rocketc new
```

Package/dependency:

```text
rocketc resolve
rocketc tree
rocketc audit
```

Inspection/measurement:

```text
rocketc target
rocketc coverage
rocketc profile
rocketc benchmark
```

Exact flags must be verified against the active Rocket version before UI command construction is finalized.

## Tool discovery and trust

Trusted automatic tool sources are RocketIDE explicit settings, `ROCKET_COMPILER` / `ROCKET_LANGUAGE_SERVER`, process `PATH`, a bundled Rocket SDK, and a developer Rocket checkout discovered from RocketIDE's own installation location (specifically a sibling `Rocket` checkout beside a recognized `RocketIDE` / `RocketIDE-Build` directory). The installation-adjacent fallback is independent of the opened workspace and exists only to support development builds without machine-specific source changes.

Build/package outputs discovered *from the opened checkout* are different: source-controlled content is untrusted, so RocketIDE must not probe or execute workspace-local `rocketc.exe` / `rocket-lsp.exe` merely because the workspace was opened. A checkout-local toolchain that is not the installation-adjacent developer SDK may be used only after the user explicitly trusts that exact checkout in Rocket SDK Settings. Trust is persisted per-user outside source control and must be revocable. This trust decision permits RocketIDE to probe/start the Rocket toolchain; it does **not** authorize running the user's Rocket program, package scripts, or arbitrary project executables.

## Existing Visual Studio code worth studying

Snapshot files under `references/rocket-current/editors/visualstudio/CoreReference/` demonstrate concepts for:

- Windows command-line quoting;
- manifest parsing;
- compiler JSON message parsing;
- source-map parsing;
- target discovery;
- tool discovery;
- hidden redirected process execution;
- process-tree cancellation.

Port concepts, not Visual Studio dependencies.
