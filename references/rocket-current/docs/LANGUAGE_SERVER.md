# Rocket Language Server Protocol 1.0

`rocket-lsp` began as Rocket 1.7's standalone, editor-neutral LSP 3.17 server
and remains the shared client-neutral protocol for the completed Rocket 2.1
baseline and accepted Rocket 3 Wave B. It
uses `Content-Length` framing over standard input/output; logs go only to
standard error. `rocket-lsp --version` prints `rocket-lsp 1.0.0`.

Wave B named/default arguments, labeled enum payloads, and bundled graphics/UI
signatures are exposed through the same signature-help, completion, formatter,
hover, and documentation metadata. Wave C is not a new editor protocol. The
current surface index is `ROCKET_3_0_SYNTAX_DICTIONARY.md` and the document
disposition is in `DOCUMENTATION_STATUS.md`.

## Safety and bounds

Opening a source file performs lexical, parser, semantic, and project-graph
analysis only. It never runs a build, package script, native program, registry
request, or other source-controlled command. Locked dependencies are opened as
inert source in offline mode.

- protocol message: 16 MiB; header: 16 KiB;
- open document: 4 MiB;
- project defaults: 4,096 files and 64 MiB of source;
- one workspace edit: 1,024 edits;
- malformed frames, invalid JSON-RPC, stale versions, invalid UTF-16 ranges,
  oversized content, and requests before `initialize` receive bounded errors.

Each client runs an independent process. Requests within a process are handled
in arrival order. `$/cancelRequest` suppresses queued work and returns LSP
`RequestCancelled` (`-32800`); analysis generations suppress results computed
for stale document versions. The server reports elapsed milliseconds, analyzed
bytes/files, generation, and invalidation counts without source text through
`rocket/analysisStatus`. `rocket/projectStatus` returns the current bounds and
index size on request.

## Project model

Protocol 1.0 reuses `ModuleLoader`, the package manifest/lock resolver, the
compiler lexer/parser, HIR symbols, and Rocket types. It does not maintain a
second type system. A workspace graph contains the root package, exact locked
dependencies, standard modules, and rootless open files. Open documents are
path-keyed in-memory overlays and therefore win over disk without being saved.
Changes invalidate the affected graph generation; only bounded workspace roots
are scanned. Incomplete files retain recoverable lexical/AST symbols so editor
features remain available without manufacturing successful type results.

## Lifecycle and synchronization

The lifecycle is `initialize`, optional `initialized`, ordinary requests and
notifications, `shutdown`, then `exit`. The server negotiates UTF-16 positions
and incremental synchronization (`textDocumentSync.change = 2`). Full-content
changes remain accepted as the bounded compatibility baseline. Versions must
increase. `workspace/didChangeConfiguration` accepts:

- `rocket.maximumProjectFiles` (1..4096),
- `rocket.maximumProjectBytes` (1 MiB..64 MiB),
- `rocket.telemetry` (boolean).

Workspace-folder changes rebuild the bounded graph. Closing a document removes
its overlay and publishes an empty diagnostics array.

## Capabilities

- deterministic semantic completion, qualified completion, and automatic
  import edits for a unique visible public declaration;
- Markdown hover with Rocket type/signature, documentation, and a
  `rocket-doc://` versioned-documentation link;
- signature help with active parameter and available compiler signatures,
  including canonical default expressions plus declared closure, labeled-enum,
  compiler-built-in, and standard-intrinsic parameter names;
- resolved definition, references, prepare-rename, and conflict-checked
  workspace rename;
- semantic token full/delta responses for keywords, values, declarations,
  parameters, properties, types, traits/interfaces, functions/methods, native
  declarations, strings, and numbers;
- stable quick fixes for `R4002` missing names plus whole-document formatter
  actions. Imports are emitted only when resolution is unique, and repeated
  action requests are byte-stable; once applied, the import action disappears;
- workspace symbols and compiler diagnostics retaining their stable `Rdddd`
  codes.

Rename refuses keywords, standard/native declarations, locked dependency
sources, invalid identifiers, and conflicts. Edits are limited to writable
workspace/open files. Textual matches that did not resolve to the selected HIR
symbol are not edited.

## Client neutrality and validation

`tests/language_server_tests.cpp` is an editor-independent in-process LSP
client. It covers lifecycle, framing, malformed/oversized messages, UTF-16,
incremental and stale changes, overlays, incomplete code, navigation, rename,
completion, imports, hover, signatures, tokens, actions, cancellation, telemetry,
and latency. This is the required non-VS-Code client. The dependency-free VS
Code extension is a separate consumer and its transport/provider tests live in
`editors/vscode/test/client.test.js`.

Protocol 1.0 deliberately remains LSP rather than a Rocket-specific editor
protocol. New fields and custom `rocket/*` methods must be additive and bounded;
breaking behavior requires a new protocol version.

## Visual Studio Community 2026 client

The repository VSIX exports a `Rocket` content type for `.rocket` documents and
an `ILanguageClient` MEF component. Visual Studio starts the same
`rocket-lsp.exe` described above with standard input/output/error redirected and
with hidden-window process creation. The active repository's pinned environment
is loaded in a separate hidden process when that option is enabled. Server
stderr is prefixed with `[LSP]` in the dedicated Rocket Output pane; stdout is
reserved exclusively for LSP framing.

The client does not duplicate or reinterpret semantic operations. Completion,
hover, signatures, definitions, references, prepare-rename/rename, document and
workspace symbols, semantic tokens, code actions, whole-document formatting,
incremental synchronization, and published live diagnostics remain implemented
by `rocket-lsp` and negotiated through Visual Studio's LSP client. TextMate
syntax highlighting remains available if the server executable cannot be
located.

Tool discovery is portable: the client checks the Rocket options page,
`ROCKET_LANGUAGE_SERVER`, a sibling of the selected compiler, repository-relative
Debug/Release outputs, and `PATH`. It does not store a checkout path in the
VSIX. Run **Extensions > Rocket > Validate Rocket Environment** and inspect `[LSP]` output
when activation fails. Focused VSIX tests validate package discovery and the
language-client asset; the editor-neutral LSP protocol suite remains the source
of truth for every semantic capability.
