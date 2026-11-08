# RocketIDE

RocketIDE is a Windows desktop IDE for the [Rocket programming language](https://github.com/RyanEid06/Rocket). It provides a focused editor and native development tools for Rocket projects.

## Download RocketIDE 1.0.0

The official Windows x64 installer and portable package are available in the [latest Rocket release](https://github.com/RyanEid06/Rocket-RocketIDE/releases/tag/v3.0.0):

- [Download the Windows installer](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-Setup-1.0.0.exe)
- [Download the portable ZIP](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip)

The installer provides a per-user installation and normal Windows uninstall support. For the portable version, extract the ZIP and run `RocketIDE.exe`. Both packages are self-contained and do not require a separate .NET runtime installation.

## Features

- Multi-file Rocket editing with completion, diagnostics, syntax highlighting, and formatting
- Navigation, symbol search, references, rename, code actions, and project exploration
- Build, run, test, and formatting workflows using Rocket's compiler and language server
- Native debugging, split editing, search, and session recovery

RocketIDE is a native **Windows x64** application built with C# 14, .NET 10, WPF, and AvalonEdit. Rocket compiler and language-server behavior comes from `rocketc` and `rocket-lsp`.

## Install the Rocket SDK

RocketIDE does not include the Rocket SDK. Download an official package from the [Rocket 3.0.0 release](https://github.com/RyanEid06/Rocket-RocketIDE/releases/tag/v3.0.0), then configure it in **Tools > Rocket SDK Settings**.

## Build from source

On Windows with the .NET 10 SDK installed, build the solution from the repository root:

```powershell
dotnet restore .\RocketIDE.sln
dotnet build .\RocketIDE.sln --configuration Release
```

See [distribution and packaging](docs/DISTRIBUTION.md), [architecture](docs/ROCKET_IDE_DESIGN_SPEC.md), [Rocket integration](docs/ROCKET_INTEGRATION_CONTRACT.md), and [debugger architecture](docs/DEBUGGER_ARCHITECTURE.md) for more detail.
