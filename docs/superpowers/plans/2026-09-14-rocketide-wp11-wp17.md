# RocketIDE WP11-WP17 Implementation Plan

> **HISTORICAL / SUPERSEDED:** This was the pre-implementation WP11-WP17 plan. Its WP17 feasibility/defer assumptions were superseded by `2026-09-14-wp17-native-debugger.md`: a redistributable Microsoft DbgX/DbgEng backend was implemented and automated-verified. Use `ROADMAP.md` and `docs/WP11-WP17-CODE-ONLY-FOLLOWUPS.md` for current status/evidence.


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the code-owned portions of IDE-WP11 through IDE-WP17 in dependency order while leaving interactive smoke evidence explicitly deferred.

**Architecture:** Keep RocketIDE.Core free of WPF, process, and filesystem APIs; put Rocket-specific command contracts in RocketIDE.Rocket; put Windows/filesystem/process/recovery/package mechanics in RocketIDE.Infrastructure; and compose the UI in RocketIDE.App. Search, large-file decisions, recovery decisions, command construction, packaging metadata, and debugger feasibility remain deterministic and unit-testable without launching WPF or Rocket tools.

**Tech Stack:** C# 14, .NET 10, WPF, AvalonEdit, MSTest, System.Text.Json, Windows Job Objects, PowerShell packaging scripts.

**Spec:** `ROADMAP.md` sections `IDE-WP11` through `IDE-WP17`, constrained by `docs/ROCKET_IDE_DESIGN_SPEC.md`, `docs/ROCKET_INTEGRATION_CONTRACT.md`, and `CONTRIBUTING.md`.

## Global Constraints

- Windows x64 is the required desktop platform; platform-specific implementations must degrade clearly outside Windows.
- RocketIDE remains a native WPF application and never embeds an IDE-local Rocket parser or semantic engine.
- Search is asynchronous, cancellable, streaming, bounded, and excludes `.git`, `.rocketc`, `bin`, `obj`, `out`, and `.vs` by default.
- Rocket LSP receives no document above the exact 4 MiB UTF-8 policy boundary; local editing, find, goto, and save remain available.
- Compiler/tool commands are explicit, cancellable, use structured output where supported, and never execute on workspace open.
- Recovery data and logs live outside the repository and never overwrite source files automatically.
- Smoke tests, real Rocket integration tests, DPI/manual acceptance, forced-kill recovery checks, and debugger backend proof remain recorded as deferred evidence.
- Use focused tests and a build after every phase; do not change roadmap status to DONE without the missing smoke/CI evidence.

### Task 1: IDE-WP11 search, replace, recent projects, and productivity

**Files:**
- Create: `src/RocketIDE.Core/Search/SearchQuery.cs`, `SearchMatch.cs`, `SearchResultBatch.cs`, `ReplacePreview.cs`, `IWorkspaceSearchService.cs`
- Create: `src/RocketIDE.Infrastructure/Files/WorkspaceSearchService.cs`
- Create: `src/RocketIDE.App/Views/SearchPanel.xaml`, `src/RocketIDE.App/Views/SearchPanel.xaml.cs`, `src/RocketIDE.App/ViewModels/SearchViewModel.cs`
- Modify: `src/RocketIDE.App/MainWindow.xaml`, `src/RocketIDE.App/MainWindow.xaml.cs`, `src/RocketIDE.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/RocketIDE.Core.Tests/Search/*`, `tests/RocketIDE.Infrastructure.Tests/Files/WorkspaceSearchServiceTests.cs`, `tests/RocketIDE.App.Tests/SearchViewModelTests.cs`

**Interfaces:**
- `SearchQuery` carries pattern, case sensitivity, whole-word mode, regex mode, root path, and excluded directory names.
- `IWorkspaceSearchService.SearchAsync(SearchQuery, IProgress<SearchResultBatch>, CancellationToken)` streams bounded result batches.
- `IWorkspaceSearchService.CreateReplacePreviewAsync(SearchQuery, string, CancellationToken)` returns file/match previews without writing.
- Applying replacements requires an explicit preview object and revalidates file fingerprints before atomic writes.

- [ ] Write tests for excluded directories, text-file filtering, line/column reporting, cancellation, bounded batches, and stale replace previews.
- [ ] Run the new Core/Infrastructure tests and confirm the missing service behavior fails for the expected reason.
- [ ] Implement the streaming service with `Directory.EnumerateFiles`, per-file async reads, line caps, total result caps, and cancellation checks.
- [ ] Add replace preview and fingerprint validation without mutating files during search or preview.
- [ ] Implement the Search view model and panel with incremental result collection and explicit Preview/Apply controls.
- [ ] Connect result activation to existing document opening/navigation and add productivity shortcuts for search, quick file navigation, build, run, stop, Problems, and Output.
- [ ] Run focused tests and a phase build.

### Task 2: IDE-WP12 large-file mode and performance hardening

**Files:**
- Create: `src/RocketIDE.Core/Documents/LargeFilePolicy.cs`, `src/RocketIDE.Core/Documents/LargeFileDecision.cs`
- Modify: `src/RocketIDE.Infrastructure/Files/FileDocumentStore.cs`, `src/RocketIDE.App/ViewModels/DocumentTabViewModel.cs`, `src/RocketIDE.App/MainWindow.xaml`, `src/RocketIDE.App/MainWindow.RocketIntegration.cs`, `src/RocketIDE.Rocket/LanguageServer/DocumentSynchronizer.cs`
- Test: `tests/RocketIDE.Core.Tests/Documents/LargeFilePolicyTests.cs`, `tests/RocketIDE.Infrastructure.Tests/Files/FileDocumentStoreTests.cs`, `tests/RocketIDE.Rocket.Tests/LanguageServer/DocumentSynchronizerTests.cs`, `tests/RocketIDE.App.Tests/LargeFileModeTests.cs`

**Interfaces:**
- `LargeFilePolicy.Decide(long utf8ByteLength)` returns a stable decision with threshold, mode, reason, and feature flags.
- Large-file tabs expose `IsLargeFileMode`, `LargeFileReason`, and disabled semantic feature state.

- [ ] Write threshold tests using UTF-8 bytes for below, exact-boundary, and above-boundary input plus malformed/binary metadata cases.
- [ ] Implement the policy and metadata-first load guard without converting oversized files into an in-memory LSP document.
- [ ] Add banner/state presentation and keep local find/goto/save paths enabled.
- [ ] Gate LSP open/change/semantic/completion/diagnostic requests for large-file tabs and preserve version ordering for supported documents.
- [ ] Add bounded debounce/coalescing for rapid document synchronization and audit background continuations for UI-thread blocking.
- [ ] Surface `rocket/analysisStatus` and project-status limits through existing status/output paths.
- [ ] Run focused tests and a phase build.

### Task 3: IDE-WP13 advanced Rocket tooling

**Files:**
- Modify: `src/RocketIDE.Rocket/Compiler/RocketCommandBuilder.cs`, `RocketCommandService.cs`, `src/RocketIDE.App/MainWindow.RocketCommands.cs`, `src/RocketIDE.App/MainWindow.xaml`
- Create: `src/RocketIDE.Rocket/Compiler/RocketAdvancedCommand.cs`, `RocketCommandCatalog.cs`
- Create: `tests/RocketIDE.Rocket.Tests/Compiler/RocketAdvancedCommandTests.cs`

**Interfaces:**
- `RocketAdvancedCommand` describes command kind, arguments, output policy, native-execution warning, and optional output path.
- `RocketCommandCatalog.Build(kind, target, options)` constructs `new`, `resolve`, `tree`, `audit`, `target`, `fmt`, `coverage`, `profile`, and `benchmark` requests.

- [ ] Write command-construction tests for quoting, package/standalone target selection, `--locked`, offline mode, verbose target info, check-only formatting, and safe output paths.
- [ ] Implement the catalog and extend the command service with explicit cancellation and output routing.
- [ ] Add safe New Project validation that rejects nonempty destinations unless the UI explicitly confirms replacement semantics.
- [ ] Add menu actions and status/output presentation for dependency and measurement commands; never invoke them on open/save.
- [ ] Run focused tests and a phase build.

### Task 4: IDE-WP14 reliability, session restore, crash recovery, and logging

**Files:**
- Create: `src/RocketIDE.Infrastructure/Recovery/*`, `src/RocketIDE.Infrastructure/Logging/*`
- Create: `src/RocketIDE.Core/Recovery/*`
- Modify: `src/RocketIDE.Infrastructure/Settings/*`, `src/RocketIDE.App/App.xaml.cs`, `src/RocketIDE.App/MainWindow.xaml.cs`, `src/RocketIDE.App/MainWindow.xaml`, `src/RocketIDE.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/RocketIDE.Core.Tests/Recovery/*`, `tests/RocketIDE.Infrastructure.Tests/Recovery/*`, `tests/RocketIDE.Infrastructure.Tests/Logging/*`, `tests/RocketIDE.App.Tests/RecoveryViewModelTests.cs`

**Interfaces:**
- Recovery records include original path, saved-file fingerprint/timestamp, buffer version, text, and conflict status.
- `ISessionStore` persists workspace, open paths, active path, panel layout, and window bounds.
- `IRecoveryStore` writes/loads/clears dirty-buffer snapshots outside the repo.
- `IApplicationLogger` supports levels and rotation while redacting source text, secrets, and environment credentials.

- [ ] Write serialization, monitor-bound clamping, fingerprint conflict, restore/discard, and cleanup tests first.
- [ ] Implement atomic per-user session/recovery stores and rotating logs.
- [ ] Add periodic dirty-buffer snapshots and clean-shutdown clearing without source overwrite.
- [ ] Add startup restore/discard choice state and explicit disk-conflict presentation.
- [ ] Connect LSP/process failure paths to disconnected/retryable UI state and ensure command busy state is released.
- [ ] Add global exception logging that rethrows fatal corrupted-state failures.
- [ ] Run focused tests and a phase build; leave forced-kill/manual recovery smoke deferred.

### Task 5: IDE-WP15 Windows distribution

**Files:**
- Create: `scripts/package.ps1`, `docs/DISTRIBUTION.md`, `tests/RocketIDE.Infrastructure.Tests/Distribution/PackageScriptTests.cs`
- Modify: `.github/workflows/windows-ci.yml`, `src/RocketIDE.App/App.xaml.cs`, `Directory.Build.props`

**Interfaces:**
- The package script accepts configuration/output/version parameters, publishes self-contained `win-x64`, creates a deterministic top-level folder, excludes PDBs from the end-user ZIP, and writes SHA-256 checksums.
- `--version` and About display the assembly informational version/commit supplied by build properties.

- [ ] Write script/data tests for deterministic folder naming, checksum generation, path safety, and optional symbol handling.
- [ ] Implement the PowerShell packaging flow using `dotnet publish` and `Compress-Archive` with explicit paths.
- [ ] Add CI packaging/checksum steps without requiring a real Rocket SDK.
- [ ] Document portable install/update/uninstall and external-versus-bundled SDK modes.
- [ ] Run script validation and a phase build; leave Windows launch smoke deferred.

### Task 6: IDE-WP16 UX polish and accessibility hardening

**Files:**
- Modify: `src/RocketIDE.App/MainWindow.xaml`, `src/RocketIDE.App/Themes/DarkTheme.xaml`, `src/RocketIDE.App/ViewModels/MainWindowViewModel.cs`, `src/RocketIDE.App/Editor/*`
- Create: `src/RocketIDE.App/Commands/RocketCommandRegistry.cs`, `src/RocketIDE.App/Accessibility/AccessibilityText.cs`
- Test: `tests/RocketIDE.App.Tests/CommandRegistryTests.cs`, `tests/RocketIDE.App.Tests/AccessibilityTests.cs`, `tests/RocketIDE.App.Tests/Editor/BracketHighlightingTests.cs`

**Interfaces:**
- One command registry owns command IDs, gestures, can-execute state, and busy-state behavior for menu/toolbar/palette surfaces.
- Accessibility metadata is centralized and icon-only controls expose names/tooltips.

- [ ] Write command-state, gesture, empty/loading/error-state, accessible-name, and purely lexical bracket-matching tests.
- [ ] Implement the shared registry and route existing menu/toolbar enablement through it.
- [ ] Add command palette/quick-open only through the registry, preserving editor text-key behavior.
- [ ] Centralize theme resources and add a light theme only where resources can be reused without hard-coded duplication.
- [ ] Add matching-bracket rendering only through a bounded lexical background renderer.
- [ ] Remove dead placeholder controls and add explicit degraded states for Explorer, Problems, Output, Tests, LSP offline, and missing compiler.
- [ ] Run focused tests and a phase build; leave DPI/manual acceptance deferred.

### Task 7: IDE-WP17 debugger feasibility and safe deferral

**Files:**
- Create: `src/RocketIDE.Core/Debugger/DebuggerFeasibility.cs`, `src/RocketIDE.Rocket/Debugger/RocketDebugArtifactValidator.cs`, `docs/DEBUGGER_FEASIBILITY.md`
- Create: `tests/RocketIDE.Core.Tests/Debugger/DebuggerFeasibilityTests.cs`, `tests/RocketIDE.Rocket.Tests/Debugger/RocketDebugArtifactValidatorTests.cs`
- Modify: `ROADMAP.md` only for an evidence-backed `ROCKET-UPSTREAM-REQUEST:` entry if the feasibility result requires it.

**Interfaces:**
- `DebuggerFeasibility` records backend name, capabilities, redistribution status, evidence status, limitations, and recommendation.
- `RocketDebugArtifactValidator` validates `.exe`, `.pdb`, and `.rocket.map.json` relationships without launching code.

- [ ] Write tests for capability aggregation, missing-artifact diagnostics, duplicate source-basename limitation reporting, and deferred recommendation.
- [ ] Implement artifact validation and a report based on the current Rocket PDB/source-map and Visual Studio snapshots.
- [ ] Record the evidence-backed feasibility result and upstream DAP/debug-adapter request if no redistributable backend can be proven.
- [ ] Do not create a fake debugger UI or execute native debug targets automatically.
- [ ] Run focused tests and a phase build; leave real debugger proof deferred for a later usage-limit-enabled pass.

## Execution checkpoints

- Preserve all pre-existing dirty files and the existing WP10 implementation.
- After each phase: run its focused tests where restore permits, run `dotnet build RocketIDE.sln -c Debug --no-restore` or the narrowest equivalent, and record the exact result.
- At the end: run `git diff --check`, inspect changed-file scope, run the strongest available build/test command, and write a follow-up note for network-blocked restore, manual smoke, CI, and any skipped real-tool evidence.
