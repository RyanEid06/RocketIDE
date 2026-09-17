# RocketIDE Roadmap

> **Historical baseline:** IDE-WP00 through IDE-WP17 below record the completed
> foundation cycle. New final-cycle work is governed by
> [`docs/ROCKETIDE_FINAL_ROADMAP_IMPLEMENTATION.md`](docs/ROCKETIDE_FINAL_ROADMAP_IMPLEMENTATION.md).
> Do not reuse the historical WP numbers for new work, and do not change a
> historical status without new evidence.

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
| IDE-WP03 | Workspace + project explorer | DONE | 2026-09-10 | Repairs re-verified on Windows; current Windows CI passed on commit `ee0ee1e`; real Rocket workspace smoke test opened/browsed the full repository and files successfully |
| IDE-WP04 | Rocket syntax + editor ergonomics | DONE | 2026-09-10 | AvalonEdit XSHD/comment-regex regressions repaired with runtime `DocumentHighlighter` coverage; current Windows CI passed on commit `ee0ee1e`; real `.rocket` editing/highlighting smoke test passed |
| IDE-WP05 | Rocket tool discovery + validation | DONE | 2026-09-10 | Local `verify.ps1` passed with 77/77 tests; Windows CI passed on commit `ee0ee1e`; real Rocket checkout smoke found `rocketc 2.1.0` and `rocket-lsp 1.0.0` without source edits |
| IDE-WP06 | LSP transport + lifecycle | DONE | 2026-09-10 | Local `verify.ps1` passed with 77/77 tests; Windows CI passed on commit `ee0ee1e`; real `rocket-lsp 1.0.0` initialized, synchronized an open Rocket file, and shut down cleanly with no lingering process |
| IDE-WP07 | Live diagnostics + Problems | DONE | 2026-09-11 | Windows verification passed; live `rocket-lsp` diagnostics, squiggles, Problems filtering/navigation, stale-version rejection, and offline clearing smoke-tested on commit `1b741bb`; GitHub CI green |
| IDE-WP08 | IntelliSense + semantic tokens | DONE | 2026-09-11 | Windows verification passed with 164/164 tests; completion, hover, semantic-token presentation, dark popup/selection styling, cancellation/transport hardening, and standalone-file LSP stability smoke-tested; incomplete-call signature response is blocked by an upstream Rocket LSP limitation recorded below |
| IDE-WP09 | Navigation + refactoring + fixes | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-13 | Implementation remains automated-green in merged `main`; final navigation/refactor/code-action/format GUI smoke is intentionally deferred to Codex/manual acceptance. |
| IDE-WP10 | Check/build/run/test/output | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-14 | Implementation remains automated-green in merged `main`; real build/run/test/stop GUI smoke is intentionally deferred to Codex/manual acceptance. |
| IDE-WP11 | Search + productivity | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-14 | Implemented/hardened and retained green in final merged verification; search/replace dirty-buffer/cancel/stress GUI smoke remains deferred. |
| IDE-WP12 | Large-file + performance hardening | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-14 | Implemented/hardened and retained green in final merged verification; real >4 MiB editor/LSP and responsiveness smoke remains deferred. |
| IDE-WP13 | Advanced Rocket tooling | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-14 | Implemented and retained green in final merged verification; real configured-SDK advanced-command smoke remains deferred. |
| IDE-WP14 | Reliability + recovery | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-14 | Recovery lifecycle/data-safety hardening remains green in merged `main`; forced-crash, recovery-decision, and external-change GUI smoke remains deferred. |
| IDE-WP15 | Distribution | AUTOMATED VERIFIED / CLEAN-MACHINE SMOKE DEFERRED | 2026-09-14 | Final merged verification passed multi-file self-contained win-x64 publish with `RocketIDE.Debugger.dll` + `amd64/EngHost.exe`; current `windows-ci` also produced verification and portable-package artifacts. Clean-machine launch remains manual. |
| IDE-WP16 | Polish + accessibility | AUTOMATED VERIFIED / MANUAL DEFERRED | 2026-09-14 | Implementation/hardening remains green in merged `main`; DPI/accessibility/keyboard GUI matrix intentionally remains deferred. |
| IDE-WP17 | Standalone debugger feasibility/optional | AUTOMATED VERIFIED / LIVE DEBUG SMOKE DEFERRED | 2026-09-14 | Merged `main` (`bf30f98`) passed 348/348 tests, win-x64 publish, debugger DLL/`EngHost.exe` guards, and current `windows-ci` packaging. Real tiny-Rocket breakpoint/step/threads/stack/locals/output smoke remains intentionally deferred. |

## Upstream Rocket requests

Add missing compiler/LSP capabilities here instead of faking them in the IDE.

- **LSP-PERF-01 — large-workspace interactive analysis:** `textDocument/didChange` currently causes broad workspace re-analysis in the full Rocket checkout (~239 files, observed ~20–25 s per analysis) and rapid edits can backlog obsolete work. Profile and optimize in the main Rocket repository without duplicating language intelligence in RocketIDE; target sub-second normal edit feedback while preserving full-analysis equivalence.
- **LSP-SIG-01 — signature help for incomplete calls:** `textDocument/signatureHelp` should continue returning callable signature/active-parameter information while the user is in a temporarily invalid/incomplete call such as `print(|)`. RocketIDE's trigger/request/presentation path is implemented, but the current Rocket LSP can lose the callable information after semantic analysis reports the incomplete call.
- **WP17 debugger backend resolution:** the prior standalone-debugger upstream request is resolved in RocketIDE by the redistributable Microsoft DbgX/DbgEng backend. Rocket still owns the existing `--debug` EXE/PDB/`rocket-source-map-1` contract; no Rocket compiler/LSP change is required.

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
      Integration/
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
    RocketIDE.Debugger/
      native debugger transport, protocol, session state, source-map identity
  tests/
    RocketIDE.App.Tests/
    RocketIDE.Core.Tests/
    RocketIDE.Rocket.Tests/
    RocketIDE.Infrastructure.Tests/
    RocketIDE.Debugger.Tests/
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
- [x] Run verification and CI.

**Completion note (2026-09-10):** workspace/project explorer, lazy enumeration, persisted recent projects, direct file operations, nearest-manifest target discovery, debounced file watching, and safe external-change handling are implemented. Windows verification, current CI, and real workspace smoke testing passed; IDE-WP03 is complete.

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
- [x] Run verification and CI.

**Completion note (2026-09-10):** lexical Rocket highlighting, four-space indentation, selection indent/outdent, smart block newline/dedent, and Rocket-configured auto-close/surround pairs are implemented without any LSP dependency. The repaired AvalonEdit highlighting path, Windows verification/current CI, and real typing/highlighting smoke testing passed; IDE-WP04 is complete.

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
    Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken);
}
```

**Files:**
- Create: `src/RocketIDE.Rocket/Tools/RocketToolLocator.cs`
- Create: `src/RocketIDE.Rocket/Tools/RocketEnvironmentValidator.cs`
- Create: `src/RocketIDE.Infrastructure/Settings/*`
- Create: `src/RocketIDE.App/Views/SettingsWindow.xaml`
- Test: `tests/RocketIDE.Rocket.Tests/Tools/*`

### Tasks

- [x] Port discovery concepts from the Rocket Visual Studio reference; write tests using temporary fake executable files/paths instead of the developer machine.
- [x] Support explicit settings and `ROCKET_COMPILER` / `ROCKET_LANGUAGE_SERVER`.
- [x] Prefer an LSP sibling of the selected compiler when present.
- [x] Support recognized Rocket checkout build/package output candidates without hard-coded checkout paths. Workspace-derived outputs require explicit per-user checkout trust; a development sibling `Rocket` SDK discovered solely beside a recognized RocketIDE installation directory is allowed as an independent trusted fallback. Merely opening source-controlled content never makes its binaries trusted.
- [x] Support PATH lookup.
- [x] Add bundled SDK candidate location under RocketIDE installation directory but do not require bundled SDK during development.
- [x] Execute `--version` safely with timeout/cancellation and capture output.
- [x] Add Tools > Validate Rocket Environment that displays selected paths, versions, and discovery problems in Output plus a user-facing summary.
- [x] Add settings UI for compiler path, LSP path, discovery reset, and explicit trust/untrust of the current checkout. Trust is stored only in `%LOCALAPPDATA%` and never in source control.
- [x] Run verification and CI.

**Completion note (2026-09-10):** discovery/settings/version validation passed automated tests and real Windows smoke testing. With the Rocket checkout open, RocketIDE resolved `rocketc 2.1.0` and `rocket-lsp 1.0.0`, displayed a valid environment, and required no source-controlled machine path.

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
    event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;
    event EventHandler<RocketTransportFaultedEventArgs>? Faulted;
    event EventHandler<string>? LogReceived;
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

- [x] Write byte-stream tests for one valid `Content-Length` frame split at every practical boundary (header fragmented, body fragmented, back-to-back frames).
- [x] Test malformed header, duplicate/invalid Content-Length, header >16 KiB, body >16 MiB, premature EOF.
- [x] Implement bounded frame reader without `ReadLineAsync` assumptions that can allocate unbounded attacker-controlled lines.
- [x] Implement frame writer with exact UTF-8 byte Content-Length, not character count.
- [x] Write JSON-RPC tests for incrementing request IDs, notification no-response behavior, response correlation, error responses, unknown response ID, cancellation, and server notification dispatch.
- [x] Implement one reader loop per connection and thread-safe pending-request table.
- [x] Spawn `rocket-lsp.exe` hidden with redirected stdin/stdout/stderr; stdout only feeds protocol parser; stderr feeds Output log channel.
- [x] Implement initialize -> initialized lifecycle and capture server capabilities.
- [x] Implement shutdown -> exit with forced process-tree cleanup fallback.
- [x] Negotiate UTF-16 positions and incremental sync.
- [x] Implement `didOpen`, versioned `didChange`, optional `didSave`, `didClose` mapping from open document lifecycle.
- [x] Never send a source document >4 MiB. Expose a typed `LargeFileUnsupportedByLsp` state before transport.
- [x] Implement `$/cancelRequest` for cancelled outstanding requests when appropriate.
- [x] Implement `workspace/didChangeConfiguration` for current Rocket max project files/bytes and telemetry settings if exposed by IDE.
- [x] Add typed models for `rocket/projectStatus` and `rocket/analysisStatus`; consume `rocket/analysisStatus` in the session status path. A `rocket/projectStatus` request/consumer is deferred until a later WP actually needs project-status data.
- [x] Run all transport tests repeatedly to expose race/flakiness.
- [x] Optional real-tool integration smoke: initialize current `rocket-lsp`, open a fixture, receive response, clean shutdown.
- [x] Run verification and CI.

**Completion note (2026-09-10):** transport/lifecycle/document-sync tests passed as part of the 77-test Windows verification suite. Real `rocket-lsp 1.0.0` initialized against the Rocket checkout, reported online, synchronized an open `.rocket` document, and terminated cleanly after normal IDE shutdown. The conditional `workspace/didChangeConfiguration` item remains dormant because RocketIDE does not yet expose project-limit/telemetry settings in the UI.

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

- [x] Map LSP severities and stable `Rdddd` codes without message-text parsing.
- [x] Key live diagnostics by document URI + server publication generation/current document version policy so stale results are not rendered over newer text.
- [x] Render error/warning/info squiggles without mutating document text.
- [x] Hovering a diagnostic decoration shows code + message + source.
- [x] Problems panel filters by severity and text, groups optionally by file, and displays file/line/column.
- [x] Double-click/Enter opens file and selects/navigates to range.
- [x] Closing a document honors Rocket's empty-diagnostics clear and removes stale display.
- [x] Show LSP disconnected/degraded state distinctly from "zero problems".
- [x] Test UTF-16 ranges involving surrogate pairs so line/column mapping does not drift.
- [x] Run verification and CI.

**Completion note (2026-09-11):** live diagnostics were Windows-smoke-tested against the real Rocket workspace and standalone files, including unsaved edits, stale clearing, Problems navigation, squiggles, and distinct offline state. Explorer/Problems selected-state contrast regressions discovered during smoke testing were carried into WP08 theme hardening.

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

- [x] Implement completion request at caret with cancellation/debounce suitable for typing.
- [x] Render label/detail/documentation/kind and selected insertion text/textEdit correctly.
- [x] Apply additional text edits such as automatic import only after validating edits against open workspace/document bounds.
- [x] Implement Markdown hover rendering using a safe WPF presentation subset; do not execute HTML/scripts.
- [x] Make `rocket-doc://` links either resolve through a safe documentation handler or remain visibly copyable; never hand arbitrary schemes to the shell without validation.
- [x] Implement signature help with active signature/parameter highlighting and Rocket named/default parameter metadata as provided by server.
- [x] Implement semantic tokens full response decoding using server legend.
- [x] Implement semantic token delta updates with fallback to full when cache/result IDs are invalid.
- [x] Ensure semantic tokens overlay/augment basic syntax rather than leaving the file colorless when LSP disconnects.
- [x] Cancel stale completion/hover/signature requests when caret/document changes.
- [x] Run verification and CI.

**Completion note (2026-09-11):** Windows verification passed with 164/164 tests and publish smoke. Real standalone-file smoke verified completion, hover, semantic coloring/fallback, readable dark completion and Problems selection UI, and stable LSP transport under rapid edits. Signature-help triggering/request/presentation is implemented and regression-tested, including auto-pair trigger forwarding; the current Rocket LSP does not reliably return a signature for an incomplete/temporarily invalid call such as `print(|)`, so that upstream limitation is tracked as `LSP-SIG-01` rather than reimplementing Rocket semantics in the IDE. Full-workspace latency is tracked separately as `LSP-PERF-01`.

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

- [x] Implement F12/go-to-definition and open target file/range.
- [x] Implement find references in a reusable references/search-results panel.
- [x] Implement prepare-rename before showing rename commit UI.
- [x] Request rename and validate every returned edit: <=1,024 total edits; writable workspace/open files only; valid normalized paths; valid document versions/ranges where available.
- [x] Apply multi-file edits transactionally enough that a failure does not leave half the workspace silently modified. Keep original snapshots for rollback/reporting.
- [x] Implement code-action lightbulb/context menu. Label actions as server-provided.
- [x] Confirm `R4002` quick fixes and formatting actions flow through LSP rather than IDE heuristics.
- [x] Implement Format Document via LSP for open supported files.
- [x] Implement Format Target/Workspace through `rocketc fmt` later in tool-command service where appropriate; do not conflate it with document formatting.
- [x] Test conflicting/invalid/out-of-workspace edit rejection.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

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

- [x] Port/test Windows command-line quoting behavior from Rocket Visual Studio reference.
- [x] Build a Windows Job Object runner that assigns child process before it can meaningfully escape; ensure Stop/Dispose terminates the tree.
- [x] Keep tool window hidden and streams redirected.
- [x] Implement line-stream callbacks without deadlocking stdout/stderr.
- [x] Parse only lines with schema `rocket-message-1` as structured compiler events; preserve unknown/plain output text in Output.
- [x] Add parser tests for diagnostic, build-finished, test-started, test-finished, test-summary, malformed JSON, unknown schema, missing optional fields.
- [x] Add command builder tests for package target and standalone file target.
- [x] Wire Check, Build, Test with `--message-format=json` in the correct argument position accepted by active Rocket.
- [x] Convert compiler diagnostic events to Problems entries with source provenance and one-based span conversion.
- [x] Implement Run with streamed stdout/stderr and program arguments setting.
- [x] Implement Stop to cancel compiler or application process tree.
- [x] Disable Run for library output where manifest/tool metadata proves it is not executable.
- [x] Prevent accidental concurrent Build/Run/Test conflict through explicit command state; do not solve by ignoring clicks.
- [x] Tests panel displays per-test PASS/FAIL and summary from structured test events.
- [x] Build output displays artifact and cache hit when reported.
- [x] Run verification and CI with fake-process/unit coverage.
- [ ] Run the deferred real Rocket Check/Build/Run/Test/Stop smoke against a configured SDK.

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

- [x] Implement cancellable async file enumeration with default exclusions `.git`, `.rocketc`, `bin`, `obj`, `.vs`.
- [x] Search supported text files as streams/individual buffers; cap individual preview lines and total in-memory UI result population.
- [x] Stream results incrementally to UI in batches to avoid Dispatcher work per character/match.
- [x] Support case sensitive, whole word, plain text; regex may be added only with timeout/bounds and tests.
- [x] Clicking result opens and selects match.
- [x] Implement replace-in-files preview with explicit file/match list before applying destructive bulk edits.
- [x] Open dirty buffers participate using in-memory text so search does not disagree with visible edits.
- [x] Persist recent project list and remove nonexistent entries gracefully.
- [x] Add keyboard shortcuts for open, quick file navigation, search, build, run, stop, problems/output toggle.
- [ ] Run stress fixture with thousands of small files and verify cancellation keeps UI responsive.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

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

- [x] Unit-test the exact 4 MiB threshold in UTF-8 bytes: below allowed, exactly boundary per Rocket contract interpretation verified against server, above denied. Use byte count, not `string.Length`.
- [x] Decide basic syntax-coloring cutoff based on measured AvalonEdit performance; document it separately from the 4 MiB LSP contract.
- [x] Add visible Large File Mode banner with exact reason and available/disabled features.
- [x] Prevent LSP `didOpen`, semantic, completion, diagnostics requests for that document.
- [x] Ensure local find/goto/save remains available.
- [x] Avoid eager `File.ReadAllText` for files whose size demands streaming/guard checks; perform metadata/size validation first.
- [x] Debounce/coalesce rapid document sync while preserving incremental version order.
- [x] Audit all background continuations for accidental UI-thread blocking.
- [x] Add cancellation to workspace scan/search and compiler/LSP operations not already covered.
- [x] Use `rocket/analysisStatus` to show analysis activity/latency unobtrusively, not as modal UI.
- [x] Test project-status response and surface if project exceeds server bounds.
- [ ] Profile memory with generated large fixture and fix retained closed documents/event subscriptions.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

**Acceptance:** opening >4 MiB Rocket text does not crash/freeze LSP; editor stays usable with explicit degraded semantics.

**Commit:** `perf: harden RocketIDE for large sources`

---

# IDE-WP13 — Advanced Rocket tooling

**Outcome:** RocketIDE exposes useful Rocket-specific package and measurement commands after the core editor is stable.

**Files:**
- Extend: `src/RocketIDE.Rocket/Compiler/RocketCommandService.cs`
- Create views/dialogs only where a command benefits from structured presentation.

### Tasks

- [x] Add New Rocket Project using `rocketc new`; validate destination and never overwrite an existing nonempty directory without explicit confirmation.
- [x] Add Resolve Dependencies: normal, `--locked`, optional offline mode exposed clearly.
- [x] Add Dependency Tree output with copy support.
- [x] Add Audit Dependencies with diagnostics/errors clearly separated from harmless output.
- [x] Add Target Information via `rocketc target [--verbose]` after verifying active version flags.
- [x] Add Format Target/Workspace with `rocketc fmt`; support check-only only where UI meaning is clear.
- [x] Add Coverage command with output path under a user-selected or `.rocketc`/IDE temp location; parse `rocket-coverage-1` only if documented/needed for UI, otherwise open/reveal generated report.
- [x] Add Profile and Benchmark analogously; never run these automatically on file open or save.
- [x] Commands that may execute native code require explicit user action and show active target.
- [x] Add tests for command argument construction and output-path quoting.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

- [ ] Run the deferred configured-SDK advanced-command GUI smoke.

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

- [x] Persist last workspace, open document paths, active tab, panel visibility/sizes, window bounds in per-user app data.
- [x] Clamp restored window coordinates to current display bounds so changed monitor layouts do not open off-screen.
- [x] Implement periodic crash-recovery snapshots only for dirty buffers, stored outside repo.
- [x] Recovery snapshot includes original path, saved-file fingerprint/timestamp, buffer version, and text; never overwrites source automatically.
- [x] On startup after unclean exit, offer Restore/Discard per recovery set; if disk changed too, make conflict explicit.
- [x] Clear recovery state after successful save/discard/clean shutdown.
- [x] Add rotating application logs without source text by default; secrets/environment credentials must not be logged.
- [x] LSP crash: show disconnected status, clear/mark stale semantic state, allow safe restart, do not lose editor buffers.
- [x] Compiler process crash/nonzero exit: keep output, release command busy state, permit rerun.
- [x] Add global unhandled-exception logging/recovery boundary without swallowing fatal corrupted-state cases.
- [x] Test recovery serialization and stale/conflict decisions with temp files.
- [ ] Run forced-kill/manual recovery smoke on Windows.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

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

- [x] Publish self-contained `win-x64` Release build.
- [x] Validate publishing model; WP17 requires a reliable self-contained multi-file folder because DbgX launches bundled `EngHost.exe`. `RocketIDE.exe` remains the entry point.
- [x] Create portable ZIP with deterministic top-level folder.
- [x] Default portable packaging excludes `.pdb` files while `-KeepSymbols` preserves them when symbols are desired.
- [x] Add `--version` or About version tied to assembly informational version/commit.
- [x] Keep the portable package in external-SDK mode; bundled Rocket SDK packaging remains intentionally unavailable until a verified distributable Rocket package is supplied.
- [x] Validate external SDK mode remains supported in the portable release.
- [ ] CI smoke launches or at minimum starts process and verifies clean startup on Windows runner where desktop-interactive limits allow; otherwise use a non-UI startup verification hook designed for CI.
- [x] Generate SHA-256 checksums for release archive.
- [x] Document portable install/update/uninstall behavior.
- [x] Defer an installer intentionally; the portable ZIP remains the supported distribution until manual clean-machine acceptance is complete.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

**Acceptance:** CI produces a clean downloadable Windows x64 artifact and checksum from a tagged/release build path.

**Commit:** `build: package RocketIDE for Windows x64`

---

# IDE-WP16 — UX polish, accessibility, final 1.0 hardening

**Outcome:** Core behavior is consistent, keyboard-friendly, DPI-safe, and visually coherent without sacrificing editor speed.

### Tasks

- [x] Audit menu/toolbar command-state logic; the final code audit fixes debugger breakpoint availability on non-Rocket tabs. Runtime interaction remains in manual acceptance.
- [x] Centralize visible production shortcuts through the existing command registry; runtime keyboard interaction remains in manual acceptance.
- [x] Add/reuse Quick Open through the existing command registry; no second command system.
- [x] Defer light theme rather than duplicate hard-coded resources; dark theme remains the supported 1.0 theme.
- [ ] Test 100%, 125%, 150%, 200% DPI.
- [ ] Test minimum supported window size and 1366x768.
- [x] Add tested lexical matching-bracket highlighting; final visual behavior remains part of the deferred GUI smoke.
- [x] Add accessibility names/tooltips/focus metadata in code; final screen-reader/focus-order behavior remains part of the deferred GUI smoke.
- [x] Verify at code/UI-structure level that diagnostic severity includes explicit Severity text in addition to color; final visual/accessibility smoke remains manual.
- [x] Audit implemented empty/status/error-state wiring for Explorer, Problems, Output, Tests, LSP offline, and missing compiler; final runtime presentation remains manual.
- [x] Remove dead placeholders/debug scaffolding found by the final code audit; keep only real debugger/UI paths.
- [ ] Run full manual acceptance matrix against real Rocket package: create -> edit -> error -> fix -> format -> build -> run -> stop -> test -> rename -> reopen/recover.
- [x] Run `scripts/verify.ps1`, Release publish, and CI for the merged implementation baseline; the final audit patch has its own fresh verification gate in `docs/FINAL_AUDIT.md`.
- [ ] Update README with screenshots only after behavior is accepted; screenshots are documentation, not evidence.

**Acceptance:** 1.0 workflow is coherent on a clean Windows machine and all critical features have clear degraded states.

**Commit:** `release: harden RocketIDE 1.0 experience`

---

# IDE-WP17 — Standalone debugger feasibility and optional implementation

**Outcome:** Decide based on evidence whether RocketIDE can provide a maintainable native debugger without embedding Visual Studio. This WP is not allowed to block a good 1.0 editor/build environment.

### Phase A — Feasibility (mandatory before implementation)

- [x] Read current Rocket PDB/source-map contract and existing Visual Studio debugger integration snapshot.
- [x] Determine available Windows debugger backend choices that can legally/technically be redistributed and support CodeView PDB stepping, breakpoints, threads, call stacks, and locals; Microsoft DbgX/DbgEng is selected.
- [x] Use an isolated DbgX/DbgEng backend project instead of writing a native debugging engine from scratch.
- [ ] Run the standalone DbgX backend against a tiny real Rocket debug build and prove breakpoint binding, continue/pause, stepping, call stack, threads, locals, output, and stop. Existing Rocket Visual Studio integration proves the artifact contract; this standalone live smoke is intentionally deferred to the final Codex/manual pass.
- [x] Document limitations including duplicate source basenames in the frozen current Rocket source-map contract.
- [x] Feasibility is sufficient with DbgX/DbgEng; retire the standalone-debugger upstream request and do not ship fake debugger data.

### Phase B — Implementation (only if Phase A passes)

- [x] Create isolated `RocketIDE.Debugger` project so debugger complexity does not infect Core/LSP/compiler services.
- [x] Implement debug build command and artifact validation (`.exe`, `.pdb`, `.rocket.map.json`).
- [x] Implement breakpoints, launch/stop, continue/pause, step over/in/out.
- [x] Add call stack, locals, threads panels populated only from real backend data.
- [x] Route debugger/target output through the Rocket Output surface where DbgX provides it.
- [x] Automated protocol/session/source-map/command/UI tests passed on Windows as part of the 348/348 merged-main verification. The manual real Rocket debug acceptance remains separately deferred below.
- [x] Run fresh Windows `scripts/verify.ps1`, portable package checks, then CI.

**Acceptance:** either a real verified debugger exists, or the WP ends explicitly deferred with a documented upstream requirement. There is no cosmetic debugger mode.

**Commit if implemented:** `feat: add Rocket native debugging`

---

# Final 1.0 Acceptance Matrix

**Automated implementation baseline:** merged `main` at `bf30f98` passed `scripts/verify.ps1` with 348/348 tests, self-contained win-x64 publish, and debugger asset guards; the corresponding `windows-ci` run also passed and produced both verification and portable-package artifacts. A final audit patch may increase the test count, so its own Windows verify/CI evidence supersedes this baseline once merged.

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
- [ ] Debug build launches through Rocket's compiler-owned `.exe` + `.pdb` + `.rocket.map.json` contract.
- [ ] Source breakpoint binds and stops on the expected Rocket line.
- [ ] Continue, pause, step over, step into, step out, and stop work against a real Rocket target.
- [ ] Debug Threads, Call Stack, and Locals show real backend data and source navigation works.
- [ ] Self-contained Windows x64 release artifact launches with the bundled DbgX `EngHost.exe`.
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
