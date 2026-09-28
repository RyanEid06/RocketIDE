# RocketIDE 1.0.0 for Windows x64

The standard [RocketIDE installer](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-Setup-1.0.0.exe) installs the app for the current Windows user. The wizard chooses a folder, creates a Start menu shortcut, offers an optional desktop shortcut, and registers an uninstaller. The installer SHA-256 is `edcf2fa265f5663b2420286ca8b62b14d8e98f1af62927272d8d2ce02e59e8b1`.

The [portable ZIP](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip) is an alternative: extract all files to a writable folder and open `RocketIDE.exe`. When cloning this branch, the same app is under `app/`. Keep the .NET runtime and native debugger files together. The portable ZIP SHA-256 is `a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c`.

RocketIDE uses the separate [Rocket SDK for Windows x64](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/Rocket-SDK-3.0.0-windows-x64.zip). Extract the SDK and select its `bin/rocketc.exe` and `bin/rocket-lsp.exe` in **Tools > Rocket SDK Settings**. The IDE installer does not install the SDK.
The SDK ZIP SHA-256 is `32198d4a79069527976bbe547add236ca37f1aec80620e2866a30d461b92fd2e`.

User settings, logs, and recovery data are stored under `%LOCALAPPDATA%\RocketIDE`. Uninstalling or removing the app folder leaves that per-user data intact. To update, close the IDE and install or extract a complete newer package.

This branch contains the released app, instructions, release metadata, per-file checksums, and Git attributes. The [developer `master` branch](https://github.com/RyanEid06/RocketIDE/tree/master) retains source and tests. RocketIDE is Windows-only; the Rocket SDK has Linux and macOS packages.

Version: **1.0.0+f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2**.

