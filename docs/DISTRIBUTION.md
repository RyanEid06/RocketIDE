# RocketIDE Windows distribution

The released application is a self-contained **multi-file** `win-x64` publish. A recipient does not need to install the .NET runtime, Visual Studio, or WinDbg. A compatible Rocket SDK must still be installed/configured separately through **Tools > Rocket SDK Settings**.

The native debugger requires a multi-file application directory: Microsoft DbgX hosts the native debugger engine in the bundled architecture-specific `EngHost.exe` child process. Do not republish RocketIDE as a single self-extracting executable unless that debugger-host contract is revalidated.

## Published version

[Download RocketIDE-Setup-1.0.0.exe](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-Setup-1.0.0.exe) for a per-user Windows installation. The separately labeled [Portable ZIP](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip) remains available and unchanged (SHA-256: `a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c`). Installer SHA-256 is in [RocketIDE-Setup-1.0.0.exe.sha256](release-1.0.0/RocketIDE-Setup-1.0.0.exe.sha256); size, URL, and frozen payload identity are recorded in [installer-1.0.0.json](release-1.0.0/installer-1.0.0.json). Linux and macOS packages on the website are for the separate Rocket language SDK.

The frozen source tag is rocketide-v1.0.0; the corresponding consumer tag is rocketide-v1.0.0-consumer. `main` continues to hold developer maintenance, including `installer/RocketIDE.iss` and `scripts/build-installer.ps1`. The installer wraps the already-published consumer payload; it does not rebuild RocketIDE. Building current `main` creates a new installer and does not reproduce the frozen portable ZIP byte-for-byte.

## Build the Windows installer from the frozen consumer archive

Install Inno Setup 7.1.0 (or a compatible Inno Setup compiler), then run:

```powershell
.\scripts\build-installer.ps1 `
  -PayloadArchive C:\path\to\RocketIDE-win-x64-1.0.0.zip `
  -OutputDirectory C:\path\to\installer-output `
  -CompilerPath 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
```

The build helper refuses any archive whose SHA-256 differs from the frozen `a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c` identity. It extracts that archive to a temporary directory, checks the app and debugger entry points, then compiles the standard wizard. The wizard follows the Windows system light/dark setting, defaults to `%LOCALAPPDATA%\Programs\RocketIDE`, creates a Start menu shortcut, offers an optional desktop shortcut, offers to launch on Finish, and registers a normal per-user uninstaller. It does not install or download the Rocket SDK.

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
