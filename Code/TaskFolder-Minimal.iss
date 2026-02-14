; Inno Setup Script for TaskFolder - MINIMAL VERSION
; This version is simple and works with manual path configuration

#define MyAppName "TaskFolder"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TaskFolder"
#define MyAppURL "https://github.com/yourusername/TaskFolder"
#define MyAppExeName "TaskFolder.exe"

; ============================================
; IMPORTANT: SET YOUR PUBLISH PATH HERE
; ============================================
; Look in your bin\Release folder and find where TaskFolder.exe actually is
; Then put the FULL path here (or relative path from this .iss file location)
; 
; Common paths:
; #define PublishPath "bin\Release\net8.0-windows\publish"
; #define PublishPath "bin\Release\net8.0-windows\win-x64\publish"
; #define PublishPath "publish"
; #define PublishPath "C:\Code\TaskFolder\bin\Release\net8.0-windows\win-x64\publish"

#define PublishPath "bin\Release\net8.0-windows\publish"

[Setup]
AppId={{A7B8C9D0-E1F2-4A3B-8C7D-9E0F1A2B3C4D}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=TaskFolder-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Run at Windows startup"; Flags: unchecked

[Files]
; Copy all files from the publish directory
Source: "{#PublishPath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
