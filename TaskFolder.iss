; Inno Setup Script for TaskFolder
; Quick application launcher for Windows 11 taskbar

#define MyAppName "TaskFolder"
#define MyAppVersion "1.3.0"
#define MyAppPublisher "TaskFolder"
#define MyAppURL "https://github.com/rod-trent/TaskFolder"
#define MyAppExeName "TaskFolder.exe"
#define MyAppIconName "TaskFolder.ico"

; ============================================
; CONFIGURE YOUR PUBLISH PATH HERE
; ============================================
; Change this to match where your TaskFolder.exe is located
; Common options:
;   "bin\Release\net8.0-windows\publish"
;   "bin\Release\net8.0-windows\win-x64\publish"
;   Or use full path: "C:\Code\TaskFolder\bin\Release\net8.0-windows\publish"

#define PublishPath "C:\Code\TaskFolder\publish"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{YOUR-GUID-HERE}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
;LicenseFile=LICENSE.txt
; Uncomment the following line to run in non administrative install mode (install for current user only.)
;PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer
OutputBaseFilename=TaskFolder-Setup-{#MyAppVersion}
;SetupIconFile=Resources\{#MyAppIconName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
;UninstallDisplayIcon={app}\{#MyAppIconName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Run {#MyAppName} at Windows startup"; GroupDescription: "Startup Options:"; Flags: unchecked
Name: "pintotray"; Description: "Always show {#MyAppName} icon in the taskbar system tray (opens Windows Settings)"; GroupDescription: "Taskbar Options:"

[Files]
; Main application files - uses the PublishPath defined above
Source: "{#PublishPath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Run at startup if task is selected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

; Configure system tray icon behavior for Windows 11
; Note: The actual icon visibility is stored in a binary format that Windows manages
; This registry setting helps ensure the app's notifications are enabled
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{#MyAppName}"; ValueType: dword; ValueName: "Enabled"; ValueData: 1; Flags: createvalueifdoesntexist uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{#MyAppName}"; ValueType: dword; ValueName: "ShowInActionCenter"; ValueData: 1; Flags: createvalueifdoesntexist

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; Open Windows Taskbar Settings so the user can pin the icon (checked by default via pintotray task)
Filename: "ms-settings:taskbar"; Description: "Open Windows Settings to pin {#MyAppName} to the system tray"; Flags: shellexec nowait postinstall skipifsilent unchecked; Tasks: pintotray

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

[Code]
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  
  // Check for Windows 11 (Build 22000+)
  if (Version.Major < 10) or ((Version.Major = 10) and (Version.Build < 22000)) then
  begin
    MsgBox('This application requires Windows 11 or later.' + #13#13 + 
           'Your Windows version: ' + IntToStr(Version.Major) + '.' + IntToStr(Version.Minor) + 
           ' (Build ' + IntToStr(Version.Build) + ')', mbError, MB_OK);
    Result := False;
  end
  else
  begin
    Result := True;
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  // Check if application is running
  if CheckForMutexes('{#MyAppName}Mutex') then
  begin
    if MsgBox('{#MyAppName} is currently running. Please close it before uninstalling.' + #13#13 + 
              'Would you like to attempt to close it now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // Try to terminate the process
      Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000);
    end
    else
    begin
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // If the user chose to pin the icon, show a quick reminder before Settings opens
    if WizardIsTaskSelected('pintotray') then
    begin
      MsgBox(
        'Windows Settings will open to the Taskbar page.' + #13#10 + #13#10 +
        'Scroll down to "Other system tray icons", find TaskFolder in the list, ' +
        'and toggle it to On.' + #13#10 + #13#10 +
        'This makes the TaskFolder icon always visible on your taskbar instead of hiding in the overflow menu.',
        mbInformation, MB_OK);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean up any remaining files or registry entries
    if MsgBox('Do you want to remove all TaskFolder settings and data?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\{#MyAppName}'), True, True, True);
    end;
  end;
end;
