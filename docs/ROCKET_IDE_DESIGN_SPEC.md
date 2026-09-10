# RocketIDE Design Specification

**Status:** Approved architecture baseline  
**Platform:** Windows x64 first  
**Primary language:** C# 14  
**Runtime/UI:** .NET 10 + WPF  
**Editor control:** AvalonEdit  
**Rocket integration:** `rocket-lsp.exe` + `rocketc.exe`

## 1. Product goal

RocketIDE is a focused native Windows IDE for the Rocket programming language. It should make Rocket pleasant to use without depending on Visual Studio, VS Code, Electron, a browser runtime, or a hosted web application.

A developer should be able to install RocketIDE, open or create a Rocket project, edit multiple files, receive compiler-accurate feedback while typing, navigate code, apply supported fixes, build/run/test the project, inspect output, and maintain package dependencies from one application.

RocketIDE is intentionally not a general-purpose polyglot IDE. It supports Rocket source files and Rocket project metadata first. Generic text viewing may exist where necessary, but language intelligence is Rocket-specific.

## 2. Non-goals for 1.0

The following are explicitly outside the initial 1.0 critical path:

- becoming a VS Code or Visual Studio clone;
- plugin marketplace or third-party extension API;
- Git hosting UI;
- collaborative editing;
- cloud accounts;
- AI code generation/chat embedded in the IDE;
- cross-platform Linux/macOS builds;
- implementing a second Rocket compiler frontend;
- implementing an independent package resolver;
- a custom native debugger engine.

A debugger may be added after the core IDE is stable. Rocket currently emits Windows PDB and Rocket source-map information, but the existing Visual Studio extension relies on Visual Studio's native debug engine. RocketIDE must not pretend that this already gives it a standalone debugger.

## 3. Architectural principle

RocketIDE is a client around stable Rocket tooling.

```text
RocketIDE.exe
  |
  +-- WPF shell / views
  |    +-- workspace explorer
  |    +-- editor tabs (AvalonEdit)
  |    +-- problems
  |    +-- output
  |    +-- search
  |    +-- settings
  |
  +-- Rocket language client
  |    +-- rocket-lsp.exe over stdio LSP 3.17 / JSON-RPC
  |
  +-- Rocket tool service
  |    +-- rocketc.exe check/build/run/test/fmt/...
  |
  +-- Windows infrastructure
       +-- files
       +-- process/job control
       +-- settings/session
       +-- dialogs
```

Semantic language behavior must stay consistent with the compiler because `rocket-lsp` reuses Rocket's compiler lexer/parser/HIR/types and accepts unsaved document overlays.

## 4. Solution boundaries

### 4.1 `RocketIDE.Core`

Owns UI-independent domain values and contracts:

- document identity/state metadata;
- workspace/project metadata;
- diagnostics displayed by the IDE;
- output events;
- search result values;
- interfaces for file, process, settings, clock, and dispatcher seams when needed;
- command state/result primitives.

It must not reference WPF, AvalonEdit, Win32, or Rocket protocol implementation packages.

### 4.2 `RocketIDE.Rocket`

Owns Rocket-specific integration:

- Rocket tool discovery;
- nearest `rocket.toml` target discovery;
- minimal manifest metadata needed by UI/tool launching;
- `rocket-message-1` parsing;
- compiler command construction;
- diagnostic-code catalog presentation metadata;
- LSP framing and JSON-RPC client;
- LSP DTOs/capability negotiation;
- URI/range conversion;
- server lifecycle;
- conversion from LSP/compiler messages to Core models.

It must not reference WPF.

### 4.3 `RocketIDE.Infrastructure`

Owns host-specific implementation:

- async file operations;
- file watchers;
- Windows process runner;
- Windows Job Object process-tree cancellation;
- user settings storage;
- recent workspaces;
- session/crash-recovery persistence;
- safe atomic writes;
- application logging.

It must not own Rocket semantic rules.

### 4.4 `RocketIDE.App`

Owns WPF composition and presentation:

- `App` startup;
- main window;
- commands/keybindings;
- view models;
- AvalonEdit integration;
- editor decorations;
- panels/dialogs;
- WPF theme resources;
- UI dispatcher adapters.

The App consumes interfaces and services from other projects; it does not parse Rocket code itself.

## 5. UI baseline

The default layout is a compact dark-theme developer environment:

```text
+----------------------------------------------------------------------------+
| File Edit Selection View Navigate Build Run Tools Help          Run Stop   |
+------------------+---------------------------------------------------------+
| EXPLORER         | main.rocket | player.rocket |                           |
|                  +---------------------------------------------------------+
| project          | editor                                                   |
|  rocket.toml     |                                                          |
|  src             |                                                          |
|   main.rocket    |                                                          |
|   player.rocket  |                                                          |
|  tests           |                                                          |
|                  |                                                          |
+------------------+---------------------------------------------------------+
| PROBLEMS | OUTPUT | TESTS                                                   |
+----------------------------------------------------------------------------+
| Rocket SDK | branch-neutral project status | Ln | Col | encoding | LSP      |
+----------------------------------------------------------------------------+
```

The layout must remain usable on 1366x768 and scale cleanly with Windows DPI. Panels are resizable. Explorer and bottom panel may be hidden. The code editor always gets priority space.

## 6. Editor requirements

The editor must support:

- multiple tabs;
- dirty-state marker;
- save / save all;
- undo / redo;
- line numbers;
- current-line highlight;
- selections and multi-line edits;
- goto line;
- find/replace within file;
- indent/outdent;
- tab key produces Rocket-compatible spaces, not tab indentation;
- bracket/quote convenience behavior only when it never corrupts selections;
- syntax highlighting even when LSP is offline;
- semantic-token overlay when LSP is online;
- diagnostic squiggles;
- hover cards;
- completion popup;
- signature help;
- rename/code-action edits;
- reliable files at normal source sizes;
- Large File Mode above the Rocket LSP open-document limit.

## 7. Workspace and project model

RocketIDE supports:

1. **Package workspace:** directory containing or descending from `rocket.toml`.
2. **Standalone file:** individual `.rocket` file without a package.
3. **Folder workspace:** a folder may be opened before a manifest exists; Rocket semantic operations operate on eligible Rocket targets within supported server behavior.

Project actions search upward from the active document for the nearest `rocket.toml`, matching the existing Visual Studio integration behavior. If none exists and the active file is `.rocket`, compiler commands target the standalone file.

The explorer must provide file/folder creation, rename, delete-to-Recycle-Bin when practical, reveal in Explorer, copy path, refresh, and direct editing. Destructive operations require clear confirmation where data loss is possible.

The IDE must notice external file changes and avoid silently overwriting them.

## 8. Language-server integration contract

Rocket's current language server is LSP 3.17 over stdin/stdout using `Content-Length` framing. Stderr is logging only.

Required client features:

- `initialize` / `initialized` / `shutdown` / `exit` lifecycle;
- incremental text synchronization;
- `didOpen`, `didChange`, `didSave`, `didClose`;
- publish diagnostics;
- completion;
- hover;
- signature help;
- definition;
- references;
- prepare rename + rename;
- semantic tokens full and delta;
- code actions;
- document formatting;
- workspace symbols;
- workspace-folder/configuration changes as implemented by Rocket;
- cancellation (`$/cancelRequest`);
- custom `rocket/projectStatus` request;
- custom `rocket/analysisStatus` notification for status/performance display.

The client must correlate request IDs, tolerate out-of-order responses if the protocol ever does, reject malformed frames safely, and never interpret server stdout as logs.

## 9. Rocket LSP safety limits

Current Rocket reference contract:

- protocol message: **16 MiB**;
- header: **16 KiB**;
- open document: **4 MiB**;
- project defaults: **4,096 files** and **64 MiB source**;
- one workspace edit: **1,024 edits**.

The IDE must enforce the open-document limit before `didOpen`/full sync.

### Large File Mode

When a source file exceeds 4 MiB:

Available:
- open/edit;
- save;
- line navigation;
- local find/replace;
- basic syntax coloring when reasonably performant.

Disabled for that document:
- LSP live diagnostics;
- semantic tokens;
- completion;
- hover/signature help;
- LSP navigation/refactors.

A visible non-blocking banner explains why. The app must not freeze or repeatedly retry rejected LSP operations.

## 10. Diagnostics and fixes

RocketIDE combines two diagnostic sources with provenance:

- **Live diagnostics:** `rocket-lsp` publish diagnostics while editing.
- **Command diagnostics:** `rocketc --message-format=json` events during explicit check/build/test.

Stable `Rdddd` code is the identity. Message text is presentation, not parsing identity.

Problems panel fields:

- severity;
- code;
- message;
- file;
- line;
- column;
- source (`LSP`, `Build`, `Test`, etc.).

Click/double-click navigation must open the file and select the relevant range/location.

Automatic edits are applied only from Rocket LSP `textDocument/codeAction` or other explicitly defined Rocket machine-readable edit contracts. Diagnostic help text may suggest manual actions but must not be presented as a guaranteed automatic fix.

## 11. Compiler/tool integration

The initial command surface includes:

- Validate Rocket Environment
- Check
- Build
- Run
- Stop
- Test
- Format Document
- Format Workspace/Target
- New Rocket Project
- Resolve Dependencies
- Dependency Tree
- Audit Dependencies
- Target Information
- Coverage
- Profile
- Benchmark

For `check`, `build`, and `test`, pass `--message-format=json` and parse each stdout line as `rocket-message-1` when it is structured output. Unknown schema majors are not silently guessed.

Normal program output from `run` is streamed to the Output panel. Runtime failures may be plain runtime stream text; they are not fabricated into compiler source diagnostics unless Rocket provides a machine-readable source mapping contract for that event.

## 12. Tool discovery

Search order follows proven Rocket integration concepts:

### Compiler
1. RocketIDE explicit setting.
2. `ROCKET_COMPILER` environment variable.
3. active Rocket checkout known build/package outputs where safely discoverable.
4. `PATH`.
5. bundled SDK path when distributed with RocketIDE.

### Language server
1. RocketIDE explicit setting.
2. `ROCKET_LANGUAGE_SERVER` environment variable.
3. sibling of selected compiler.
4. active Rocket checkout known build/package outputs.
5. `PATH`.
6. bundled SDK sibling/path.

The UI exposes selected path + `--version`, and lets the user reset to automatic discovery.

## 13. Process model

Only one conflicting target operation should control the primary target at a time unless Rocket explicitly guarantees concurrency for the combination.

All external processes:

- hidden/no shell window for tool commands;
- redirected stdout/stderr;
- cancellable;
- placed in a kill-on-close Windows Job Object when appropriate;
- disposed reliably;
- no orphaned compiler/debuggee tree after Stop or IDE exit.

`Run` may need application-specific GUI behavior. A native Rocket GUI executable may create its own window normally even though the launcher process uses redirected streams.

## 14. Search and large-project performance

Workspace search must be asynchronous, streaming, cancellable, and bounded. It must skip generated/transient directories by default, including `.git`, `.rocketc`, `bin`, `obj`, `.vs`, and configurable exclusions.

Do not load every file into WPF controls to search it. Stream file contents and results. Avoid one giant in-memory string representing the workspace.

Opening normal source files should feel immediate. Slow LSP analysis is surfaced as status, not as a frozen editor.

## 15. Persistence and recovery

Persist per-user, not inside the source repo unless explicitly project configuration:

- recent workspaces;
- window size/position;
- panel visibility/sizes;
- selected Rocket SDK path;
- theme;
- optional editor preferences;
- last session documents.

Crash recovery may persist unsaved buffers to an application data recovery area. It must never silently replace the original source file on startup; offer restore/discard comparison.

## 16. Distribution

Primary 1.0 artifact:

- Windows x64;
- self-contained .NET publish;
- `RocketIDE.exe` entry point;
- portable ZIP first;
- installer may be added after the portable artifact is stable.

Two SDK modes:

- **Bundled Rocket SDK** for a turnkey release.
- **External/custom Rocket SDK** for Rocket compiler development and testing.

The IDE must report compiler and LSP versions in environment validation.

## 17. Testing strategy

### Unit tests

Test pure logic without launching WPF or Rocket tools:

- target discovery;
- manifest metadata parsing needed by IDE;
- command construction and quoting;
- `rocket-message-1` parsing;
- URI/path conversion;
- UTF-16 LSP position conversion;
- LSP frame reader/writer;
- document version/state rules;
- large-file-mode threshold;
- workspace-edit validation;
- search filtering;
- settings serialization.

### Protocol integration tests

Use a deterministic fake stdio LSP server or in-memory duplex streams to validate:

- lifecycle;
- response correlation;
- notifications;
- cancellation;
- malformed/oversized framing;
- incremental sync;
- diagnostics conversion;
- workspace edit handling.

### Rocket integration tests

When `rocketc.exe` / `rocket-lsp.exe` fixtures are available, run opt-in tests against actual tools. The normal unit suite must remain fast and deterministic without requiring a Rocket compiler installation.

### UI smoke tests

Keep UI automation limited to high-value flows after the core is stable. Do not attempt to prove semantic correctness by clicking pixels.

## 18. Definition of done

A feature is complete only when:

1. behavior exists through the intended architecture;
2. focused automated tests pass;
3. no fake fallback duplicates Rocket semantics;
4. errors/cancellation are handled;
5. `scripts/verify.ps1` passes;
6. Windows CI passes for code-affecting WPs;
7. documentation/progress ledger is updated.

A screenshot is never acceptance evidence for compiler/editor semantics.
