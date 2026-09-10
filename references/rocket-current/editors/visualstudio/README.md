# Rocket for Visual Studio Community 2026

`Rocket.Language.VisualStudio` 2.0 upgrades the repository's original TextMate
VSIX without changing its extension identity. It supports Visual Studio
Community 2026 on Windows x64 and provides:

- Rocket Build, Run, Test, Stop, Debug, environment-validation, and options
  commands under **Extensions > Rocket**; the five development commands also
  appear on the standard toolbar while a `.rocket` file or `rocket.toml` is
  active;
- nearest-ancestor `rocket.toml` discovery and standalone `.rocket` files;
- hidden compiler, language-server, environment-activation, and application
  processes with redirected streams and process-tree cancellation;
- a dedicated **Rocket** Output pane and `rocket-message-1` compiler
  diagnostics in the Error List with clickable source locations;
- a Visual Studio LSP client for the repository's `rocket-lsp.exe`; and
- native Visual Studio debugging of Rocket executables through their adjacent
  CodeView PDB and `rocket-source-map-1` files.

The tracked CMake targets and scripts remain supported fallback automation.
No generated state or machine-specific path is stored in the VSIX sources.

The client consumes the same `rocket-lsp` metadata for the accepted Rocket 3
Wave B named/default argument and labeled-enum surface, so signature help,
formatting, and diagnostics remain consistent with VS Code. Wave C is not a
separate editor contract; current documentation is indexed by
`docs/ROCKET_3_0_SYNTAX_DICTIONARY.md` and `docs/DOCUMENTATION_STATUS.md`.

## Install or upgrade

These steps are repository-relative. Do not replace them with a checked-in
username, drive letter, or clone directory.

1. Install Visual Studio Community 2026 with **Desktop development with C++**
   and CMake support.
2. Bootstrap and verify Rocket's pinned MSVC, Ninja, and LLVM environment:

   ```powershell
   .\dependencies\bootstrap.ps1
   powershell.exe -NoProfile -ExecutionPolicy Bypass `
     -File .\dependencies\verify.ps1
   ```

3. Build the extension. Its first build may restore the pinned .NET reference
   assemblies and Visual Studio SDK build tools into ignored `out/` state:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass `
     -File .\scripts\package-visualstudio-extension.ps1
   ```

4. Close Visual Studio, open
   `out/visualstudio/Rocket.Language.VisualStudio.vsix`, and install it for
   Visual Studio Community 2026. The installer upgrades version 1.x in place
   because the extension identity is unchanged.
5. Launch the repository through the supported wrapper:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass `
     -File .\scripts\open-visualstudio.ps1
   ```

For a normal installed Rocket compiler, Visual Studio can instead open any
folder or source file directly. Configure a non-discoverable compiler or
language server under **Tools > Options > Rocket > General**.

## Build, run, test, stop, and debug

Open a Rocket source document or its `rocket.toml`, then use the standard
toolbar buttons, the **Build**/**Debug** menus, or **Extensions > Rocket**:

- **Build Rocket Project** runs `rocketc build`.
- **Run Rocket Project** runs `rocketc run`, with optional program arguments
  from Rocket options.
- **Test Rocket Project** runs `rocketc test` and reports the structured test
  summary in the Rocket pane.
- **Stop Rocket** cancels the compiler/application process tree or stops the
  active native-debug session.
- **Debug Rocket Project** performs an unoptimized `--debug` build and launches
  the resulting executable with Visual Studio's native Windows debugger.

For every command, the extension walks upward from the active document to the
nearest `rocket.toml`. If none exists, the active `.rocket` file is compiled as
a standalone program. Run and Debug are disabled for library package outputs.

The extension discovers `rocketc.exe` and `rocket-lsp.exe` from, in order, the
configured option, the corresponding environment variable, a sibling tool,
the active Rocket repository's Debug/Release build, and `PATH`. It never writes
a discovered absolute path into source-controlled files.

**Validate Rocket Environment** prints both tool locations and versions in the
Rocket pane. With **Load pinned repository environment** enabled, the extension
runs `dependencies/activate.ps1` in a hidden PowerShell process, captures its
process environment, and passes that environment directly to Rocket tools.
PowerShell, Windows Terminal, `conhost`, and external debug-console windows are
not part of ordinary Build, Run, Test, LSP, or Debug execution.

## Output and diagnostics

Select **View > Output** and choose **Rocket** to see compiler messages, test
events, program output, tool validation, and LSP stderr logs. All ordinary Run
output stays in this pane. Native Debug starts the executable suspended with
`CREATE_NO_WINDOW`, redirects its standard output and error to the Rocket pane,
attaches Visual Studio's native debugger by process ID, resumes execution, and
automatically continues past Windows' attach-only `ntdll` breakpoint. It never
asks Visual Studio to create a console or terminal, and it does not
auto-continue a Rocket source breakpoint.

Build/Test compiler lines using schema `rocket-message-1` are decoded instead
of scraped as text. Diagnostics appear under provider **Rocket** in the Error
List with stable `Rdddd` codes, severity, absolute document path, and one-based
source location. Double-click an entry to open and select that location.
`rocket-lsp` independently publishes live diagnostics while a document is
being edited.

## Language-server features

Opening a `.rocket` document activates one hidden `rocket-lsp.exe` client
process for Visual Studio's Rocket content type. Visual Studio uses the
server's LSP 3.17 capabilities for completion, hover and signatures,
definition, references, prepare-rename/rename, document and workspace symbols,
semantic tokens, code actions, whole-document formatting, incremental document
updates, and live diagnostics. See `docs/LANGUAGE_SERVER.md` for the bounded
server contract.

If semantic features do not start, run **Extensions > Rocket > Validate Rocket Environment**,
check **Tools > Options > Rocket > General > Language server path**, and inspect
`[LSP]` lines in the Rocket Output pane. Syntax coloring remains provided by
the packaged TextMate grammar even when the LSP executable is unavailable.

## Native debugging

The Debug command requires an executable package and validates that the build
produced matching `.exe`, `.pdb`, and `.rocket.map.json` files. It resolves
every source recorded by the sidecar, rejects missing source files or ambiguous
duplicate basenames, prints each `rocket:\source\<file>` mapping in the Rocket
pane, starts the debuggee hidden and suspended with redirected streams, attaches
the Visual Studio native-only engine by process ID, and then resumes it.

Set a breakpoint on an executable Rocket line before choosing **Debug Rocket
Project**. Visual Studio consumes Rocket's CodeView functions, line records,
locals, and native call frames. Stepping, call stacks, and locals are available
where those records describe them. Debug builds are unoptimized; optimized CLI
builds can still fold or omit locals. The frozen Rocket 2.0 CodeView contract
stores source basenames, so two compiled source files with the same basename
cannot be mapped unambiguously and are rejected before launch.

Run and Debug intentionally do not provide interactive console input: Run has
no console, and the hidden Debug process receives `NUL` as standard input while
stdout/stderr are captured. Use Rocket options for program
arguments or application-level file/GUI input when interaction is required.

## Fallback automation

`scripts/open-visualstudio.ps1` still refreshes the tracked
`editors/visualstudio/launch.vs.json` into ignored `.vs` state. CMake Targets
View still exposes `rocket_demo_check`, `rocket_demo_run`, and
`rocket_demo_test` for `examples/visualstudio_demo`. The VSIX build is also
available as the `rocket_visualstudio_extension` CMake target.

These paths are useful for reproducibility and extension troubleshooting; they
are not required for day-to-day Rocket commands after the VSIX is installed.

## Validation and troubleshooting

Run the focused extension suite and portable-package inspection with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\test-visualstudio-extension.ps1
```

The suite covers manifest/package discovery, standalone files, structured
diagnostics and test summaries, source-map parsing, Windows argument rules,
hidden redirected process output, VSIX identity/assets, and checkout-path
leaks.

If commands are absent, confirm version 2.0.3 under **Extensions > Manage
Extensions**, restart Visual Studio, and activate a `.rocket` file or
`rocket.toml`. If a command cannot find a tool, use explicit Rocket options or
put the packaged tools on `PATH`. If an Error List entry does not navigate,
confirm the diagnostic's file still exists relative to the active package. If
a breakpoint stays unbound, rebuild with **Debug Rocket Project**, confirm the
PDB and sidecar lines in the Rocket pane, and verify that no second source file
has the same basename.

Keep `.vs`, `out`, `.rocketc`, `dependencies/installed`, downloaded SDK
packages, Visual Studio experimental-instance state, and extension install
directories out of Git.
