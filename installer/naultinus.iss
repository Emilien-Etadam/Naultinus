#define MyAppName "Naultinus"
#define MyAppExeName "Naultinus.exe"
#define MyAppPublisher "StouderIO"
#define MyAppURL "https://github.com/Emilien-Etadam/Naultinus"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=Naultinus-{#MyAppVersion}-setup
OutputDir=.
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
SetupIconFile=..\Naultinus.Application\Ressources\icon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Launch at Windows startup"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Naultinus"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Naultinus"; Flags: nowait postinstall skipifsilent