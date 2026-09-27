# Repository branch layout

Updated 2026-09-27.

- **main:** full developer source, automated tests, roadmaps, design documentation and release evidence. Includes all verified fixes delivered for RocketIDE 1.0.0. Later maintenance changes improve naming and documentation.
- **consumer:** the minimal frozen 1.0.0 application directory, README, release metadata, checksums and Git attributes. It intentionally differs from main; ahead/behind counts do not indicate missing bug fixes. Do not merge this distribution tree into the developer tree.

The website repository, Rocket-RocketIDE, has only main. Temporary release branches are merged and retired. Git history, existing local worktrees and release tags are retained.

Frozen tags are rocketide-v1.0.0 (product source f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2) and rocketide-v1.0.0-consumer (original consumer commit 6a7f538234e24cc1de4bb9bb6fea737e570d5324). Published version: 1.0.0. [ZIP](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip) SHA-256: a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c.

The published package remains frozen at its tested source identity; source-only naming cleanup on main does not rebuild or replace the release. A future runtime change requires its own verification and release decision.

Current release status is in [the acceptance report](release-1.0.0/ACCEPTANCE_REPORT.md). Dated records describe their original snapshots; [FILE_RENAMES.json](FILE_RENAMES.json) resolves old evidence paths. Completed one-time transfer scripts, duplicate package inputs and archived upload workflows were removed from the current tree; their exact versions remain in history at a194aaf170263195f44540d7a339568ce442caaf. They are not the release procedure for future versions.
