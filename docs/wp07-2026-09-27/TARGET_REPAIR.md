# FINAL-WP07 Target Information repair

Rocket 3 rejects a positional input on `rocketc target`. The adapter now omits that argument for Target while preserving package/file arguments for other advanced commands. The regression exercises package and standalone targets, verbosity on/off, and an explicit `--target windows-x64` selection.

Verification on 2026-09-27: four regression cases failed before the fix; all eight advanced command cases passed after it. Full scripts/verify.ps1 passed 514/514 (Core 40, Rocket 147, Infrastructure 63, Debugger 41, App 223), zero build warnings/errors, plus self-contained publish and debugger guards. Logs are in evidence/.

Scope: RocketIDE source only, based on WP06 376854e39be222984452c6c87d289f70e68e364e. Main and Rocket source remain untouched. Approved product version remains 1.0.0. Package replay, remaining acceptance, separate consumer branch and public download verification are still pending; this commit does not declare a frozen release.
