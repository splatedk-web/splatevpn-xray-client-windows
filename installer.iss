; SplateVPN Desktop - Inno Setup Script
; Requires Inno Setup 6+ (https://jrsoftware.org/isinfo.php)
; Build: ISCC.exe installer.iss

#define MyAppName "SplateVPN Desktop"
#define MyAppVersion "2.12.5"
#define MyAppPublisher "Splate"
#define MyAppURL "https://github.com/splatedk-web"
#define MyAppExeName "SplateClient.exe"

[Setup]
AppId={{B7C4F8E2-1A3D-4E5F-9B2C-6D8A0E1F3C5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\SplateVPN
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=SplateVPN_Setup_v{#MyAppVersion}
SetupIconFile=Assets\app.ico
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardImageStretch=no
CloseApplications=yes
CloseApplicationsFilter=SplateClient.exe
RestartApplications=no
ShowLanguageDialog=auto
MinVersion=10.0

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные задачи:"
Name: "autostart"; Description: "Запускать при старте Windows"; GroupDescription: "Дополнительные задачи:"

[Files]
Source: "release-v{#MyAppVersion}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SplateVPN"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{sys}\icacls.exe"; Parameters: """{app}\runtime\WebView2"" /grant *S-1-15-2-2:(OI)(CI)(RX) *S-1-15-2-1:(OI)(CI)(RX)"; Flags: runhidden waituntilterminated; Check: IsWindows10
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить SplateVPN"; Flags: nowait postinstall skipifsilent

[Code]
function IsWindows10: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 10) and (Version.Build < 22000);
end;
