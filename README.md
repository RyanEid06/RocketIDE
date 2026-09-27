# RocketIDE 1.0.0 for Windows x64

Download [the complete portable ZIP](https://github.com/RyanEid06/Rocket-RocketIDE/releases/download/v3.0.0/RocketIDE-win-x64-1.0.0.zip), extract it to a writable folder, and open RocketIDE.exe. If downloading this consumer branch instead, open app/RocketIDE.exe. Keep every file in the app folder together: the included .NET runtime and native debugger are required. There is no Next/Next/Finish installer, and no separate .NET installation is needed.

Install the Windows [Rocket 3.0.0 SDK](https://github.com/RyanEid06/Rocket-RocketIDE/releases/tag/v3.0.0), then choose **Tools > Rocket SDK Settings** and select its bin/rocketc.exe and bin/rocket-lsp.exe. The SDK is a separate download.

To update, close RocketIDE and extract a complete newer package into a separate folder. Settings, logs and recovery data are stored under %LOCALAPPDATA%\RocketIDE. Removing the app folder leaves those files intact.

This branch contains only the released app, this README, release metadata, checksums and byte-preserving Git attributes. [main](https://github.com/RyanEid06/RocketIDE/tree/main) contains the developer source, tests and documentation. The branches intentionally have different files and commit counts; do not merge the distribution into main.

Version: **1.0.0+f54a7b2cd72dabaa5a327b61685bf53bbe81d5d2**. See RELEASE.json for package provenance and SHA256SUMS.txt for individual app file hashes. The approved runtime is unchanged by documentation and naming cleanup. RocketIDE is Windows-only; the separate Rocket SDK also supports Linux and macOS Apple Silicon.
