; Inno Setup Script for TaskFolder - FLEXIBLE VERSION
; Quick application launcher for Windows 11 taskbar

; ============================================
; CONFIGURATION - UPDATE THESE AS NEEDED
; ============================================

#define MyAppName "TaskFolder"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TaskFolder"
#define MyAppURL "https://github.com/yourusername/TaskFolder"
#define MyAppExeName "TaskFolder.exe"
#define MyAppIconName "TaskFolder.ico"

; IMPORTANT: Choose ONE of these publish paths based on how you built your app:
; Option 1: Self-contained with win-x64 runtime (recommended)
#define PublishPath "bin\Release\net8.0-windows\win-x64\publish"

; Option 2: Framework-dependent (requires .NET 8 installed on target machine)
;#define PublishPath "bin\Release\net8.0-windows\publish"

; Option 3: Custom path
;#define PublishPath "publish"

[Setup]
; NOTE: Generate a new GUID using Tools | Generate GUID in Inno Setup
AppId={{A7B8C9D0-E1F2-4A3B-8C7D-9E0F1A2B3C4D}}
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
LicenseFile=LICENSE.txt
; Use lowest privilege for current user install
;PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer
OutputBaseFilename=TaskFolder-Setup-{#MyAppVersion}
SetupIconFile=Resources\{#MyAppIconName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Require Windows 11 (Build 22000)
MinVersion=10.0.22000
UninstallDisplayIcon={app}\{#MyAppIconName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Run {#MyAppName} at Windows startup"; GroupDescription: "Startup Options:"; Flags: unchecked

[Files]
; Copy ALL files from publish directory
Source: "{#PublishPath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation (optional - will skip if not found)
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Run at startup if task is selected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

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
