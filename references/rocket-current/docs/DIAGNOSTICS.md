# Rocket Diagnostic Catalog

The `Rdddd` format and original categories were frozen for Rocket 1.0. The
catalog has since grown additively for native interoperability, the Rocket
1.6 package ecosystem, Rocket 1.8 concurrency, and Rocket 2.0 bounded-input
hardening without changing existing
code identities.
Rocket 2.1 adds the `R6000` target/configuration category.

Rocket 3 Wave B reuses the stable `R4002` name-resolution and `R4005` arity
categories for named/default arguments, labeled enum payloads, and the bundled
graphics/UI API. Wave C has not introduced a new diagnostic category. Current
status and the copyable syntax/API reference are in `DOCUMENTATION_STATUS.md`
and `ROCKET_3_0_SYNTAX_DICTIONARY.md`.

Compiler diagnostics use the stable shape:

```text
path\file.rocket:12:9: error[R4002]: undefined name 'total'
```

For source diagnostics, the path, one-based line, one-based column, severity, code, and message are all
machine-readable. Messages may become clearer without changing the code's
category. Tools should match the complete `Rdddd` code rather than message text.
Manifest and tooling errors without a source position use
`rocketc: error[R5001]: ...` or `rocketc: error[R5002]: ...`.

| Code | Category | Typical cause |
| --- | --- | --- |
| `R1001` | Lexical | Invalid character, literal, or escape |
| `R1002` | Indentation | Tab indentation or a non-four-space block |
| `R1003` | Resource limit | Source, overlay, module graph, or other frontend input exceeds a documented bound |
| `R2001` | Syntax | Missing or unexpected grammar token |
| `R3001` | Module not found | Imported source cannot be read |
| `R3002` | Import cycle | Recursive source-module dependency |
| `R3003` | Visibility | Private declaration used across modules |
| `R3004` | Import alias | Two imports claim the same local alias |
| `R3005` | Dependency import | Import is absent from or attempts to bypass the exact locked dependency graph |
| `R4001` | Type/semantic | Type mismatch, invalid operation or mutation, unsupported native ABI type, or extern call outside `unsafe:` |
| `R4002` | Name resolution | Undefined value, function, constructor, or named argument (with a deterministic typo suggestion when applicable) |
| `R4003` | Control flow | Invalid loop control, entry point, or return path |
| `R4004` | Pattern match | Invalid, duplicate, or non-exhaustive cases |
| `R4005` | Arity | Wrong number of call or constructor arguments; missing, duplicate, conflicting, or unsupported named/default argument; required parameter after a default; mixed labeled/anonymous enum payload declaration |
| `R4101` | Concurrency constraint | A boundary transfers a non-`Send` value, or repeatable mutex/once/buffer access requires a value that is not `Share` |
| `R4102` | Share constraint | A weak reference targets a value that is not an identity-bearing `Share` value |
| `R4103` | Move-only ownership | A task, unique buffer, thread, guard, task group, or aggregate containing one is copied, recaptured, or used after move |
| `R4104` | Scoped lifetime | A lock guard or structured task group is returned, stored, sent, or captured beyond its lexical scope |
| `R4105` | Await context | `await` occurs outside an async function or its operand is not `Task[T]` |
| `R4106` | Async suspension | An async result/capture/local cannot safely cross a suspension or async functions do not return `Result[T, String]` |
| `R5001` | Manifest/package | Invalid `rocket.toml`, semantic version, dependency source, resolver conflict, lockfile, or package checksum |
| `R5002` | Tooling | Formatter, test-runner, binding/header generation, linker, or artifact workflow failure |
| `R5003` | Package integrity | Checksum, signing-key, metadata signature, archive, or cache verification failure |
| `R5004` | Registry authorization | Missing/revoked credentials, insufficient scope, namespace ownership, reserved-name, or immutable-version refusal |
| `R5005` | Dependency audit | SPDX/license policy, yank policy, signed advisory, provenance, or compromised-package failure |
| `R5006` | Package transport | HTTPS/Git timeout, redirect, TLS, immutable-object, or bounded-download failure |
| `R5007` | Package capability | Implicit build script or unapproved native dependency capability |
| `R6001` | Unknown target | Target alias or canonical triple is not recognized |
| `R6002` | Unsupported target | Target is recognized but not a production-supported target |
| `R6003` | Target toolchain | Required compiler, linker, sysroot, SDK, runtime, or selected native input is unavailable |
| `R6004` | Host/target operation | Requested cross path or native execution on a different target is unsupported |
| `R6005` | Target manifest | Target-conditioned source or native configuration is invalid or ambiguous |
| `R9001` | Internal compiler | Verified compiler invariant failed |

Runtime failures remain a separate `rocket runtime error:` stream and exit with
status 101. They do not pretend to have source positions once native code is
running. Expected file, parsing, and process failures in the standard library
are `Result` values rather than diagnostics.

The compiler test suite fixes golden output for representative errors and
asserts catalog identities across lexical, syntax, name, control-flow, match,
arity, manifest, and internal categories. The VS Code extension's `$rocket`
problem matcher consumes this format.

## Tooling transports

LSP 1.0 publishes the same codes in `PublishDiagnosticsParams` with UTF-16
ranges. Unsaved overlays and incomplete parses do not invent alternate code
names, and closing a document clears its diagnostics.

`--message-format=json` emits newline-delimited `rocket-message-1` objects.
Diagnostic events contain `reason: "diagnostic"`, `level`, the stable `code`,
message, and a one-based `{file,line,column}` span. Build/test events use the
same schema version, so CI parsers can reject unknown major schemas rather than
scraping human prose. Source text is never included in telemetry or machine
messages. Native runtime text remains deliberately small; PDB line tables and
`rocket-source-map-1` provide the panic call-site location to debuggers.
