# Repository branch layout

Updated 2026-09-27 at the owner's request.

- `main`: full developer source, tests, docs, and verified fixes from FINAL-WP06 and FINAL-WP07. Main incorporates the WP07 delivery tip `a194aaf170263195f44540d7a339568ce442caaf` without rewriting history.
- `consumer`: minimal user distribution. Renamed from `codex/rocketide-consumer-1.0.0`; the original consumer tip is `6a7f538234e24cc1de4bb9bb6fea737e570d5324`. Its app files remain unchanged; current navigation points developers to main.

The merged remote WP06/WP07 branch references are retired. Existing local worktrees and all commits remain preserved. The separate Rocket-RocketIDE website repository uses only main. The Rocket compiler repository is outside this consolidation.

Frozen tags remain `rocketide-v1.0.0` (product source `f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2`) and `rocketide-v1.0.0-consumer` (original consumer commit `6a7f538234e24cc1de4bb9bb6fea737e570d5324`). Published version: 1.0.0. ZIP SHA-256: `a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c`.

The acceptance report and delivery receipt describe the historical release state; this document supersedes their branch-layout statements only. Their passed/waived evidence is unchanged. The completed one-time transfer workflow is archived under docs/wp07-2026-09-27/archived-workflows so it cannot recreate the retired consumer branch. Its scripts and manifests are retained as historical evidence, not as the current release procedure.