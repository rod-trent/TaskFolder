; Inno Setup Script for TaskFolder
; Quick application launcher for Windows 11 taskbar

#define MyAppName "TaskFolder"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TaskFolder"
#define MyAppURL "https://github.com/yourusername/TaskFolder"
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

#define PublishPath "C:\Code\TaskFolder\bin\Release\net8.0-windows\win-x64\publish"

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
var
  ResultCode: Integer;
  PowerShellScript: String;
  ScriptPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Configure Windows 11 to show TaskFolder icon in system tray by default
    // This sets the toggle to "On" in Settings > Personalization > Taskbar > Other system tray icons
    
    ScriptPath := ExpandConstant('{tmp}\ConfigureTrayIcon.ps1');
    PowerShellScript := 
      '# Enable TaskFolder system tray icon in Windows 11' + #13#10 +
      '$appPath = "' + ExpandConstant('{app}\{#MyAppExeName}') + '"' + #13#10 +
      '' + #13#10 +
      'try {' + #13#10 +
      '    # Method 1: Enable notification area icon via SystemTray registry' + #13#10 +
      '    $explorerPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer"' + #13#10 +
      '    ' + #13#10 +
      '    # Ensure the key exists' + #13#10 +
      '    if (-not (Test-Path "$explorerPath\TrayNotify")) {' + #13#10 +
      '        New-Item -Path "$explorerPath\TrayNotify" -Force | Out-Null' + #13#10 +
      '    }' + #13#10 +
      '    ' + #13#10 +
      '    # Method 2: Clear the icon cache to force Windows to re-detect all tray icons' + #13#10 +
      '    # When Windows rebuilds, new apps default to "On"' + #13#10 +
      '    $regPath = "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify"' + #13#10 +
      '    ' + #13#10 +
      '    if (Test-Path $regPath) {' + #13#10 +
      '        # Backup current settings' + #13#10 +
      '        $iconStreams = Get-ItemProperty -Path $regPath -Name "IconStreams" -ErrorAction SilentlyContinue' + #13#10 +
      '        $pastIcons = Get-ItemProperty -Path $regPath -Name "PastIconsStream" -ErrorAction SilentlyContinue' + #13#10 +
      '        ' + #13#10 +
      '        # Remove the binary data that controls icon visibility' + #13#10 +
      '        Remove-ItemProperty -Path $regPath -Name "IconStreams" -ErrorAction SilentlyContinue' + #13#10 +
      '        Remove-ItemProperty -Path $regPath -Name "PastIconsStream" -ErrorAction SilentlyContinue' + #13#10 +
      '        ' + #13#10 +
      '        Write-Host "System tray icon cache cleared successfully"' + #13#10 +
      '        ' + #13#10 +
      '        # Restart Explorer to rebuild the icon list' + #13#10 +
      '        Write-Host "Restarting Windows Explorer to apply changes..."' + #13#10 +
      '        Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue' + #13#10 +
      '        Start-Sleep -Seconds 2' + #13#10 +
      '        Start-Process explorer.exe' + #13#10 +
      '        Start-Sleep -Seconds 1' + #13#10 +
      '        ' + #13#10 +
      '        Write-Host "TaskFolder will now show in the system tray by default"' + #13#10 +
      '    } else {' + #13#10 +
      '        Write-Host "Registry path not found - icon will be visible on first run"' + #13#10 +
      '    }' + #13#10 +
      '    ' + #13#10 +
      '    # Method 3: Set notification settings to ensure the app can show icons' + #13#10 +
      '    $notificationPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{#MyAppName}"' + #13#10 +
      '    if (-not (Test-Path $notificationPath)) {' + #13#10 +
      '        New-Item -Path $notificationPath -Force | Out-Null' + #13#10 +
      '    }' + #13#10 +
      '    Set-ItemProperty -Path $notificationPath -Name "Enabled" -Value 1 -Type DWord -Force' + #13#10 +
      '    Set-ItemProperty -Path $notificationPath -Name "ShowInActionCenter" -Value 1 -Type DWord -Force' + #13#10 +
      '    ' + #13#10 +
      '} catch {' + #13#10 +
      '    Write-Host "Warning: Could not configure system tray - $($_.Exception.Message)"' + #13#10 +
      '    Write-Host "You may need to manually enable the icon in Settings > Taskbar"' + #13#10 +
      '}' + #13#10;
    
    SaveStringToFile(ScriptPath, PowerShellScript, False);
    
    // Execute the PowerShell script
    Exec('powershell.exe', '-ExecutionPolicy Bypass -WindowStyle Hidden -File "' + ScriptPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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
