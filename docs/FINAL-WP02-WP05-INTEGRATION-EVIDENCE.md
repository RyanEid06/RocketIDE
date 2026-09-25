# FINAL-WP02–WP05 RocketIDE integration evidence

**Status: integration candidate only; production merge blocked.** This is the current evidence for the dedicated RocketIDE integration branch. The WP02 source-branch evidence is historical and is not acceptance evidence for this combined tree.

## Scope and preflight

- Repository: `C:\Users\Administrator\Desktop\Projects\RocketIDE`; `origin` is `https://github.com/RyanEid06/RocketIDE.git`. Live `origin/HEAD` identifies `main` as the production branch. The separate Rocket repository and Rocket FINAL-WP01A/B were excluded.
- The read-only preflight found all four registered worktrees clean, including the root WP03 checkout, the in-repo WP02 checkout, and the external WP03 and WP05 checkouts. The existing stash was preserved. All six relevant local branches matched their tracking and live remote SHAs with ahead/behind `0/0` at preflight.
- Preflight SHAs: `main` `062b78fe2d12675b86fd44ccf3c0e24e98b81d01`; `codex/rocketide-final-wp02-wp07` `8013d109632687687ca99612cdde881421aa192c`; `codex/rocketide-final-wp345-seam` `63c462a22f63e84e3ececde4a447f704b48c639f`; WP03 `23fbb86c0f948fef45bdc7fda2f45c1c6fc187ef`; WP04 `e4c1f1dcecccf0d223ee7468e3a0563a706e190f`; WP05 `6561264e5acc820a8560f65fca2d36b889be3a5f`.
- Ancestry showed the `wp02-wp07` branch contains one WP02-only lifecycle commit after `main`; no WP06/WP07 implementation was included. The seam descends from it. WP03 and WP04 each add one commit from that seam; WP05 adds two commits.

## Baselines and merge ancestry

| Source | SHA | `scripts/verify.ps1` baseline |
| --- | --- | --- |
| WP02 (`wp02-wp07`) | `8013d10` | Release build 0 warnings/errors; 370/370 tests; publish assets present |
| WP03 | `23fbb86` | First full run: build passed, but the hung fake-LSP shutdown test failed once (`ForcedKillCount` 0 versus 1). The isolated test passed 8 reruns; a second unchanged full run passed 390/390, 0 warnings/errors, publish assets present. The original failure remains part of baseline evidence. |
| WP04 | `e4c1f1` | Release build 0 warnings/errors; 417/417 tests; publish assets present |
| WP05 | `6561264` | Release build 0 warnings/errors; 395/395 tests; publish assets present |

The dedicated branch `codex/rocketide-final-wp02-wp05-integration` started at `main` and preserved published source history. Merge commits: `438fee0` seam/WP02, `2fa93aa` WP03, `728301b` WP04, and `744d8fd` WP05. The WP04 merge reconciled `MainWindow.xaml`, retaining WP03 command routes and WP04 editor controls. The WP05 merge reconciled `MainWindow.Debugger.cs`, `MainWindow.RocketIntegration.cs`, `MainWindow.xaml`, and `MainWindow.xaml.cs`, preserving WP03 navigation/command routing, WP04 Quick Open, and WP05 split editing/snippets. The auto-merged `MainWindowViewModel.cs` was reviewed. No source branch or worktree was rewritten or deleted.

## Confirmed integration fixes

- WP04's folding controller was unreachable from the production editor host. Each WP05 editor view now wires the typed LSP folding provider into its own AvalonEdit folding controller and manager. Per-view collapsed state is captured for recovery; only exact coordinates from current authoritative ranges are restored. The provider now allows simultaneous same-version split-view requests, rejects stale version/session responses, and observes application shutdown.
- The WP02 force-stop path marked a client as stopped before its synchronous process-tree kill completed. A deterministic concurrent test failed before the fix and passed after serializing completion; the focused shutdown tests passed 3/3.
- The WP03 workspace-symbol parser previously silently truncated responses at 1,024 entries. It now reports an incomplete-response error at that bound, and the picker displays the failure. File and workspace symbol navigation now checks document/workspace and LSP-session freshness after selection. These safeguards do **not** prove complete fuzzy search when the server filters by substring or truncates below the client bound.

## Integrated automated acceptance

- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1` on the current source: .NET SDK 10.0.401; Release build succeeded with **0 warnings and 0 errors**; Core 40/40, Rocket 141/141, Debugger 22/22, Infrastructure 63/63, App 194/194, **460/460 total**. Self-contained win-x64 publish passed with nonempty `RocketIDE.exe` and `amd64/EngHost.exe`.
- The first sandboxed restore failed with NuGet `NU1301`/TLS authentication errors. The unchanged gate passed with normal NuGet access. This was an environment restore failure, not a test failure.
- `git diff --check` passed on the source changes.
- `scripts/package.ps1 -OutputRoot artifacts/integration-wp02-wp05-package -Version integration-wp02-wp05` passed. ZIP: `artifacts/integration-wp02-wp05-package/RocketIDE-win-x64-integration-wp02-wp05.zip`; 834 entries; SHA-256 `b94766790facd118acd252229bfd8fc5fe453b1181675cfa2cb1271283e5e3b8`, independently matched against the `.sha256` file. The ZIP contains `RocketIDE.exe` (287,744 bytes), `RocketIDE.Debugger.dll` (78,848 bytes), `amd64/EngHost.exe` (42,808 bytes), and `RocketIDE.runtimeconfig.json`. Both executables have PE machine `0x8664` (x64).

## Outstanding acceptance and blockers

- **Authoritative fuzzy workspace-symbol completeness: FAIL / unresolved.** The IDE sends the raw query to `workspace/symbol` and can only rank symbols returned by the server. `PlayerController` ranks for `plctrl` when supplied, but a server that first applies substring filtering or a lower undocumented result limit can omit it. A bounded, complete selection contract from Rocket's authoritative symbol index is required; no IDE-side parser or source scan was added. This is a known high-severity WP03 acceptance gap and blocks the production merge.
- **Rocket toolchain smoke: BLOCKED.** The current Rocket compiler and `rocket-lsp` were unavailable to this RocketIDE-only run; RocketIDE's observed status was `Rocket SDK: not found` and `LSP: offline`. The existing snippet smoke script probes the separate Rocket checkout, so it was not run under the repository boundary. The five bundled snippets and Rocket LSP protocol smoke remain unverified against the authoritative current binaries.
- **GUI manual smoke: mostly MANUAL SMOKE DEFERRED.** The built app launched and showed the editor and folding margin. It automatically restored a previous out-of-scope workspace, so the window was closed without using that workspace. Workspace/file open, Quick Open, symbol navigation/fuzzy query, Outline, history, Command Palette, productivity actions, word wrap, Format-on-Save, folding behavior, split editing and independent presentation, dirty-view close, snippets, semantic features, debugger markers, recovery, and active-work shutdown were not observed in the RocketIDE scope. Startup visibility is not evidence for those flows.
- **Performance and lifetime matrix: INCOMPLETE.** Quick Open has a 10,000-file scan cap and 250-result cap in production, with cancellation in discovery and ranking, but cold/warm timing on a realistically large RocketIDE workspace was not measured. Workspace-symbol latency/memory and active-work five-second shutdown were not measured with the real LSP/debugger/compiler. The deterministic fake-LSP shutdown race is fixed; that narrower test does not replace the full matrix.

No production-branch merge or push has occurred. The candidate is **not yet a verified production baseline** for the next work package. Reconcile with current `main`, rerun the full gate, and merge/push only after the authoritative symbol contract and blocked acceptance are resolved within the authorized repository boundary.
