# FINAL-WP02 evidence — Lifecycle, Synchronization, and Shutdown

## Scope and baseline

- Dedicated worktree: `.worktrees/rocketide-final-wp02-wp07`, branch `codex/rocketide-final-wp02-wp07`.
- Live `main` and `origin/main` were both `062b78fe2d12675b86fd44ccf3c0e24e98b81d01` before work. The frozen roadmap base `d73b5c3` is its parent; the intervening commit only added this roadmap.
- Baseline `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1` passed before changes: Release build with zero warnings/errors, 350 tests, self-contained win-x64 publish, and nonempty `RocketIDE.exe`, `RocketIDE.Debugger.dll`, and `amd64/EngHost.exe`.
- Rocket FINAL-WP01 integration remains blocked by the existing `phase20_compatibility` expectation at `tests/portable_workflow_test.py:130`: it expects `rocketc 2.1.0`, while the compiler reports `rocketc 3.0.0`. Rocket WP01B and `rocket35/eddie` were not modified.

## Implementation and automated verification

- One application shutdown stopwatch supplies the normal two-second and hard five-second budgets. Cancellation stops new work; debugger, LSP, process, session/recovery, and output cleanup consume the same deadline. Dirty-tab save work begins after user choices and is included in that deadline.
- Debugger synchronization-context joining and transport disposal are bounded; forced teardown detaches safely and terminates known debuggee and owned EngHost processes. LSP startup, graceful shutdown, process exit, restart, and disposal have bounds and forced process-tree termination. Compiler/run/test processes continue using the existing Windows Job Object ownership model.
- Per-document scheduling serializes pending and in-flight changes with disk save and matching `didSave`. Disk persistence continues if LSP synchronization fails. A concurrent edit preserves the exact snapshot written to disk for `didSave`.
- UI output is queued in bounded dispatcher batches, keeps bounded history with chunk trimming, and orders command headers and Clear with queued lines.
- Focused tests: 13/13 app scheduler, lifetime, and output tests passed. Affected Release suites: Core 38/38, Debugger 22/22, Rocket 133/133, Infrastructure 60/60, App 117/117 (370/370 total).
- Final `scripts/verify.ps1`: passed, zero build warnings/errors, the same 370/370 tests, and self-contained win-x64 publish with nonempty `RocketIDE.exe` and `amd64/EngHost.exe`.
- `git diff --check` passed. `dotnet format whitespace RocketIDE.sln --verify-no-changes --no-restore --include ...` passed for every WP02 changed source/test file. A solution-wide `dotnet format --verify-no-changes` reports pre-existing whitespace findings in `ProblemsViewModelTests.cs` and `WorkspaceEditTransactionServiceTests.cs`, plus WPF generated-name errors in `EditorDocumentHost.xaml.cs` despite the canonical build passing; those files were not changed for WP02.

## Manual smoke

**MANUAL SMOKE DEFERRED — external Computer Use bridge blocker.** The initial `cua.getState()` returned `apps: []`; the attempted native-window query returned `cua.listWindows is not a function`. No native RocketIDE window could be bound or driven. The manual normal, hung-LSP/DbgX, and active-process shutdown matrix is therefore unverified. Automated fake-hang and disposable child-process tests provide only the narrower evidence described above; they are not GUI acceptance.
