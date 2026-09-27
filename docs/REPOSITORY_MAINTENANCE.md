# Repository maintenance record

Date: 2026-09-27. Scope: RocketIDE main/consumer and the Rocket-RocketIDE website main branch. Rocket repository excluded.

## File cleanup

Developer main retains the source, tests, engineering rules, build/packaging scripts, roadmaps, design references and raw evidence needed for future changes. Completed consumer-transfer scripts, reconstruction manifests, duplicate executable inputs and the archived transfer workflow were removed from the current tree. They remain in Git history; no history or release tag was rewritten.

The consumer branch retains 834 required application/runtime files plus README.md, RELEASE.json, SHA256SUMS.txt and .gitattributes. Runtime dependencies and debugger files were not guessed to be unnecessary. The frozen app tree and checksums are unchanged; only user instructions were clarified.

The website retains its source, active assets, build configuration, lockfile, download manifest and Pages deployment workflow. Unused AI/server template files and packages, unused image copies and completed release workflows were removed. No UI redesign was introduced.

## Descriptive naming

Numbered source/test/script filenames now describe navigation, editor productivity, save pipelines, language-server capabilities and bundled snippets. Developer docs use lifecycle, editor integration, debugger validation and release-1.0.0 names. Raw evidence file contents and original manifests remain byte-identical; [FILE_RENAMES.json](FILE_RENAMES.json) maps historical names to their current locations. Work-package IDs within dated specifications/captures remain historical identifiers.

## Documentation

The README, roadmap entry points, branch guidance, distribution instructions, debugger architecture, integration contract and acceptance-history overview describe the published release. Dated evidence is clearly marked as historical, preserving original counts and outcomes. Current release acceptance remains 34 passed and 13 explicitly waived/accepted.

The final commit SHAs and fresh validation results are recorded in the external repository cleanup receipt after commit/push. This avoids embedding an impossible self-referential commit SHA in the commit itself. The published 1.0.0 binaries, hashes and release URLs remain unchanged.

## Local validation

The completed cleanup passed the full Windows gate: 514 tests, zero failures/skips, zero build warnings/errors, self-contained publish and debugger asset guards. Website clean dependency installation, TypeScript checking and production build passed. All 834 consumer app files matched their SHA-256 manifest; all 407 raw evidence/fixture/manifest Git blobs remained identical after moves. Current documentation links resolve. A separate read-only review found no important issues.
