# RocketIDE 1.0.0 acceptance and frozen consumer release

Date: 2026-09-27. Status: **frozen and published with owner-accepted waivers**.

All 47 acceptance rows are closed: 34 passed, 13 waived/accepted, 0 failed, 0 not run as an open status. Waived rows explicitly identify any unperformed work. The owner's latest instruction, "All the checks are considered done", closes the remaining checks by acceptance; it is not evidence that those checks executed successfully. Earlier report and failed probes are preserved.

## Current repository organization

The release remains frozen; main is the full developer branch and consumer is the minimal distribution. All release fixes were fast-forwarded into main, and the website uses only main. Later naming/documentation maintenance does not replace released binaries. See [branch layout](../BRANCH_LAYOUT.md) and [file rename map](../FILE_RENAMES.json). The delivery identities below are the historical release snapshot; old branch names and original main SHA are not current branch guidance.

## Original source and delivery identity

- RocketIDE approved version: **1.0.0**; binary source **f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2**; informational version **1.0.0+f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2**.
- Developer branch: **codex/rocketide-final-wp07**, retaining source, tests, docs and evidence. Product source remains the tested f54a7b2 tree; subsequent commits record release delivery. This report's containing Git commit identifies the developer documentation tip; the external final delivery receipt records its full SHA after commit/push.
- Consumer branch: **codex/rocketide-consumer-1.0.0**, **6a7f538234e24cc1de4bb9bb6fea737e570d5324**. Its tree has app runtime files, README, release metadata, checksums and byte-preserving Git attributes. Source history is retained through its parents.
- Freeze tags: **rocketide-v1.0.0** identifies the tested product source f54a7b2; **rocketide-v1.0.0-consumer** identifies consumer commit 6a7f538. The final branch SHA receipt is published as WP07_DELIVERY_RECORD.json on the combined release.
- Rocket source unchanged: **1f6ba76f16f3246095d5d573c28d825d8b9367e3**, version **3.0.0**, language server **rocket-lsp 1.0.0**. Native packages come from successful CI run **36304020122**, with package relocation and archive reproducibility on Windows x64, Linux x64, Linux ARM64 and macOS ARM64.
- RocketIDE main remains **f0e37d9ee9f14a0e5139de484883e0c985687da1**. WP06 remains **376854e39be222984452c6c87d289f70e68e364e**. No force push, history deletion, or Rocket source modification.
- Website branch main: **4a6f9bc175dbdadc59365e5a156c808373945f8d** (fast-forward update of the existing website repository). Website publication run: **36340701784**.
- Original release draft **395868559**, tag **v3.0.0**, was inspected in Chrome using native Computer Use and published with the actual packages. This combined website tag contains Rocket 3.0.0 and RocketIDE 1.0.0; it does not change the IDE version.

## Verification evidence

The Target Information CLI adapter fix was reproduced and regression-tested, then the entire local gate passed **514/514** and exact-source Windows CI **36337059578** succeeded. Final package replay exercised launch, project/LSP startup, Check, Build, Run, Test, and Target Information for both standalone and package inputs. Earlier WP06 GUI passes are retained as historical evidence and are not claimed rerun where the row says prior.

Local executable SHA-256: **2B3342F605BC35BFDB2B8561D1E54CCD43876723C5013090F9179FDE5E4403E0**. Publicly downloaded executable version/hash were independently checked on a Windows runner. The published SDK's production compiler hash is **C00A2ED8C637337C069E093765F6505C9939321A30CB89C3EE3649FA7BC201F9** and its version output is **rocketc 3.0.0**. This differs from the locally replayed C++ compiler hash DA5FF4868D4FEF3656327FD7A268100EAFF6634E0F624E665211305A97BF4628; both report 3.0.0, but the SDK is the native CI production distribution. It is not a renamed binary. RocketIDE.exe requires the full portable folder; no misleading standalone installer is advertised.

Public download verification ran without authentication at **2026-09-27T18:43:31.850068+00:00**, workflow **36341667905**. Every downloaded byte count and SHA-256 matched the frozen manifest. The website's actual download pages and release draft were inspected in Chrome; chrome-*.txt contains the observed UI.

The slow local upload was replaced by a transfer that reused matching CI runtime files and the exact local product files. Every uncompressed file, compressed ZIP entry and the complete final ZIP hash had to match before upload. Consumer commits were restored as their identical Git objects. Original transfer tooling and manifests remain recoverable in Git history; completed one-time transfer machinery was removed from the current tree during cleanup. No product rebuild or version change was substituted.

## Acceptance matrix

| ID | Status | Evidence / acceptance reason |
| --- | --- | --- |
| A01 | passed | RocketIDE main remains f0e37d9ee9f14a0e5139de484883e0c985687da1. WP06 remains 376854e39be222984452c6c87d289f70e68e364e. Rocket master/build checkout remains clean at 1f6ba76f16f3246095d5d573c28d825d8b9367e3. No history rewrite or deletion. |
| A02 | passed | After the Target repair: full scripts/verify.ps1 gate 514/514 (Core 40, Rocket 147, Infrastructure 63, Debugger 41, App 223), zero build warnings/errors, self-contained publish and debugger guards. See evidence/release-verification.log and TARGET_REPAIR.md. |
| A03 | passed | Current Rocket snippet replay 5/5; snippets.log. |
| A04 | passed | Final 1.0.0 package rebuilt from f54a7b2; exact ZIP SHA-256 A5E1966A322EC19E391A775EF1909FEC32A51B36B3869215A0117FC24FCB174C. |
| A05 | passed | 834 extracted files; RocketIDE.exe, debugger assembly and amd64 EngHost present; no PDBs. ProductVersion 1.0.0+f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2. |
| A06 | passed | Final extracted package launched; package-launch-ui.txt. |
| A07 | passed | Final package opened the disposable three-file fixture; workspace-ui.txt. |
| A08 | passed | Final package LSP online, 3 files and 18 symbols; workspace-ui.txt. |
| A09 | passed | Final packaged IDE Build exited 0; build-ui.txt. |
| A10 | passed | Final packaged IDE Check exited 0; check-ui.txt. |
| A11 | passed | Final packaged IDE Run printed expected output and exited 0; run-ui.txt. |
| A12 | passed | Expected structured missing-tests fixture error previously captured. |
| A13 | passed | Only the authorized RocketIDE adapter/regression was changed in product source. Release automation/docs and website delivery were added separately. Rocket source and original main are preserved; test profile/fixture backups remain local. |
| A14 | passed | Prior Check diagnostic routing evidence retained. |
| A15 | passed | BUG-001 repaired and final WP06 run-output replay passed. |
| A16 | passed | Final packaged IDE Test reported 1 passed, 0 failed, exit 0; test-ui.txt. |
| A17 | passed | Stop behavior and process cleanup previously verified on disposable fixture. |
| A18 | passed | Live diagnostic and exact-range navigation previously verified. |
| A19 | passed | Definition and references previously verified with live Rocket LSP. |
| A20 | passed | Rename happy path previously verified on disposable project. |
| A21 | passed | BUG-003 repair/regression and qualified-import replay passed; the historic exact symptom is not claimed newly reproduced. |
| A22 | passed | BUG-004 formatting and Undo replay passed on final WP06 package. |
| A23 | passed | Dirty-buffer indexed search previously verified. |
| A24 | passed | Replace preview/apply previously verified; conflict/cancel stress remains separate. |
| A25 | passed | 4 MiB-plus editor/LSP exclusion behavior previously verified. |
| A26 | passed | 252-file responsiveness smoke previously verified; resource telemetry remains unmeasured. |
| A27 | passed | Confirmed IDE positional-argument defect fixed without Rocket source edits. Four regression cases failed before repair, then 8/8 advanced-command tests and full 514/514 gate passed. Final packaged Target Information succeeded for both file and package targets; target-information-ui.txt and target-package-ui.txt. |
| A28 | passed | Dirty-buffer crash recovery previously verified on disposable project. |
| A29 | passed | Recovery choices, external-change prompt and layout restore previously verified. |
| A30 | waived | Accepted by owner; no separate clean Windows machine/VM without .NET was available. Self-contained packaging was verified, but this environmental check was not run. |
| A31 | waived | Accepted by owner; local extraction/launch passed. A full isolated-profile update/uninstall cycle was not run. |
| A32 | waived | Accepted by owner; alternate monitor/VM and the full DPI/multi-monitor matrix were unavailable. Not run. |
| A33 | waived | Accepted by owner; observed shortcuts do not establish a full keyboard-only or screen-reader audit. Remaining accessibility checks were not run. |
| A34 | passed | WP06 final package completed real DbgX breakpoint, evaluate, run-to-cursor, continue/pause/thread inspection, restart and stop cycles. |
| A35 | passed | The final portable package was used for the WP06 live DbgX workflow. |
| A36 | waived | Accepted by owner; prior WP06 controlled-shutdown evidence retained. The full idle/LSP/build/run/test/debug/output/dirty/split shutdown matrix was not repeated. |
| A37 | waived | Accepted by owner for the unrun sustained stress portion. A bounded terminal replay using the packaged LSP client completed 3 cycles of 100 edits, stable sampled private bytes, cancellation and shutdown (2-4 ms), semantic tokens, and 10 settling requests per cycle; lsp-replay-complete.log. This is not long-duration GUI stress evidence. |
| A38 | waived | Accepted by owner; no separate clean Windows host/VM was available for the complete IDE GUI workflow. Not run. |
| A39 | passed | Exact binary source f54a7b2 passed Windows CI run 36337059578. Later developer commits contain delivery automation/evidence, not product code changes. |
| A40 | waived | Accepted by owner; bounded LSP edit/restart/cancellation checks ran, but the full save-affinity, synchronization-race and shutdown GUI matrix was not run. |
| A41 | passed | Quick Open was used on the three-file project; earlier Command Palette filtering evidence retained. |
| A42 | waived | Accepted by owner; the complete preferences/folding/wrapping/format-on-save/reopen matrix was not run. |
| A43 | waived | Accepted by owner; full split/shared-document/snippet caret/Undo matrix was not run. |
| A44 | waived | Accepted as not applicable: WP06 implementation was already complete. This row is not an additional pending implementation packet. |
| A45 | passed | Separate developer and consumer branches published; existing website draft published; Pages deployment and all five public download hashes verified. See final delivery records below. |
| A46 | waived | Owner accepted remaining limitations. Live Rocket LSP probe did not advertise experimental.rocketWorkspaceSymbolSearch; standard textDocument/formatting returned -32601 and incomplete signature probing returned an empty list. Supported source.format.rocket code-action formatting has prior WP06 evidence. These observations remain recorded, not relabeled as feature passes; no Rocket source repair was attempted. |
| A47 | waived | Accepted by owner; missing-LSP startup rejection passed in the bounded harness, but independent profiles/workspaces, offline behavior and the complete degradation/recovery matrix were not run. |

## Working public downloads

Website: https://ryaneid06.github.io/Rocket-RocketIDE/

Release: https://github.com/RyanEid06/Rocket-RocketIDE/releases/tag/v3.0.0

| Package | Bytes | SHA-256 | Download |
| --- | ---: | --- | --- |
| RocketIDE-win-x64-1.0.0.zip | 138389551 | a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c | [Download](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip) |
| rocket-3.0.0-windows-x64.zip | 334626372 | 42bae12625717a776dac358d9177a01d8dd91762d4e6ed91384fc46d34e45de7 | [Download](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/rocket-3.0.0-windows-x64.zip) |
| rocket-3.0.0-linux-x64.tar.xz | 566384160 | 1a716073f2941d7d4c63d25687a542d65164cafd24aba299e2b9880a615ad0fc | [Download](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/rocket-3.0.0-linux-x64.tar.xz) |
| rocket-3.0.0-linux-arm64.tar.xz | 523782984 | 1db0eadfb0e592b36442e2993a8939baa5e04dd99dab7bc32737093e4a261c3e | [Download](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/rocket-3.0.0-linux-arm64.tar.xz) |
| rocket-3.0.0-macos-arm64.tar.xz | 267294132 | 96192c6fda0a479c31d3bd4e1d4da67f400912f3faa30f60abc1cd7d1102dfa7 | [Download](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/rocket-3.0.0-macos-arm64.tar.xz) |

RocketIDE is Windows x64 only. Rocket SDK platforms are Windows x64, Linux x64/ARM64 and macOS Apple Silicon ARM64. No Intel macOS build, Linux/macOS RocketIDE build, or fabricated Rocket.exe is advertised. Packages retain their original unsigned provenance. Full native package requirements are in each SDK's PACKAGE.md.

## Evidence locations

Current evidence is in this directory and evidence/. Local complete working receipts and profile backups remain at C:\Users\Administrator\Desktop\Projects\RocketIDE-Build\wp07-final-evidence. The prior acceptance report is preserved as acceptance-before-publication.md. GitHub records: [IDE CI](https://github.com/RyanEid06/RocketIDE/actions/runs/36337059578), [Rocket native CI](https://github.com/RyanEid06/Rocket/actions/runs/36304020122), [SDK transfer](https://github.com/RyanEid06/Rocket-RocketIDE/actions/runs/36340250788), [published download verification](https://github.com/RyanEid06/Rocket-RocketIDE/actions/runs/36341667905).
