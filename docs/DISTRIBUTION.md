# RocketIDE Windows distribution

The portable release is a self-contained **multi-file** `win-x64` publish. A recipient does not need to install the .NET runtime, Visual Studio, or WinDbg. A compatible Rocket SDK must still be configured unless a separately verified Rocket SDK bundle is supplied.

The native debugger requires a multi-file application directory: Microsoft DbgX hosts the native debugger engine in the bundled architecture-specific `EngHost.exe` child process. Do not republish RocketIDE as a single self-extracting executable unless that debugger-host contract is revalidated.

## Published version

[Download RocketIDE 1.0.0](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip) for Windows x64. SHA-256: a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c. The package is portable and unsigned; no installer wizard is included. Linux and macOS packages on the website are for the separate Rocket language SDK.

The frozen source tag is rocketide-v1.0.0; the corresponding consumer tag is rocketide-v1.0.0-consumer. main continues to hold developer maintenance. Building current main creates a new build and does not reproduce the frozen package byte-for-byte. Do not overwrite the published release as part of source/documentation maintenance.

## Build a package

From the repository root:

```powershell
.\scripts\package.ps1 -Version 1.0.0 -OutputRoot artifacts\package-win-x64
```

The script creates `RocketIDE-win-x64-<version>`, publishes the self-contained application, verifies `RocketIDE.exe`, `RocketIDE.Debugger.dll`, and an x64 `EngHost.exe`, omits application PDB files by default, writes a portable README, creates a ZIP, and writes a sibling SHA-256 file. Use `-KeepSymbols` when a diagnostic symbol copy is wanted.

The script intentionally uses `--no-restore`; CI first runs `scripts\verify.ps1`, which restores both the solution and `win-x64` runtime assets. A local packaging run should restore those assets first with:

```powershell
dotnet restore .\src\RocketIDE.App\RocketIDE.App.csproj -r win-x64
```

## SDK and debugger contents

RocketIDE stores Rocket SDK settings per user. The package does not copy a random development checkout into the release. External SDK mode remains the default and is configured from **Tools > Rocket SDK Settings**.

The Microsoft DbgX/DbgEng native debugger engine **is** part of the RocketIDE portable package because the native debugger requires it at runtime. Keep the extracted application directory together; moving only `RocketIDE.exe` will break debugging. The Rocket SDK is still separate.

## Install, update, and remove

Extract the complete ZIP to a user-writable directory and launch `RocketIDE.exe`. Updating means replacing/extracting the complete application folder while RocketIDE is closed. User settings, logs, and recovery snapshots live under `%LOCALAPPDATA%\RocketIDE`, so removing the application folder does not remove those files; remove that per-user directory separately only when its recovery/log history is no longer needed.
