# Rocket Tooling and Packages 2.1 + Rocket 3.0 Wave B

Rocket 2.0 retains the Rocket 1.6 package ecosystem and Rocket 1.7
language-server and developer-tooling contracts. Its ownership, concurrency,
and asynchronous-I/O additions require no new editor protocol or manifest
syntax; `CONCURRENCY.md`, `STDLIB.md`, and `MIGRATION_1_8.md` document the new
source APIs and diagnostics.

Rocket 2.1 adds `--target <alias-or-triple>` to target-producing and
target-inspecting commands. `rocketc target [--verbose]` reports normalized
host/target information. Package artifacts default to
`.rocketc/targets/<alias>/`; `run` and native test execution reject a different
host/target with `R6004`. `TARGETS.md` defines supported cross paths and SDK
discovery.

The accepted Rocket 3 Wave B callable surface is editor-neutral: named/default
arguments and labeled enum payloads use the same formatter, LSP metadata,
signature help, and coded diagnostics. Wave C is ready but not yet a released
tooling contract. Use `ROCKET_3_0_SYNTAX_DICTIONARY.md` for the current syntax
index and `DOCUMENTATION_STATUS.md` for the live/historical document boundary.

For package targets, `build` and `run` reuse a `rocket-build-cache-1` artifact
only when compiler, runtime, target, options, dependency/native configuration,
and sorted package bytes match. Cache hits are reported in text and as
`"cache":"hit"` in `rocket-message-1` build output. Standalone source does not
use this cache. Independent compiler processes may build separate packages in
parallel; a single compilation remains deterministic and single-process.

## Create and use a package

```powershell
rocketc new hello
rocketc check hello
rocketc run hello
rocketc test hello
rocketc fmt hello --check
```

`rocketc new` creates the stable layout:

```text
hello/
  rocket.toml
  src/
    main.rocket
  tests/
    smoke_test.rocket
```

The manifest is intentionally small TOML:

```toml
[package]
name = "hello"
version = "0.1.0"
entry = "src/main.rocket"

[test]
directory = "tests"

[build]
kind = "executable"
name = "hello"
```

Names start with a letter or underscore. Entry and test paths are relative and
cannot escape the package root. Package builds keep generated objects and
executables in `<package>/.rocketc`. Imports resolve from the package root, so a
test may use `import src.math` even though its file lives under `tests/`.

Standalone `.rocket` files remain supported. When no input is supplied,
package-aware commands use the current directory.

## Phase 16 dependency ecosystem

The stage0 package workflow accepts `[package].license`, `[package].registry`,
and a `[dependencies]` table. Registry constraints and pinned `path:`/`git:`
forms, deterministic `rocket.lock`, SHA-256 caching, offline verification,
dependency trees, and integrity/license audits are specified in `PACKAGES.md`.

```powershell
rocketc resolve .
rocketc resolve . --locked
rocketc resolve . --offline
rocketc tree .
rocketc audit .
```

Commit `rocket.lock`; do not commit `.rocketc`. The first resolver transport is
a reviewed local directory or `file://` registry. Network fetching, publishing,
cached-module import integration, and self-hosted CLI parity remain explicit
later Phase 16 work rather than hidden behavior in normal compilation.

## Native inputs and library products

`[build].kind` is `executable`, `static-library`, or `dynamic-library`.
`[build].name` is the artifact basename. For backward compatibility, an
executable package without `[build]` still writes `.rocketc/main.exe`; a library
without an explicit name uses the package name. Library builds emit `.lib` or
`.dll` plus a deterministic `.h` header and do not synthesize `main`.

Native inputs are explicit and target-scoped; this Windows x64 example has
equivalent sections for the other production aliases:

```toml
[native.windows-x64]
libraries = "vendor_math.lib;vendor_handles.lib"
library-search = "native/lib"
headers = "native/vendor.h"
```

Lists use semicolons inside one quoted value. Search and header paths are
package-relative, containment-checked, and deterministic. Named libraries are
resolved in listed search directories, then the package root, and passed to the
native linker in manifest order. Headers are validated inputs; Rocket never
implicitly parses C during a normal build.

Generate an importable low-level binding module and a C consumer header with:

```powershell
rocketc bind native/vendor.h --output native_bindings.rocket
rocketc emit-header . --output .rocketc/my_library.h
```

`bind` intentionally accepts only the frozen C subset: `int64_t`, `double`,
`uint8_t`, `rocket_bool`, `void`, pointers, one-line named struct typedefs,
opaque struct typedefs, function-pointer typedefs, integer `#define` constants,
and ordinary or `ROCKET_API` function prototypes. Output declarations are
`pub extern` so a safe wrapper module can import them. Preprocessor conditionals,
unions, bitfields, arrays, variadics, calling-convention attributes, C++ APIs,
and macro expressions are rejected or ignored rather than guessed.

Keep generated modules low-level and put policy in a handwritten wrapper. The
wrapper owns the smallest possible unsafe region and translates documented C
status values into Rocket's checked values:

```rocket
import native_bindings

pub fn validate(value: Int) -> Result[Int, String]:
    unsafe:
        if native_bindings.vendor_status(value) == 0:
            return Ok(value)
    return Err("vendor rejected the value")
```

An owned handle follows the same rule: acquire it inside the wrapper, release it
exactly once with the documented C destructor, never use it afterward, and do
not rely on Rocket ARC to manage it. A wrapper must not let a synchronous native
callback or pointer escape beyond the lifetime promised by the C API.

Static Rocket libraries do not embed `rocket_runtime.lib`; a native consumer
must link the matching runtime when an exported function reaches runtime
services. Dynamic Rocket libraries embed the runtime and produce an import
library beside the DLL. Native library packages cannot be passed to `run`.

## Test runner

Every `.rocket` file under the manifest's test directory is a separate native
test program and must define `fn main() -> Int`. Exit 0 passes; any other exit,
compile failure, or runtime failure fails. Files are discovered recursively in
lexical path order and generated directories are ignored.

This convention keeps tests ordinary Rocket programs with no hidden exception
or reflection mechanism:

```rocket
import src.math

fn main() -> Int:
    if math.doubled(3) == 6:
        return 0
    return 1
```

The runner reports each path followed by `PASS` or `FAIL`, prints a final count,
and exits nonzero if any test failed. It uses the same LLVM or preserved Stage 0
backend as `run`.

## Formatter

`rocketc fmt <file-or-directory>` writes canonical source. `--check` makes no
changes and exits 1 if any file differs, which is suitable for CI. Formatting
uses four-space indentation, canonical token spacing, LF newlines, one final
newline, and preserves line comments and literal contents. It is idempotent and
refuses lexically invalid input rather than guessing around a broken literal or
indentation structure. The compiler accepts both LF and CRLF input; repository
attributes keep Rocket sources and golden diagnostics at canonical LF on every
host.

## Other commands

`check`, `build`, `run`, `emit-ir`, `emit-asm`, and `emit-header` accept either a standalone
source, a package directory, or a `rocket.toml` path. Program arguments follow
`--`. `rocketc --version` prints the compiler version.

In an LLVM-disabled stage0 build, generated C++ is compiled by the exact C++
compiler recorded during CMake configuration. On the supported Windows workflow
this is the activated MSVC compiler; stage0 does not require a separate `g++`
installation.

## VS Code

`editors/vscode` contains the Rocket TextMate grammar, language configuration,
snippets, `$rocket` diagnostic matcher, and a dependency-free client for the
Phase 17 `rocket-lsp` process. Copy or link it into the VS Code extensions
directory as `rocket-lang.rocket-language-1.7.0`, reload the editor, and set
`rocket.languageServer.path` when `rocket-lsp.exe` is not on `PATH`. The tasks
in `.vscode/tasks.json` remain available for check, run, test, and format check.

`rocket-lsp` protocol 1.0 uses standard LSP 3.17 framing and negotiated incremental
synchronization. It publishes stable lexical/parser/semantic diagnostics and
reuses the bounded multi-package compiler graph. Run it directly for any
editor-neutral LSP client:

```powershell
.\out\build\windows-debug\rocket-lsp.exe
```

The precise transport, lifecycle, diagnostic, and security contract is in
`LANGUAGE_SERVER.md`, including multi-file analysis, hover, completion,
navigation, rename, semantic tokens, and code actions.

Named/default-argument syntax is editor-neutral: the shared formatter
canonicalizes `name: expression`, labeled enum entries such as `name: Type`, and
`name: Type = expression`; diagnostics use the same stable codes in every
client. Signature help exposes declared function, closure, and enum-payload
names plus compiler-owned built-in and standard-intrinsic names, types, and
canonical defaults across package modules.
Documentation search metadata emits a `defaults` array aligned with
`parameters`, using `null` for required operands, a `payloadLabels` array for
public enums, and a deterministic `compilerCallables` inventory. VS Code and
Visual Studio use the same `rocket-lsp` analysis and need no editor-specific
semantic fork; their existing Rocket grammars already tokenize the identifiers,
colons, and equals signs used by declarations and calls.

## Visual Studio 2026

The supported Windows IDE is Visual Studio Community 2026 on Windows x64. The
repository-owned `Rocket.Language.VisualStudio` VSIX preserves its original
extension identity and now supplies a VSPackage plus an LSP MEF component.
Build and install or upgrade it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\package-visualstudio-extension.ps1
```

The build restores only pinned Visual Studio SDK build tools and .NET Framework
reference assemblies under ignored `out/visualstudio/packages`, locates the
installed 18.x Community instance with `vswhere`, and produces
`out/visualstudio/Rocket.Language.VisualStudio.vsix`. The package validation is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\test-visualstudio-extension.ps1
```

Open the VSIX, then start the repository with Rocket's pinned environment:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\open-visualstudio.ps1
```

The **Extensions > Rocket** menu and context-sensitive standard toolbar provide
Build, Run, Test, Stop, and Debug commands. The active target is the nearest ancestor
`rocket.toml`; without one, the active `.rocket` file is a standalone target.
Compiler and application processes use hidden-window startup, redirected
streams, and a kill-on-close Windows job. The dedicated Rocket Output pane
receives build, test, ordinary program, environment-validation, and language
server logs. `rocket-message-1` diagnostics populate Visual Studio's Error List
with stable codes and navigable paths/locations.

Tools are discovered from repository-relative outputs, a sibling executable,
environment variables, `PATH`, or optional user settings under
**Tools > Options > Rocket**. With pinned-environment loading enabled,
`dependencies/activate.ps1` runs in a hidden process and its environment is
forwarded directly; no discovered machine path is written into the project or
VSIX.

The MEF language client starts `rocket-lsp.exe` with redirected standard I/O
for `.rocket` content. Visual Studio consumes the server's negotiated
completion, hover/signatures, definitions, references, rename, symbols,
semantic tokens, formatting/code actions, incremental synchronization, and
live diagnostics.

Debug performs an unoptimized compiler build, requires the matching executable,
PDB, and `rocket-source-map-1` sidecar, validates each mapped source, creates
the program suspended with `CREATE_NO_WINDOW` and redirected streams, attaches
Visual Studio's native-only Windows debugger by process ID, automatically
continues only the attach-generated `ntdll` break, and then stops normally on
Rocket source breakpoints.
The CodeView contract provides source breakpoints, stepping, native Rocket call
frames, and locals where represented. The frozen reproducible record uses
source basenames; compiled sources with duplicate basenames are rejected as
ambiguous before launch. Optimized CLI builds can fold or omit locals.

`open-visualstudio.ps1` copies the tracked
`editors/visualstudio/launch.vs.json` template into Visual Studio's ignored
`.vs` workspace state. CMake Targets View continues to expose
`rocket_demo_check`, `rocket_demo_run`, and `rocket_demo_test`, and CMake exposes
`rocket_visualstudio_extension`. These scripts/targets are reproducible
fallback automation; installed VSIX commands do not depend on `.vs` state.
Complete installation, usage, troubleshooting, and limitations are in
`editors/visualstudio/README.md`; the debugger contract remains in
`docs/DEBUGGING.md`.

## Rocket 1.7 professional tooling

The standalone compiler accepts `--message-format=json` on check/build/test
paths. Each stdout line is one `rocket-message-1` object (`diagnostic`,
`build-finished`, `test-started`, `test-finished`, or `test-summary`). Diagnostic
objects retain stable Rocket codes and one-based source spans. Human output is
unchanged when the flag is absent. The self-hosted compiler forwards this
explicit host-tooling mode to the packaged stage0 compiler; ordinary compilation
and bootstrap behavior remain self-hosted.

Native measurement commands are opt-in and never run on file open:

```powershell
rocketc coverage examples/hello.rocket --output out/coverage.json
rocketc profile examples/hello.rocket --output out/profile.json
rocketc benchmark examples/hello.rocket --iterations 20 --output out/benchmark.json
```

Coverage instruments MIR source locations and writes deterministic
`rocket-coverage-1` hit records. Profiling inserts function-entry hooks and emits
`rocket-profile-1` native symbols, which are resolved through the adjacent
Rocket source map/PDB. Benchmarks report bounded iteration count (1..1000),
minimum, median, and maximum wall time in `rocket-benchmark-1`. Instrumentation
is absent from normal builds.

`scripts/tooling.ps1` validates all three schemas and compiler JSON messages on
Windows. The Phase 19 portable workflow also validates target-native
CodeView/PDB, ELF/DWARF, or Mach-O `.dSYM` selection and executable-suffix
behavior on each native host.
`scripts/debugging.ps1` validates optimized/unoptimized PDB and map records on
Windows; native-host debug-information evidence is recorded in
`PHASE_19_AUDIT.md`.
The evaluated AOT prototype and its limits are in `docs/REPL.md`.

## Compiler packaging

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\package-compiler.ps1 -Configuration Release
```

This historical command packages the frozen Rocket 2.0 Windows SDK under
`out/package`; Phase 19 must not run it in place. `scripts/phase19_package.py`
packages an isolated target build under `out/phase19/package`, verifies
checksums, and relocates it to a sanitized path. A target package includes the
matching `rocketc`, runtime, stage0, standard library, linker/toolchain pieces,
and target-native support libraries needed for ordinary compilation after
installation. The verification step clears development toolchain variables,
changes to an isolated working directory, then checks, builds, runs, and
directly executes a native Rocket program.

The macOS package does not copy or redistribute Apple's SDK. Its launchers
resolve the active Xcode Command Line Tools SDK with `/usr/bin/xcrun`, export it
as `ROCKET_MACOS_SDK_ROOT`, and every stage0, self-hosted, bootstrap, and
relocation link passes that directory to Clang/LLD as the native sysroot.
Mach-O products retain reproducible content-hash UUIDs and reserve install-name
header padding; packaging verifies each rewritten image still has `LC_UUID`
before checksumming and testing the relocated SDK.

The preserved C++ bootstrap compiler is distributed separately as
`stage0/rocketc-stage0` (with `.exe` on Windows). It remains the reproducible bootstrap compiler and
the audited package security host used by the Rocket-written CLI for registry,
credential, signing, HTTPS, and Git operations; it is not the user-facing
`rocketc` command.

## Rocket 1.6 package tooling

`resolve`, `tree`, `audit`, `doc`, `login`, `logout`, `publish`, and `registry`
implement the contract in `PACKAGES.md`. Normal `check`, `build`, `run`, and
`test` require the committed lock when dependencies exist and load modules only
from verified content-addressed cache roots. The Rocket-written compiler
forwards package-security commands to its colocated preserved Stage 0 host and
then independently consumes the resulting exact lock/cache graph. This keeps
one reviewed implementation of credential, ECDSA, HTTPS, Git, and archive
boundaries without moving the compiler frontend back into C++.

The Release package includes the host at `stage0/rocketc-stage0.exe`; repository
bootstrap and conformance set `ROCKET_STAGE0` explicitly. Relocation validation
clears that variable and proves colocated discovery, online resolve, audit,
locked-offline resolve, and dependency import checking.

## raylib application workflow

Rocket 1.4 pins raylib 6.0 in `dependencies/manifest.json`. CMake builds raylib
and the primitive adapter statically, then runs `rocketc bind` into an ignored
`generated/` module before checking or building `examples/raylib_showcase`.
Use `scripts/run-raylib-validation.ps1` for the labeled native suite,
`scripts/new-raylib-app.ps1` for a scaffold, and
`scripts/package-raylib-showcase.ps1` for the historical Windows bundle. Phase
19 uses target-scoped raylib adapters and the native acceptance matrix for the
same headless/application validation on every production target. The interactive
executable must run with its packaged `assets` directory as the working
directory.
