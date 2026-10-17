; Inno Setup script for Mercury Converter.
; Build it with scripts\build-installer.ps1, which publishes the app first.

#define AppName "Mercury Converter"
#define AppVersion "1.0.0"
#define AppExe "MercuryConverter.exe"

[Setup]
AppId={{6E2B4C1A-3F7D-4B8E-9A21-5D0C7E9F4B13}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Mercury Converter contributors
AppPublisherURL=https://github.com/
DefaultDirName={autopf}\Mercury Converter
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Mercury.Converter.App\Assets\AppIcon.ico
OutputDir=..\dist
OutputBaseFilename=MercuryConverter-{#AppVersion}-x64-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Installs for the current user only, so no administrator password is needed.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
LicenseFile=..\LICENSE
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the right-click menu entries that the app adds (one menu per file type). The app does this without opening a window.
; Settings, presets and previews in %LOCALAPPDATA%\MercuryConverter are kept.
Filename: "{app}\{#AppExe}"; Parameters: "--remove-menu"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveRightClickMenu"
