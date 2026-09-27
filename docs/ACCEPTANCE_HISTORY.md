# Acceptance history and closure

Updated 2026-09-27. The initial code-only delivery deferred native GUI, SDK, debugger, clean-machine and accessibility checks. The completed [release acceptance report](release-1.0.0/ACCEPTANCE_REPORT.md) now supersedes that pending list.

The 47-row release matrix has **34 passed and 13 waived/accepted**, with no failed or open not-run status. Each waiver identifies the unperformed or environmental work and the owner's acceptance. The full automated gate passed **514/514**, with zero failures or skips, and packaged launch and public download verification are recorded separately.

Earlier snapshots at bf30f98 (348 tests), the editor integration baseline (473 tests), the initial repair gate (487 tests), and debugger validation (510 tests) remain dated evidence; they are not the current test total. Historical test counts and raw captures have not been rewritten.

For the exact disposition of navigation/refactoring, build/run/test, search, large files, advanced tooling, recovery, clean-machine distribution, accessibility and debugger scenarios, use the rows in the release matrix. A waiver is not proof that a scenario ran. Revisit it if a future change affects that area or a suitable environment becomes available.
