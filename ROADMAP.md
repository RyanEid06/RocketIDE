# RocketIDE Roadmap

> Execute work packages in dependency order. Checklist items use (`- [ ]`) tracking. Do not skip acceptance gates, and update the progress ledger only after verification evidence exists.

**Goal:** Build a fast, reliable, Windows-only desktop IDE dedicated to Rocket, with multi-file editing, compiler-accurate live errors and fixes, project navigation, build/run/test/output workflows, and large-file-safe behavior.

**Architecture:** WPF/AvalonEdit is the native editor shell. Rocket semantics are consumed from `rocket-lsp.exe` over LSP 3.17 stdio; explicit compiler/tool operations are consumed from `rocketc.exe`, preferring structured `rocket-message-1` JSON. Core, Rocket integration, Windows infrastructure, and WPF presentation remain separated so protocol and process logic are testable without UI.

**Tech Stack:** C# 14, .NET 10, WPF, AvalonEdit 6.3.1.120, MSTest 4.4.0, Microsoft.NET.Test.Sdk 18.9.0, System.Text.Json, Windows Job Objects.

**Design specification:** `docs/ROCKET_IDE_DESIGN_SPEC.md`

## Global Constraints

- Windows x64 is the only required 1.0 platform.
- RocketIDE is a native WPF desktop app; do not replace it with Electron, React, WebView/Monaco, Tauri, or a web app.
- `rocket-lsp.exe` is the source of truth for editor semantics.
- `rocketc.exe` is the source of truth for compiler/tool behavior.
- Opening source files must never execute Rocket/native code or package scripts.
- LSP stdout is protocol-only; stderr is log output.
- Respect the current Rocket LSP bounds: 16 MiB message, 16 KiB header, 4 MiB document, 4,096 project files, 64 MiB project source, 1,024 workspace edits.
- Use `--message-format=json` for supported check/build/test command paths.
- No source-controlled absolute machine paths.
- No blocking disk/process/LSP operations on the WPF UI thread.
- No WP is complete until focused tests and `scripts/verify.ps1` pass; code-affecting WPs also require Windows CI evidence.
- Do not mark features complete based on visual appearance alone.

---

# Progress Ledger

Change only `Status`, `Completed`, and `Evidence` after the WP gate passes.

| WP | Name | Status | Completed | Evidence |
|---|---|---|---|---|
| IDE-WP00 | Repository baseline + CI | DONE | 2026-09-10 | GitHub Actions `windows-ci` run `34501119792` passed on commit `788e990`; Verify and portable artifact upload succeeded |
| IDE-WP01 | Native shell + layout | DONE | 2026-09-10 | Windows CI run `34504712942` passed on commit `0c741d7`; user launched the published `RocketIDE.exe` and accepted the native shell/editor smoke test |
| IDE-WP02 | Editor/document foundation | DONE | 2026-09-10 | Same Windows CI run `34504712942` passed; user exercised real multi-tab editing/saving in the published app and accepted the smoke test |
| IDE-WP03 | Workspace + project explorer | IN PROGRESS |  | Implementation prepared locally; Windows CI + workspace smoke test pending |
| IDE-WP04 | Rocket syntax + editor ergonomics | IN PROGRESS |  | Batched with WP03; Windows CI + syntax/typing smoke test pending |
| IDE-WP05 | Rocket tool discovery + validation | READY |  | WP00 dependency satisfied |
| IDE-WP06 | LSP transport + lifecycle | BLOCKED by WP05 |  |  |
| IDE-WP07 | Live diagnostics + Problems | BLOCKED by WP06 |  | WP02 dependency satisfied |
| IDE-WP08 | IntelliSense + semantic tokens | BLOCKED by WP06 |  | WP02 dependency satisfied |
| IDE-WP09 | Navigation + refactoring + fixes | BLOCKED by WP06,WP03 |  |  |
| IDE-WP10 | Check/build/run/test/output | BLOCKED by WP05,WP03 |  |  |
| IDE-WP11 | Search + productivity | BLOCKED by WP03 |  |  |
| IDE-WP12 | Large-file + performance hardening | BLOCKED by WP06,WP11 |  | WP02 dependency satisfied |
| IDE-WP13 | Advanced Rocket tooling | BLOCKED by WP10 |  |  |
| IDE-WP14 | Reliability + recovery | BLOCKED by WP03,WP10 |  |  |
| IDE-WP15 | Distribution | BLOCKED by WP14 |  |  |
| IDE-WP16 | Polish + accessibility | BLOCKED by WP15 |  |  |
| IDE-WP17 | Standalone debugger feasibility/optional | BLOCKED by WP16 |  |  |

## Upstream Rocket requests

Add missing compiler/LSP capabilities here instead of faking them in the IDE.

_No requests recorded at baseline._

---

# Repository Map

The target structure is:

```text
RocketIDE/
  .github/workflows/windows-ci.yml
  docs/
    ROCKET_IDE_DESIGN_SPEC.md
    ROCKET_INTEGRATION_CONTRACT.md
  references/rocket-current/
  scripts/
    verify.ps1
  src/
    RocketIDE.App/
      App.xaml
      App.xaml.cs
      MainWindow.xaml
      MainWindow.xaml.cs
      Views/
      ViewModels/
      Editor/
      Themes/
    RocketIDE.Core/
      Documents/
      Workspaces/
      Diagnostics/
      Output/
      Search/
      Commands/
    RocketIDE.Rocket/
      Tools/
      Compiler/
      LanguageServer/
      Diagnostics/
      Projects/
    RocketIDE.Infrastructure/
      Files/
      Processes/
      Settings/
      Recovery/
      Logging/
  tests/
    RocketIDE.Core.Tests/
    RocketIDE.Rocket.Tests/
    RocketIDE.Infrastructure.Tests/
  Directory.Build.props
  Directory.Packages.props
  RocketIDE.sln
  CONTRIBUTING.md
  README.md
  ROADMAP.md
```

Do not create a monolithic `Services` folder where unrelated responsibilities accumulate.

---

# IDE-WP00 — Repository baseline + Windows CI

**Outcome:** The repository has a reproducible .NET 10 solution, project boundaries, test harness, verification script, and Windows CI. The repository baseline already contains the required scaffolding; this WP validates and repairs it rather than blindly recreating files.

**Files:**
- Review/modify: `Directory.Build.props`
- Review/modify: `Directory.Packages.props`
- Review/modify: `RocketIDE.sln`
- Review/modify: `src/*/*.csproj`
- Review/modify: `tests/*/*.csproj`
- Review/modify: `.github/workflows/windows-ci.yml`
- Review/modify: `scripts/verify.ps1`
- Create if absent: `tests/RocketIDE.Core.Tests/BaselineTests.cs`

### Tasks

- [x] Confirm the Windows target framework is `net10.0-windows10.0.19041.0` for the WPF app and `net10.0` for UI-independent libraries/tests where possible.
- [x] Confirm nullable reference types and implicit usings are enabled.
- [x] Confirm repository-owned compiler warnings are treated as errors.
- [x] Confirm central package management pins AvalonEdit `6.3.1.120`, MSTest `4.4.0`, and Microsoft.NET.Test.Sdk `18.9.0`.
- [x] Confirm project-reference direction is one-way: App -> Rocket/Core/Infrastructure; Rocket -> Core; Infrastructure -> Core; Core -> nothing repository-owned.
- [x] Add baseline tests that prove each test project resolves and loads its intended production assembly.
- [x] Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1` on Windows.
- [x] Push and require `.github/workflows/windows-ci.yml` to pass.
- [x] Record the CI run/commit in the progress ledger and set IDE-WP00 `DONE`.

**Completion evidence (2026-09-10):** GitHub Actions `windows-ci` run `34501119792` completed successfully for commit `788e990`. The `build-test-publish` job passed Setup .NET 10, Verify, and portable verification artifact upload.

**Acceptance:** clean restore, build, tests, and publish smoke artifact on GitHub Actions Windows runner.

**Commit:** `chore: establish RocketIDE build and CI baseline`

---

# IDE-WP01 — Native WPF shell + layout

**Outcome:** Launching `RocketIDE.exe` shows a stable native IDE shell with menu, toolbar, explorer region, editor region, bottom tool-panel region, status bar, resizable splitters, and dark resources. Controls may be non-functional where later WPs own behavior, but disabled state must be explicit rather than pretending functionality.

**Files:**
- Modify: `src/RocketIDE.App/App.xaml`
- Modify: `src/RocketIDE.App/MainWindow.xaml`
- Create: `src/RocketIDE.App/Themes/DarkTheme.xaml`
- Create: `src/RocketIDE.App/ViewModels/MainWindowViewModel.cs`
- Create: `src/RocketIDE.App/Commands/RelayCommand.cs`
- Test: keep logic-only command tests in `tests/RocketIDE.Core.Tests` or App-specific test project only if needed; do not force WPF into all tests.

### Tasks

- [x] Define reusable theme resources for backgrounds, borders, text, selection, error/warning/info accents, editor chrome, splitter, toolbar, tabs, and status bar. Avoid hard-coded colors scattered across XAML.
- [x] Build the three-zone layout: Explorer left, Editor center, Problems/Output/Tests bottom.
- [x] Add splitters with sane minimum widths/heights so panels cannot collapse the editor to zero.
- [x] Add top menus: File, Edit, Selection, View, Navigate, Build, Run, Tools, Help.
- [x] Add toolbar placeholders only for New/Open, Save, Build, Run, Stop, Test. Keep unavailable commands disabled until their owning WP wires them.
- [x] Add bottom status fields for Rocket SDK state, LSP state, active target, line, column, encoding.
- [x] Complete a real Windows launch/layout smoke test. The exhaustive 1366x768 and 100/125/150/200% DPI matrix is intentionally tracked in IDE-WP16 so it is verified once against the near-final UI rather than repeatedly during foundation work.
- [x] Run verification and CI.

**Completion evidence (2026-09-10):** `windows-ci` run `34504712942` passed on commit `0c741d7`. The published Windows artifact launched successfully and the user accepted the native shell/editor smoke test. The screenshot review also identified three cosmetic issues that are repaired in the WP03/WP04 batch: dark title-bar integration, higher selected-tab contrast, and removal of default toolbar gripper/overflow chrome.

**Acceptance:** native window opens without browser/WebView, resizes without overlap, and no toolbar action claims functionality it does not have.

**Commit:** `feat: add native RocketIDE shell`

---

# IDE-WP02 — Editor and document foundation

**Outcome:** Users can open/edit/save multiple text/Rocket files reliably in AvalonEdit tabs with document state and no language-server dependency.

**Core interfaces to produce:**

```csharp
public readonly record struct DocumentId(Guid Value);
public sealed record DocumentSnapshot(DocumentId Id, string Path, string Text, int Version, bool IsDirty, long ByteLength);

public enum DocumentSaveStatus { Saved, NoChanges, Conflict }
public sealed record DocumentSaveResult(DocumentSaveStatus Status, DocumentSnapshot Document, string? Message = null);

public interface IDocumentStore
{
    IReadOnlyList<DocumentSnapshot> OpenDocuments { get; }
    Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken);
    DocumentSnapshot UpdateText(DocumentId id, string text);
    Task<DocumentSaveResult> SaveAsync(DocumentId id, bool overwriteExternalChanges, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken);
    bool TryGet(DocumentId id, out DocumentSnapshot? document);
    bool Close(DocumentId id);
}
```

Names may be refined only if all consumers/tests are updated in the same WP; do not fork duplicate concepts.

**Files:**
- Create: `src/RocketIDE.Core/Documents/*`
- Create: `src/RocketIDE.Infrastructure/Files/*`
- Create: `src/RocketIDE.App/Editor/*`
- Create: `src/RocketIDE.App/ViewModels/DocumentTabViewModel.cs`
- Test: `tests/RocketIDE.Core.Tests/Documents/*`
- Test: `tests/RocketIDE.Infrastructure.Tests/Files/*`

### Tasks

- [x] Write tests for normalized document identity so opening the same Windows path twice activates one tab rather than duplicating buffers.
- [x] Implement document state with monotonically increasing edit version.
- [x] Write tests for dirty state transitions: open clean -> edit dirty -> save clean -> external edit conflict does not silently clear dirty state.
- [x] Implement async UTF-8 file open/save with byte-length tracking and atomic save where appropriate.
- [x] Bind each open document to one AvalonEdit instance or a safe recyclable editor host; do not share one mutable text document across unrelated tabs.
- [x] Implement tabs with filename, dirty marker, close button, middle-click close optional.
- [x] Implement Save, Save All, Close, Close All, Close Others and unsaved-change prompts.
- [x] Add line numbers, current-line highlight, editor font settings baseline, selection, undo/redo.
- [x] Implement goto-line and in-document find/replace using editor/document APIs rather than regex over serialized UI state.
- [x] Update status line/column from caret movement.
- [x] Ensure binary-looking or undecodable files fail gracefully rather than filling the editor with replacement garbage.
- [x] Run focused tests, full verification, and CI.

**Completion evidence (2026-09-10):** `windows-ci` run `34504712942` passed after the one-line `System.IO` import repair in commit `0c741d7`. The user then launched the published Windows artifact and exercised the real editor flow before approving continuation.

**Acceptance:** edit/save/reopen multi-tab flow works without LSP, preserves exact text, and guards unsaved changes.

**Commit:** `feat: add multi-document editor foundation`

---

# IDE-WP03 — Workspace + Rocket project explorer

**Outcome:** Users can open folders/projects, see a scalable tree, operate on files directly, discover nearest Rocket target, and handle external changes safely.

**Interfaces to produce:**

```csharp
public sealed record WorkspaceRoot(string Path);
public sealed record RocketTarget(string InputPath, string WorkingDirectory, string? ManifestPath, bool IsStandalone);

public interface IRocketTargetDiscovery
{
    RocketTarget? Discover(string activePath);
}
```

**Files:**
- Create: `src/RocketIDE.Core/Workspaces/*`
- Create: `src/RocketIDE.Rocket/Projects/RocketTargetDiscovery.cs`
- Create: `src/RocketIDE.Rocket/Projects/RocketManifest.cs`
- Create: `src/RocketIDE.Infrastructure/Files/WorkspaceFileWatcher.cs`
- Create: `src/RocketIDE.App/ViewModels/Explorer/*`
- Test: `tests/RocketIDE.Rocket.Tests/Projects/*`
- Test: `tests/RocketIDE.Infrastructure.Tests/Files/*`

### Tasks

- [x] Port the *behavior* of current Rocket Visual Studio nearest-ancestor `rocket.toml` discovery into a UI-independent class; tests cover manifest ancestor, standalone file, nested directory, non-Rocket file, and nonexistent path.
- [x] Parse only manifest metadata RocketIDE needs for presentation/launch decisions; do not build a second package resolver.
- [x] Add File > Open Folder, Open File, Recent Projects. Recent workspaces persist in `%LOCALAPPDATA%\RocketIDE\recent-workspaces.json` rather than source control.
- [x] Build lazy explorer tree enumeration so opening a large folder does not recursively materialize every node on the UI thread.
- [x] Exclude `.git`, `.rocketc`, `bin`, `obj`, `.vs` by default where appropriate while still allowing reveal if explicitly navigated.
- [x] Implement new file/new folder/rename/delete/copy path/reveal in File Explorer/refresh. Right-click selects the node it will operate on before the context menu opens.
- [x] Prefer Recycle Bin for user-initiated delete; no silent permanent-delete fallback is used.
- [x] Add file watcher coalescing/debounce and tests for external modify/create/delete/rename notifications.
- [x] On an externally modified clean open document, offer reload; on a dirty document, never overwrite silently. External delete/rename is surfaced without discarding the editor buffer.
- [ ] Run verification and CI.

**Local implementation note (2026-09-10):** workspace/project explorer, lazy enumeration, persisted recent projects, direct file operations, nearest-manifest target discovery, debounced file watching, and safe external-change handling are implemented. Windows CI and a real workspace smoke test remain required before IDE-WP03 can be marked DONE.

**Acceptance:** real multi-file project editing works, direct explorer operations are reflected on disk, and target discovery matches Rocket's existing editor behavior.

**Commit:** `feat: add Rocket workspace explorer`

---

# IDE-WP04 — Rocket syntax highlighting + editor ergonomics

**Outcome:** Rocket code is comfortable before the LSP starts and remains readable when the LSP is unavailable.

**Files:**
- Reference: `references/rocket-current/editors/vscode/syntaxes/rocket.tmLanguage.json`
- Reference: `references/rocket-current/editors/vscode/language-configuration.json`
- Create: `src/RocketIDE.App/Editor/RocketSyntaxHighlighting.cs`
- Create: `src/RocketIDE.App/Editor/RocketIndentationStrategy.cs`
- Create: `src/RocketIDE.App/Editor/EditorKeyBehavior.cs`
- Test pure token/config conversion logic where possible.

### Tasks

- [x] Decide and document the baseline syntax-highlighting adapter: port the authoritative Rocket TextMate lexical categories into AvalonEdit XSHD without creating semantic analysis; see `docs/EDITOR_SYNTAX_BASELINE.md`.
- [x] Ensure comments, strings, numbers, keywords, types/declarations, operators, and punctuation have stable basic colors.
- [x] Implement Rocket indentation: Tab inserts spaces; no literal tab indentation by default because Rocket diagnostic `R1002` rejects tab/non-four-space blocks.
- [x] Add indent/outdent selection and smart newline indentation based on editor-local lexical context only, including safe `else:` / `case ...:` auto-dedent when the current line is still at the preceding body indent.
- [x] Evaluate matching-bracket visualization. It is deferred to IDE-WP16 instead of shipping an unverified custom background renderer in this phase; WP04 still provides the authoritative bracket auto-close/surround behavior.
- [x] Add auto-close/surround pairs using Rocket's language configuration; pure rules cover selection wrapping and the balanced-pair decision used by backspace deletion.
- [x] Ensure syntax highlighting remains available when `rocket-lsp.exe` cannot be found; the XSHD layer has no LSP dependency.
- [ ] Run verification and CI.

**Local implementation note (2026-09-10):** lexical Rocket highlighting, four-space indentation, selection indent/outdent, smart block newline/dedent, and Rocket-configured auto-close/surround pairs are implemented without any LSP dependency. Windows CI and a real typing/highlighting smoke test remain required before IDE-WP04 can be marked DONE.

**Acceptance:** a Rocket file is readable and editing respects Rocket indentation rules even with LSP fully disabled.

**Commit:** `feat: add Rocket editor syntax support`

---

# IDE-WP05 — Rocket tool discovery + environment validation

**Outcome:** RocketIDE reliably finds `rocketc.exe` and `rocket-lsp.exe`, shows versions, supports custom paths, and never stores source-controlled machine paths.

**Core interfaces to produce:**

```csharp
public sealed record RocketToolchain(string CompilerPath, string LanguageServerPath, string CompilerVersion, string LanguageServerVersion);
public interface IRocketToolLocator
{
    Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken);
}
```

**Files:**
- Create: `src/RocketIDE.Rocket/Tools/RocketToolLocator.cs`
- Create: `src/RocketIDE.Rocket/Tools/RocketEnvironmentValidator.cs`
- Create: `src/RocketIDE.Infrastructure/Settings/*`
- Create: `src/RocketIDE.App/Views/SettingsWindow.xaml`
- Test: `tests/RocketIDE.Rocket.Tests/Tools/*`

### Tasks

- [ ] Port discovery concepts from the Rocket Visual Studio reference; write tests using temporary fake executable files/paths instead of the developer machine.
- [ ] Support explicit settings and `ROCKET_COMPILER` / `ROCKET_LANGUAGE_SERVER`.
- [ ] Prefer an LSP sibling of the selected compiler when present.
- [ ] Support recognized active Rocket checkout build/package output candidates without hard-coded checkout paths.
- [ ] Support PATH lookup.
- [ ] Add bundled SDK candidate location under RocketIDE installation directory but do not require bundled SDK during development.
- [ ] Execute `--version` safely with timeout/cancellation and capture output.
- [ ] Add Tools > Validate Rocket Environment that displays selected paths, versions, and discovery problems in Output plus a user-facing summary.
- [ ] Add settings UI for compiler path, LSP path, automatic discovery reset, and optional pinned-repository environment loading only if the behavior is safely ported/tested.
- [ ] Run verification and CI.

**Acceptance:** the same repo can run on two Windows machines with different checkout locations without source edits.

**Commit:** `feat: add Rocket toolchain discovery`

---

# IDE-WP06 — LSP framing, JSON-RPC, lifecycle, document synchronization

**Outcome:** RocketIDE has a tested dependency-light LSP client capable of running the real `rocket-lsp.exe` without corrupting protocol streams.

**Interfaces to produce:**

```csharp
public interface IRocketLanguageClient : IAsyncDisposable
{
    bool IsInitialized { get; }
    Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken);
    Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken);
    Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

Implementation may introduce typed higher-level methods later, but transport remains isolated.

**Files:**
- Create: `src/RocketIDE.Rocket/LanguageServer/LspFrameReader.cs`
- Create: `src/RocketIDE.Rocket/LanguageServer/LspFrameWriter.cs`
- Create: `src/RocketIDE.Rocket/LanguageServer/JsonRpcConnection.cs`
- Create: `src/RocketIDE.Rocket/LanguageServer/RocketLanguageClient.cs`
- Create: `src/RocketIDE.Rocket/LanguageServer/LspDtos/*`
- Create: `src/RocketIDE.Rocket/LanguageServer/DocumentSynchronizer.cs`
- Test: `tests/RocketIDE.Rocket.Tests/LanguageServer/*`

### Tasks

- [ ] Write byte-stream tests for one valid `Content-Length` frame split at every practical boundary (header fragmented, body fragmented, back-to-back frames).
- [ ] Test malformed header, duplicate/invalid Content-Length, header >16 KiB, body >16 MiB, premature EOF.
- [ ] Implement bounded frame reader without `ReadLineAsync` assumptions that can allocate unbounded attacker-controlled lines.
- [ ] Implement frame writer with exact UTF-8 byte Content-Length, not character count.
- [ ] Write JSON-RPC tests for incrementing request IDs, notification no-response behavior, response correlation, error responses, unknown response ID, cancellation, and server notification dispatch.
- [ ] Implement one reader loop per connection and thread-safe pending-request table.
- [ ] Spawn `rocket-lsp.exe` hidden with redirected stdin/stdout/stderr; stdout only feeds protocol parser; stderr feeds Output log channel.
- [ ] Implement initialize -> initialized lifecycle and capture server capabilities.
- [ ] Implement shutdown -> exit with forced process-tree cleanup fallback.
- [ ] Negotiate UTF-16 positions and incremental sync.
- [ ] Implement `didOpen`, versioned `didChange`, optional `didSave`, `didClose` mapping from open document lifecycle.
- [ ] Never send a source document >4 MiB. Expose a typed `LargeFileUnsupportedByLsp` state before transport.
- [ ] Implement `$/cancelRequest` for cancelled outstanding requests when appropriate.
- [ ] Implement `workspace/didChangeConfiguration` for current Rocket max project files/bytes and telemetry settings if exposed by IDE.
- [ ] Implement `rocket/projectStatus` and `rocket/analysisStatus` parsing into typed status models.
- [ ] Run all transport tests repeatedly to expose race/flakiness.
- [ ] Optional real-tool integration smoke: initialize current `rocket-lsp`, open a fixture, receive response, clean shutdown.
- [ ] Run verification and CI.

**Acceptance:** protocol tests pass deterministically and a real Rocket LSP can initialize without any protocol bytes being mixed with logs.

**Commit:** `feat: add Rocket LSP transport and lifecycle`

---

# IDE-WP07 — Live diagnostics + Problems panel

**Outcome:** Errors/warnings from Rocket appear while typing with correct ranges, squiggles, codes, and clickable navigation.

**Models to produce:**

```csharp
public enum DiagnosticSeverity { Error, Warning, Information, Hint }
public sealed record SourceRange(int StartLine, int StartCharacter, int EndLine, int EndCharacter);
public sealed record RocketDiagnostic(string Source, string Code, string Message, DiagnosticSeverity Severity, string FilePath, SourceRange Range);
```

**Files:**
- Create: `src/RocketIDE.Core/Diagnostics/*`
- Create: `src/RocketIDE.Rocket/Diagnostics/LspDiagnosticMapper.cs`
- Create: `src/RocketIDE.App/Editor/DiagnosticRenderer.cs`
- Create: `src/RocketIDE.App/ViewModels/ProblemsViewModel.cs`
- Test: mapper/range/version handling.

### Tasks

- [ ] Map LSP severities and stable `Rdddd` codes without message-text parsing.
- [ ] Key live diagnostics by document URI + server publication generation/current document version policy so stale results are not rendered over newer text.
- [ ] Render error/warning/info squiggles without mutating document text.
- [ ] Hovering a diagnostic decoration shows code + message + source.
- [ ] Problems panel filters by severity and text, groups optionally by file, and displays file/line/column.
- [ ] Double-click/Enter opens file and selects/navigates to range.
- [ ] Closing a document honors Rocket's empty-diagnostics clear and removes stale display.
- [ ] Show LSP disconnected/degraded state distinctly from "zero problems".
- [ ] Test UTF-16 ranges involving surrogate pairs so line/column mapping does not drift.
- [ ] Run verification and CI.

**Acceptance:** intentionally invalid Rocket produces compiler-code-accurate live Problems entries and editor squiggles at the correct text.

**Commit:** `feat: show live Rocket diagnostics`

---

# IDE-WP08 — Completion, hover, signature help, semantic tokens

**Outcome:** RocketIDE provides useful IntelliSense by rendering Rocket LSP responses, not hard-coded keyword lists posing as semantic completion.

**Files:**
- Create: `src/RocketIDE.Rocket/LanguageServer/Features/*`
- Create: `src/RocketIDE.App/Editor/Completion/*`
- Create: `src/RocketIDE.App/Editor/Hover/*`
- Create: `src/RocketIDE.App/Editor/SignatureHelp/*`
- Create: `src/RocketIDE.App/Editor/SemanticTokens/*`
- Test protocol mapping and edit application.

### Tasks

- [ ] Implement completion request at caret with cancellation/debounce suitable for typing.
- [ ] Render label/detail/documentation/kind and selected insertion text/textEdit correctly.
- [ ] Apply additional text edits such as automatic import only after validating edits against open workspace/document bounds.
- [ ] Implement Markdown hover rendering using a safe WPF presentation subset; do not execute HTML/scripts.
- [ ] Make `rocket-doc://` links either resolve through a safe documentation handler or remain visibly copyable; never hand arbitrary schemes to the shell without validation.
- [ ] Implement signature help with active signature/parameter highlighting and Rocket named/default parameter metadata as provided by server.
- [ ] Implement semantic tokens full response decoding using server legend.
- [ ] Implement semantic token delta updates with fallback to full when cache/result IDs are invalid.
- [ ] Ensure semantic tokens overlay/augment basic syntax rather than leaving the file colorless when LSP disconnects.
- [ ] Cancel stale completion/hover/signature requests when caret/document changes.
- [ ] Run verification and CI.

**Acceptance:** completions, hovers, signatures and semantic colors are driven by the real LSP and remain responsive under rapid typing.

**Commit:** `feat: add Rocket IntelliSense`

---

# IDE-WP09 — Navigation, rename, code actions, formatting

**Outcome:** Go-to-definition, references, safe rename, Rocket-owned quick fixes, and formatting work across a multi-file project.

**Files:**
- Create: `src/RocketIDE.Rocket/LanguageServer/Features/NavigationClient.cs`
- Create: `src/RocketIDE.Rocket/LanguageServer/Features/WorkspaceEditValidator.cs`
- Create: `src/RocketIDE.App/Views/ReferencesPanel.xaml`
- Create: `src/RocketIDE.App/ViewModels/ReferencesViewModel.cs`
- Test: workspace edit validation/apply transaction behavior.

### Tasks

- [ ] Implement F12/go-to-definition and open target file/range.
- [ ] Implement find references in a reusable references/search-results panel.
- [ ] Implement prepare-rename before showing rename commit UI.
- [ ] Request rename and validate every returned edit: <=1,024 total edits; writable workspace/open files only; valid normalized paths; valid document versions/ranges where available.
- [ ] Apply multi-file edits transactionally enough that a failure does not leave half the workspace silently modified. Keep original snapshots for rollback/reporting.
- [ ] Implement code-action lightbulb/context menu. Label actions as server-provided.
- [ ] Confirm `R4002` quick fixes and formatting actions flow through LSP rather than IDE heuristics.
- [ ] Implement Format Document via LSP for open supported files.
- [ ] Implement Format Target/Workspace through `rocketc fmt` later in tool-command service where appropriate; do not conflate it with document formatting.
- [ ] Test conflicting/invalid/out-of-workspace edit rejection.
- [ ] Run verification and CI.

**Acceptance:** rename across files changes only server-resolved symbol edits and no regex/textual rename fallback exists.

**Commit:** `feat: add Rocket navigation and refactoring`

---

# IDE-WP10 — Check/build/run/test/stop + Output

**Outcome:** RocketIDE can operate real Rocket targets and provide structured compiler/test feedback plus streamed program output.

**Interfaces to produce:**

```csharp
public sealed record ProcessRunResult(int ExitCode, bool Cancelled);
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessStartRequest request, IProgress<ProcessOutput> output, CancellationToken cancellationToken);
    void StopActive();
}
```

**Files:**
- Create: `src/RocketIDE.Infrastructure/Processes/WindowsJobProcessRunner.cs`
- Create: `src/RocketIDE.Rocket/Compiler/RocketMessage.cs`
- Create: `src/RocketIDE.Rocket/Compiler/RocketMessageParser.cs`
- Create: `src/RocketIDE.Rocket/Compiler/RocketCommandBuilder.cs`
- Create: `src/RocketIDE.Rocket/Compiler/RocketCommandService.cs`
- Create: `src/RocketIDE.App/ViewModels/OutputViewModel.cs`
- Create: `src/RocketIDE.App/ViewModels/TestsViewModel.cs`
- Test: process quoting/cancellation, message parser, command builder.

### Tasks

- [ ] Port/test Windows command-line quoting behavior from Rocket Visual Studio reference.
- [ ] Build a Windows Job Object runner that assigns child process before it can meaningfully escape; ensure Stop/Dispose terminates the tree.
- [ ] Keep tool window hidden and streams redirected.
- [ ] Implement line-stream callbacks without deadlocking stdout/stderr.
- [ ] Parse only lines with schema `rocket-message-1` as structured compiler events; preserve unknown/plain output text in Output.
- [ ] Add parser tests for diagnostic, build-finished, test-started, test-finished, test-summary, malformed JSON, unknown schema, missing optional fields.
- [ ] Add command builder tests for package target and standalone file target.
- [ ] Wire Check, Build, Test with `--message-format=json` in the correct argument position accepted by active Rocket.
- [ ] Convert compiler diagnostic events to Problems entries with source provenance and one-based span conversion.
- [ ] Implement Run with streamed stdout/stderr and program arguments setting.
- [ ] Implement Stop to cancel compiler or application process tree.
- [ ] Disable Run for library output where manifest/tool metadata proves it is not executable.
- [ ] Prevent accidental concurrent Build/Run/Test conflict through explicit command state; do not solve by ignoring clicks.
- [ ] Tests panel displays per-test PASS/FAIL and summary from structured test events.
- [ ] Build output displays artifact and cache hit when reported.
- [ ] Run verification and CI with fake-process unit tests; add opt-in real Rocket command smoke when available.

**Acceptance:** a sample Rocket project can build, run, stop, and test from RocketIDE with no extra console window and structured errors navigate to source.

**Commit:** `feat: integrate Rocket build run and tests`

---

# IDE-WP11 — Workspace search, replace, recent projects, productivity

**Outcome:** Large multi-file projects are practical to navigate and edit without recursively loading the workspace into memory/UI.

**Files:**
- Create: `src/RocketIDE.Core/Search/*`
- Create: `src/RocketIDE.Infrastructure/Files/WorkspaceSearchService.cs`
- Create: `src/RocketIDE.App/Views/SearchPanel.xaml`
- Create: `src/RocketIDE.App/ViewModels/SearchViewModel.cs`
- Test: search filters, cancellation, replace safety.

### Tasks

- [ ] Implement cancellable async file enumeration with default exclusions `.git`, `.rocketc`, `bin`, `obj`, `.vs`.
- [ ] Search supported text files as streams/individual buffers; cap individual preview lines and total in-memory UI result population.
- [ ] Stream results incrementally to UI in batches to avoid Dispatcher work per character/match.
- [ ] Support case sensitive, whole word, plain text; regex may be added only with timeout/bounds and tests.
- [ ] Clicking result opens and selects match.
- [ ] Implement replace-in-files preview with explicit file/match list before applying destructive bulk edits.
- [ ] Open dirty buffers participate using in-memory text so search does not disagree with visible edits.
- [ ] Persist recent project list and remove nonexistent entries gracefully.
- [ ] Add keyboard shortcuts for open, quick file navigation, search, build, run, stop, problems/output toggle.
- [ ] Run stress fixture with thousands of small files and verify cancellation keeps UI responsive.
- [ ] Run verification and CI.

**Acceptance:** searching a multi-thousand-file fixture can be cancelled and does not freeze the editor or scan generated folders by default.

**Commit:** `feat: add workspace search and productivity tools`

---

# IDE-WP12 — Large File Mode + performance hardening

**Outcome:** RocketIDE remains usable on large files/projects and handles Rocket LSP limits explicitly rather than failing unpredictably.

**Files:**
- Create: `src/RocketIDE.Core/Documents/LargeFilePolicy.cs`
- Modify document/LSP/editor services as needed.
- Add performance fixtures generated during tests rather than committing huge binaries.

### Tasks

- [ ] Unit-test the exact 4 MiB threshold in UTF-8 bytes: below allowed, exactly boundary per Rocket contract interpretation verified against server, above denied. Use byte count, not `string.Length`.
- [ ] Decide basic syntax-coloring cutoff based on measured AvalonEdit performance; document it separately from the 4 MiB LSP contract.
- [ ] Add visible Large File Mode banner with exact reason and available/disabled features.
- [ ] Prevent LSP `didOpen`, semantic, completion, diagnostics requests for that document.
- [ ] Ensure local find/goto/save remains available.
- [ ] Avoid eager `File.ReadAllText` for files whose size demands streaming/guard checks; perform metadata/size validation first.
- [ ] Debounce/coalesce rapid document sync while preserving incremental version order.
- [ ] Audit all background continuations for accidental UI-thread blocking.
- [ ] Add cancellation to workspace scan/search and compiler/LSP operations not already covered.
- [ ] Use `rocket/analysisStatus` to show analysis activity/latency unobtrusively, not as modal UI.
- [ ] Test project-status response and surface if project exceeds server bounds.
- [ ] Profile memory with generated large fixture and fix retained closed documents/event subscriptions.
- [ ] Run verification and CI.

**Acceptance:** opening >4 MiB Rocket text does not crash/freeze LSP; editor stays usable with explicit degraded semantics.

**Commit:** `perf: harden RocketIDE for large sources`

---

# IDE-WP13 — Advanced Rocket tooling

**Outcome:** RocketIDE exposes useful Rocket-specific package and measurement commands after the core editor is stable.

**Files:**
- Extend: `src/RocketIDE.Rocket/Compiler/RocketCommandService.cs`
- Create views/dialogs only where a command benefits from structured presentation.

### Tasks

- [ ] Add New Rocket Project using `rocketc new`; validate destination and never overwrite an existing nonempty directory without explicit confirmation.
- [ ] Add Resolve Dependencies: normal, `--locked`, optional offline mode exposed clearly.
- [ ] Add Dependency Tree output with copy support.
- [ ] Add Audit Dependencies with diagnostics/errors clearly separated from harmless output.
- [ ] Add Target Information via `rocketc target [--verbose]` after verifying active version flags.
- [ ] Add Format Target/Workspace with `rocketc fmt`; support check-only only where UI meaning is clear.
- [ ] Add Coverage command with output path under a user-selected or `.rocketc`/IDE temp location; parse `rocket-coverage-1` only if documented/needed for UI, otherwise open/reveal generated report.
- [ ] Add Profile and Benchmark analogously; never run these automatically on file open or save.
- [ ] Commands that may execute native code require explicit user action and show active target.
- [ ] Add tests for command argument construction and output-path quoting.
- [ ] Run verification and CI.

**Acceptance:** advanced tools are explicit, cancellable, use the selected Rocket SDK, and do not make project opening executable.

**Commit:** `feat: add advanced Rocket tooling`

---

# IDE-WP14 — Reliability, session restore, crash recovery, logging

**Outcome:** RocketIDE can be used daily without losing work or becoming mysterious after a process/protocol failure.

**Files:**
- Create: `src/RocketIDE.Infrastructure/Recovery/*`
- Create: `src/RocketIDE.Infrastructure/Logging/*`
- Extend settings/session models.
- Create UI for recovery choice and log location.

### Tasks

- [ ] Persist last workspace, open document paths, active tab, panel visibility/sizes, window bounds in per-user app data.
- [ ] Clamp restored window coordinates to current display bounds so changed monitor layouts do not open off-screen.
- [ ] Implement periodic crash-recovery snapshots only for dirty buffers, stored outside repo.
- [ ] Recovery snapshot includes original path, saved-file fingerprint/timestamp, buffer version, and text; never overwrites source automatically.
- [ ] On startup after unclean exit, offer Restore/Discard per recovery set; if disk changed too, make conflict explicit.
- [ ] Clear recovery state after successful save/discard/clean shutdown.
- [ ] Add rotating application logs without source text by default; secrets/environment credentials must not be logged.
- [ ] LSP crash: show disconnected status, clear/mark stale semantic state, allow safe restart, do not lose editor buffers.
- [ ] Compiler process crash/nonzero exit: keep output, release command busy state, permit rerun.
- [ ] Add global unhandled-exception logging/recovery boundary without swallowing fatal corrupted-state cases.
- [ ] Test recovery serialization and stale/conflict decisions with temp files.
- [ ] Run forced-kill/manual recovery smoke on Windows.
- [ ] Run verification and CI.

**Acceptance:** killing RocketIDE with an unsaved buffer allows recovery on next launch without silently overwriting a changed source file.

**Commit:** `feat: add RocketIDE session and crash recovery`

---

# IDE-WP15 — Windows distribution

**Outcome:** A non-developer can download a Windows x64 RocketIDE artifact and launch it without installing .NET separately.

**Files:**
- Extend: `.github/workflows/windows-ci.yml`
- Create: `scripts/package.ps1`
- Create: `docs/DISTRIBUTION.md`
- Optional later: installer project/script after portable package gate.

### Tasks

- [ ] Publish self-contained `win-x64` Release build.
- [ ] Validate single-file publishing with AvalonEdit/WPF; if framework/runtime behavior makes single-file brittle, prefer a reliable self-contained folder over forcing one-file marketing. `RocketIDE.exe` remains the entry point either way.
- [ ] Create portable ZIP with deterministic top-level folder.
- [ ] Exclude `.pdb` from end-user release only after deciding whether crash diagnostics benefit from symbols; keep symbols as separate CI artifact if excluded.
- [ ] Add `--version` or About version tied to assembly informational version/commit.
- [ ] Add bundled Rocket SDK packaging mode only when a verified distributable Rocket package is supplied; do not copy random development build trees.
- [ ] Validate external SDK mode remains supported even in bundled release.
- [ ] CI smoke launches or at minimum starts process and verifies clean startup on Windows runner where desktop-interactive limits allow; otherwise use a non-UI startup verification hook designed for CI.
- [ ] Generate SHA-256 checksums for release archive.
- [ ] Document portable install/update/uninstall behavior.
- [ ] Only after portable release is reliable, optionally add installer; installer is not allowed to hide unresolved portable bugs.
- [ ] Run verification and CI.

**Acceptance:** CI produces a clean downloadable Windows x64 artifact and checksum from a tagged/release build path.

**Commit:** `build: package RocketIDE for Windows x64`

---

# IDE-WP16 — UX polish, accessibility, final 1.0 hardening

**Outcome:** Core behavior is consistent, keyboard-friendly, DPI-safe, and visually coherent without sacrificing editor speed.

### Tasks

- [ ] Audit every menu/toolbar command for correct enabled/disabled/busy state.
- [ ] Audit keyboard access and visible shortcuts; ensure editor text keys are not stolen by global commands.
- [ ] Add command palette/quick-open only if it materially improves workflow and reuses existing command registry; do not create a second command system.
- [ ] Add light theme only if dark theme resources are fully centralized; otherwise defer rather than duplicate hard-coded XAML.
- [ ] Test 100%, 125%, 150%, 200% DPI.
- [ ] Test minimum supported window size and 1366x768.
- [ ] Add and visually verify matching-bracket highlighting if a tested AvalonEdit background renderer can be kept purely lexical and low-cost.
- [ ] Add accessible names/tooltips for icon-only buttons and meaningful keyboard focus order.
- [ ] Verify error/warning state does not rely on color alone.
- [ ] Audit empty/loading/error states for Explorer, Problems, Output, Tests, LSP offline, compiler missing.
- [ ] Remove dead placeholders/debug UI and unused dependencies.
- [ ] Run full manual acceptance matrix against real Rocket package: create -> edit -> error -> fix -> format -> build -> run -> stop -> test -> rename -> reopen/recover.
- [ ] Run `scripts/verify.ps1`, Release publish, and CI.
- [ ] Update README with screenshots only after behavior is accepted; screenshots are documentation, not evidence.

**Acceptance:** 1.0 workflow is coherent on a clean Windows machine and all critical features have clear degraded states.

**Commit:** `release: harden RocketIDE 1.0 experience`

---

# IDE-WP17 — Standalone debugger feasibility and optional implementation

**Outcome:** Decide based on evidence whether RocketIDE can provide a maintainable native debugger without embedding Visual Studio. This WP is not allowed to block a good 1.0 editor/build environment.

### Phase A — Feasibility (mandatory before implementation)

- [ ] Read current Rocket PDB/source-map contract and existing Visual Studio debugger integration snapshot.
- [ ] Determine available Windows debugger backend choices that can legally/technically be redistributed and support CodeView PDB stepping, breakpoints, threads, call stacks, and locals.
- [ ] Prefer a DAP-capable or well-isolated debug engine over writing Win32 debugging from scratch unless no reasonable option exists.
- [ ] Prototype against a tiny Rocket debug build and prove breakpoint binding to Rocket source, continue, step, call stack, locals.
- [ ] Document limitations including duplicate source basenames in the frozen current Rocket source-map contract if still applicable.
- [ ] If feasibility is poor, record `ROCKET-UPSTREAM-REQUEST:` for a Rocket DAP/debug adapter and close WP17 as DEFERRED with evidence. Do not ship a fake debugger.

### Phase B — Implementation (only if Phase A passes)

- [ ] Create isolated `RocketIDE.Debugger` project so debugger complexity does not infect Core/LSP/compiler services.
- [ ] Implement debug build command and artifact validation (`.exe`, `.pdb`, `.rocket.map.json` as applicable).
- [ ] Implement breakpoints, launch/stop, continue/pause, step over/in/out.
- [ ] Add call stack, locals, threads panels only when backend data is real.
- [ ] Capture program output consistently with Run where backend permits.
- [ ] Add integration tests for protocol/backend adapter plus manual real Rocket debug acceptance.
- [ ] Run verification and CI.

**Acceptance:** either a real verified debugger exists, or the WP ends explicitly deferred with a documented upstream requirement. There is no cosmetic debugger mode.

**Commit if implemented:** `feat: add Rocket native debugging`

---

# Final 1.0 Acceptance Matrix

RocketIDE 1.0 must not be declared ready until every non-deferred row passes on a clean Windows x64 environment.

- [ ] Launch RocketIDE without Visual Studio/VS Code.
- [ ] Open standalone `.rocket` file.
- [ ] Open package directory with `rocket.toml`.
- [ ] Create/open/edit/save multiple source files.
- [ ] Explorer create/rename/delete/refresh works on disk.
- [ ] Syntax colors work without LSP.
- [ ] `rocket-lsp` discovers and initializes.
- [ ] Unsaved edits are synchronized.
- [ ] Live `Rdddd` diagnostics point to correct source ranges.
- [ ] Problems navigation works.
- [ ] Completion works from LSP.
- [ ] Hover works from LSP.
- [ ] Signature help works from LSP.
- [ ] Semantic tokens work and degrade cleanly offline.
- [ ] Go to definition works.
- [ ] Find references works.
- [ ] Rename uses server workspace edits only.
- [ ] Quick fixes use server code actions only.
- [ ] Format Document uses LSP.
- [ ] Check works and structured diagnostics display.
- [ ] Build works and artifact/cache information displays.
- [ ] Run streams output.
- [ ] Stop terminates child process tree.
- [ ] Tests show PASS/FAIL/summary.
- [ ] Workspace search is cancellable.
- [ ] External file modification cannot silently overwrite dirty buffer.
- [ ] >4 MiB source enters Large File Mode and is not sent to LSP.
- [ ] Missing compiler/LSP gives clear setup state, not crash.
- [ ] LSP crash can recover/restart without losing text.
- [ ] Unclean IDE exit can recover dirty buffers.
- [ ] Self-contained Windows x64 release artifact launches.
- [ ] GitHub Windows CI passes.

---

# Completion Note Template

Append one note under the relevant WP before marking it DONE:

```text
Completion evidence — IDE-WPxx
Commit: <hash>
Focused tests: <command + result>
Full verify: <command + result>
Windows CI: <run/link or exact status>
Manual checks: <only checks actually performed>
Known limitations: <none, or explicit list>
```

Never write `Known limitations: none` unless you actually checked the WP acceptance scope.
