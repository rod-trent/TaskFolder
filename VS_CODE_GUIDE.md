# Using TaskFolder with Visual Studio Code

## Prerequisites

### Required Extensions
VS Code will prompt you to install these when you open the project:
1. **C# Dev Kit** (ms-dotnettools.csdevkit)
2. **C#** (ms-dotnettools.csharp)
3. **.NET Runtime** (ms-dotnettools.vscode-dotnet-runtime)

### Required Software
- **.NET 6.0 SDK** or later
  - Download from: https://dotnet.microsoft.com/download
  - Verify installation: Open terminal and run `dotnet --version`

## Opening the Project

### Method 1: From VS Code
1. Open VS Code
2. File → Open Folder
3. Select the `TaskFolder` folder
4. VS Code will detect the .csproj file and prompt to install C# extensions
5. Click "Install" when prompted

### Method 2: From Terminal
```bash
cd path/to/TaskFolder
code .
```

## Building the Project

### Option 1: Using VS Code Tasks (Recommended)
1. Press `Ctrl+Shift+B` (or Cmd+Shift+B on Mac)
2. Select "build" from the task list
3. Output appears in the Terminal panel

### Option 2: Using Terminal
1. Open Terminal in VS Code (`Ctrl+``)
2. Run:
```bash
dotnet build
```

### Option 3: Using Build Script
```bash
# In VS Code terminal
./build.bat
```

## Running the Project

### Option 1: Debug Mode (F5)
1. Press `F5`
2. The application will build and launch
3. Debugger attaches automatically
4. Set breakpoints by clicking left of line numbers

### Option 2: Run Without Debugging
1. Press `Ctrl+F5`
2. Application runs without debugger

### Option 3: Terminal
```bash
dotnet run
```

## VS Code Features Setup

### IntelliSense
- Should work automatically after installing C# Dev Kit
- If not working:
  1. Press `Ctrl+Shift+P`
  2. Type "OmniSharp: Restart OmniSharp"
  3. Press Enter

### Code Navigation
- **Go to Definition**: `F12`
- **Peek Definition**: `Alt+F12`
- **Find All References**: `Shift+F12`
- **Rename Symbol**: `F2`

### Debugging Features
- **Set Breakpoint**: Click left of line number (red dot appears)
- **Step Over**: `F10`
- **Step Into**: `F11`
- **Step Out**: `Shift+F11`
- **Continue**: `F5`
- **Stop**: `Shift+F5`

## Available Tasks

Press `Ctrl+Shift+P` and type "Tasks: Run Task" to see:

1. **build** - Compile the project
2. **publish** - Create release executable
3. **watch** - Auto-rebuild on file changes
4. **clean** - Remove build artifacts

## Project Structure in VS Code

```
TaskFolder/
├── .vscode/               ← VS Code configuration
│   ├── tasks.json        ← Build tasks
│   ├── launch.json       ← Debug configuration
│   └── extensions.json   ← Recommended extensions
├── Models/
├── Services/
├── Views/
├── Utilities/
├── TaskFolder.csproj     ← Project file
└── Program.cs            ← Entry point
```

## Useful VS Code Shortcuts

### General
- `Ctrl+Shift+P` - Command Palette
- `Ctrl+P` - Quick File Open
- `Ctrl+Shift+E` - Explorer
- `Ctrl+Shift+F` - Search across files
- `Ctrl+`` ` - Toggle Terminal

### Editing
- `Ctrl+Space` - Trigger IntelliSense
- `Ctrl+.` - Quick Actions (code fixes)
- `Alt+↑/↓` - Move line up/down
- `Ctrl+/` - Toggle line comment
- `Shift+Alt+F` - Format document

### Debugging
- `F5` - Start debugging
- `Ctrl+F5` - Run without debugging
- `F9` - Toggle breakpoint
- `F10` - Step over
- `F11` - Step into

## Troubleshooting

### "C# extension not working"
1. Check .NET SDK is installed: `dotnet --version`
2. Restart VS Code
3. Reload window: `Ctrl+Shift+P` → "Developer: Reload Window"

### "Build failed"
1. Check Terminal output for errors
2. Try: `dotnet restore` in terminal
3. Clean build: `dotnet clean` then `dotnet build`

### "Cannot find .NET SDK"
1. Download from: https://dotnet.microsoft.com/download
2. Install .NET 6.0 SDK
3. Restart VS Code
4. Verify: `dotnet --version` in terminal

### "OmniSharp server not running"
1. `Ctrl+Shift+P`
2. Type "OmniSharp: Restart OmniSharp"
3. Wait for "OmniSharp server started" message

### "IWshRuntimeLibrary not found"
This is normal - the COM reference is added at build time. If build fails:
1. Try `dotnet restore`
2. Try `dotnet clean` then `dotnet build`

## Creating a Release Build

### In Terminal:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output location:
```
bin/Release/net6.0-windows/win-x64/publish/TaskFolder.exe
```

### Using Task:
1. `Ctrl+Shift+P`
2. "Tasks: Run Task"
3. Select "publish"

## Editing Tips

### Organizing Using Statements
- `Ctrl+.` on unused imports → "Remove unnecessary usings"
- `Ctrl+Shift+P` → "Organize Imports"

### Rename Safely
1. Right-click symbol
2. "Rename Symbol" or press `F2`
3. Type new name
4. Press Enter (updates all references!)

### Find All References
1. Right-click any method/class/variable
2. "Go to References" or press `Shift+F12`
3. See all usages

## Recommended Settings

Add to `.vscode/settings.json`:
```json
{
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
        "source.organizeImports": true
    },
    "csharp.format.enable": true,
    "omnisharp.enableEditorConfigSupport": true
}
```

## Git Integration

VS Code has built-in Git support:
1. Source Control icon in sidebar (`Ctrl+Shift+G`)
2. Stage changes, commit, push
3. View diffs inline

Initialize Git (if not already):
```bash
git init
git add .
git commit -m "Initial TaskFolder commit"
```

## Additional Resources

- **VS Code Docs**: https://code.visualstudio.com/docs
- **C# in VS Code**: https://code.visualstudio.com/docs/languages/csharp
- **.NET CLI**: https://docs.microsoft.com/en-us/dotnet/core/tools/

## Quick Start Summary

1. **Install**: .NET 6.0 SDK + VS Code + C# Dev Kit extension
2. **Open**: File → Open Folder → Select TaskFolder
3. **Build**: Press `Ctrl+Shift+B`
4. **Run**: Press `F5`
5. **Done!** System tray icon appears

---

**Pro Tip**: Use the Command Palette (`Ctrl+Shift+P`) - it's your best friend in VS Code!
