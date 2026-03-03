#define MyAppName "WriteSpeech"
#define MyAppVersion GetEnv('APP_VERSION')
#if MyAppVersion == ""
  #define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "Sev7eNup"
#define MyAppURL "https://github.com/Sev7eNup/WriteSpeech.NET"
#define MyAppExeName "WriteSpeech.App.exe"

[Setup]
AppId={{B8F2A1D4-7E3C-4F5B-9A1E-2D6C8F4B3A7E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\output
OutputBaseFilename=WriteSpeech-Setup-{#MyAppVersion}
SetupIconFile=..\src\WriteSpeech.App\Resources\Icons\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchstartup"; Description: "Start WriteSpeech with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Preserve user-modified appsettings.json on upgrades
Source: "..\publish\appsettings.template.json"; DestDir: "{app}"; DestName: "appsettings.json"; Flags: onlyifdoesntexist
; All other files are always overwritten
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "appsettings.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WriteSpeech"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: launchstartup

[UninstallDelete]
Type: files; Name: "{app}\appsettings.json"
