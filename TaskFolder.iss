; Per-user Windows 10/11 installer. Compile after publishing the win-x64 app.
#define MyAppName "TaskFolder"
#define MyAppVersion "1.3.0"
#define MyAppFileVersion "1.3.0.0"
#define MyAppPublisher "TaskFolder"
#define MyAppURL "https://github.com/sorrowwind/TaskFolder"
#define MyAppExeName "TaskFolder.exe"
#define MyAppUserModelID "TaskFolder.TaskFolder"
#ifndef PublishPath
  #define PublishPath "bin\Release\net8.0-windows\win-x64\publish-folder"
#endif

[Setup]
AppId={{D461C0F5-36E3-40D1-A110-0F6422B0BBA4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppFileVersion}
VersionInfoTextVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=TaskFolder-Setup-{#MyAppVersion}-win-x64
SetupIconFile=Resources\TaskFolder.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=TaskFolder_SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start TaskFolder when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishPath}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "docs\usability-upgrade.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "docs\desktop-widget.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "docs\menu-motion.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "docs\releases\1.3.0.md"; DestDir: "{app}\docs\releases"; Flags: ignoreversion
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; AppUserModelID: "{#MyAppUserModelID}"; Comment: "Windows application launcher"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"; WorkingDir: "{app}"; AppUserModelID: "{#MyAppUserModelID}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure InitializeWizard();
begin
  // Carry over startup selected in the app, not only the previous installer's task list.
  if RegValueExists(HKCU, StartupKey, '{#MyAppName}') then
    WizardSelectTasks('startup');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, StartupKey, '{#MyAppName}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Leave a startup entry alone if the user has registered a different portable copy.
    if RegQueryStringValue(HKCU, StartupKey, '{#MyAppName}', Command) then
      if Pos(Lowercase('"' + ExpandConstant('{app}\{#MyAppExeName}') + '"'), Lowercase(Command)) = 1 then
        RegDeleteValue(HKCU, StartupKey, '{#MyAppName}');
    // User profiles, shortcuts and metadata in %APPDATA% are intentionally preserved.
  end;
end;
