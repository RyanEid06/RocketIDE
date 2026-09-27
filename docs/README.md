# Developer documentation

RocketIDE **1.0.0** is published for Windows x64. The release acceptance matrix is closed with **34 passed and 13 explicitly waived/accepted** checks. Waivers do not claim execution. All 514 automated tests passed in the release gate and the subsequent main consolidation gate.

## Current guidance

- [Repository README](../README.md): product and developer entry point.
- [Roadmap](../ROADMAP.md): current release status and historical foundation milestones.
- [Release roadmap](RELEASE_ROADMAP.md): completed integration scope and future-work boundary.
- [Distribution](DISTRIBUTION.md): download, setup, packaging, updates and removal.
- [Branch layout](BRANCH_LAYOUT.md): developer main and minimal consumer roles.
- [Architecture](ROCKET_IDE_DESIGN_SPEC.md), [Rocket integration contract](ROCKET_INTEGRATION_CONTRACT.md), [debugger architecture](DEBUGGER_ARCHITECTURE.md), and [editor syntax](EDITOR_SYNTAX_BASELINE.md).

## Verification and historical records

- [Release acceptance matrix](release-1.0.0/ACCEPTANCE_REPORT.md) and [Target Information repair](release-1.0.0/TARGET_REPAIR.md).
- [Debugger validation](debugger-validation-2026-09-27/DEBUGGER_VALIDATION_REPORT.md).
- [Editor integration validation](EDITOR_INTEGRATION_VALIDATION.md) and [lifecycle validation](LIFECYCLE_VALIDATION.md).
- [Acceptance history](ACCEPTANCE_HISTORY.md) and [earlier implementation audit](FINAL_AUDIT.md).
- Historical plans are under implementation-plans; design proposals are under designs. They describe the work at their dates, not instructions to restart completed work.

Raw captures, fixtures and original evidence manifests are preserved byte-for-byte. Their numbered internal identifiers, recorded paths and historical counts remain evidence, not current status. [FILE_RENAMES.json](FILE_RENAMES.json) maps historical paths to descriptive filenames. Manifests still describe the original captured versions of reports; edited narrative reports are not claimed to retain their original hashes. Frozen Git tags preserve the complete original release snapshots.
