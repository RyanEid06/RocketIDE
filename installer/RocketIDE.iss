#define PayloadRoot GetEnv("ROCKETIDE_PAYLOAD_ROOT")
#define InstallerOutput GetEnv("ROCKETIDE_INSTALLER_OUTPUT")
#define WizardLogo GetEnv("ROCKETIDE_WIZARD_LOGO")

#if PayloadRoot == ""
  #error "ROCKETIDE_PAYLOAD_ROOT must point to the extracted frozen consumer payload."
#endif

#if InstallerOutput == ""
  #error "ROCKETIDE_INSTALLER_OUTPUT must point to the installer output directory."
#endif

#if WizardLogo == ""
  #error "ROCKETIDE_WIZARD_LOGO must point to the RocketIDE wizard icon image."
#endif

[Setup]
AppId={{E8F9FB0A-7DB0-49B3-A7F8-68F8253F1D07}
AppName=RocketIDE
AppVersion=1.0.0
AppVerName=RocketIDE 1.0.0
AppPublisher=Ryan Eid
AppPublisherURL=https://github.com/RyanEid06/RocketIDE
AppSupportURL=https://github.com/RyanEid06/RocketIDE
DefaultDirName={localappdata}\Programs\RocketIDE
DefaultGroupName=RocketIDE
UsePreviousAppDir=yes
UsePreviousGroup=no
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern dynamic
SetupIconFile=..\src\RocketIDE.App\Assets\RocketIDE.ico
WizardSmallImageFile={#WizardLogo}
WizardSmallImageFileDynamicDark={#WizardLogo}
WizardSmallImageBackColor=none
UninstallDisplayIcon={app}\RocketIDE.exe
OutputDir={#InstallerOutput}
OutputBaseFilename=RocketIDE-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=RocketIDE.exe
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RocketIDE"; Filename: "{app}\RocketIDE.exe"
Name: "{autodesktop}\RocketIDE"; Filename: "{app}\RocketIDE.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RocketIDE.exe"; Description: "Launch RocketIDE"; Flags: nowait postinstall skipifsilent
