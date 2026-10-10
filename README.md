# RocketIDE 1.0.0 for Windows x64

The standard [RocketIDE installer](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-Setup-1.0.0.exe) installs the app for the current Windows user. The wizard chooses a folder, creates a Start menu shortcut, offers an optional desktop shortcut, and registers an uninstaller. The installer SHA-256 is `edcf2fa265f5663b2420286ca8b62b14d8e98f1af62927272d8d2ce02e59e8b1`.

The [portable ZIP](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip) is an alternative: extract all files to a writable folder and open `RocketIDE.exe`. When cloning this branch, the same app is under `app/`. Keep the .NET runtime and native debugger files together. The portable ZIP SHA-256 is `a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c`.

RocketIDE uses the separate [Rocket SDK for Windows x64](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/Rocket-SDK-3.0.0-windows-x64.zip). Extract the entire SDK, keeping its matching `stage0/` host for debugging and structured IDE commands, and select its `bin/rocketc.exe` and `bin/rocket-lsp.exe` in **Tools > Rocket SDK Settings**. The IDE installer does not install the SDK.
The SDK ZIP SHA-256 is `b4d72588af417a748e8d552543487ad411f26600838857be594c89a579bf50b0`.

## First-time SDK setup

1. Download the Windows SDK linked above and extract the entire ZIP into a folder you will keep, such as `Documents\Rocket\`. Keep all SDK directories together, including `stage0`.
2. Open RocketIDE and choose **Tools > Rocket SDK Settings...**.
3. Beside **Rocket compiler**, click **Browse...** and select `Rocket-SDK-3.0.0-windows-x64\bin\rocketc.exe` inside the extracted SDK.
4. Beside **Language server**, click **Browse...** and select `Rocket-SDK-3.0.0-windows-x64\bin\rocket-lsp.exe` from the same SDK. Leave **Program arguments** empty for your first test; the workspace build-output trust checkbox can stay unchecked for this downloaded SDK.
5. Click **Save**, then choose **Tools > Validate Rocket Environment**. Open **View > Output** and look for `Environment validation passed.` and compiler version `rocketc 3.0.0`.
6. Choose **File > Open Folder...** to open a project folder you can write to, open or save a `.rocket` file, then try **Build > Check Rocket Target** and **Run > Run Rocket Target**.

These selections are saved for later launches. If you move the SDK or reinstall into a different folder, repeat steps 2â€“5 with the new paths. The [setup guide with a first-program example](https://github.com/RyanEid06/Rocket-RocketIDE/blob/main/docs/GETTING_STARTED.md) also covers debugging and common setup problems.

User settings, logs, and recovery data are stored under `%LOCALAPPDATA%\RocketIDE`. Uninstalling or removing the app folder leaves that per-user data intact. To update, close the IDE and install or extract a complete newer package.

This branch contains the released app, instructions, release metadata, per-file checksums, and Git attributes. The [developer `master` branch](https://github.com/RyanEid06/RocketIDE/tree/master) retains source and tests. RocketIDE is Windows-only; the Rocket SDK has Linux and macOS packages.

Version: **1.0.0+f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2**.
