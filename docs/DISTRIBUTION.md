# RocketIDE Windows distribution

The portable release is a self-contained `win-x64` publish. A recipient does not need to install the .NET runtime, but must configure a compatible Rocket SDK unless a separately verified SDK bundle is supplied.

## Build a package

From the repository root:

```powershell
.\scripts\package.ps1 -Version 1.0.0 -OutputRoot artifacts\package-win-x64
```

The script creates a deterministic top-level folder named `RocketIDE-win-x64-<version>`, publishes the `RocketIDE.exe` entry point, omits PDB files by default, writes a portable README, creates a ZIP, and writes a sibling SHA-256 file. Use `-KeepSymbols` when a diagnostic symbol copy is wanted.

The script intentionally uses `--no-restore`; CI restores the solution and the `win-x64` runtime assets before packaging. A local run should restore those assets first with `dotnet restore .\src\RocketIDE.App\RocketIDE.App.csproj -r win-x64`.

## SDK modes

RocketIDE stores SDK settings per user. The package does not copy a random development checkout into the release. External SDK mode remains the default and is configured from Tools > Rocket SDK Settings. Bundled SDK mode should only be added after a versioned, redistributable Rocket package has been verified separately.

## Install, update, and remove

Extract the ZIP to a user-writable directory and launch `RocketIDE.exe`. Updating means extracting a new version beside or over the old application folder after the application is closed. User settings, logs, and recovery snapshots live under `%LOCALAPPDATA%\RocketIDE`, so removing the application folder does not remove those files; remove that per-user directory separately only when its recovery/log history is no longer needed.
