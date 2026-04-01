# TaskFolder - Quick Start Guide

## For Developers: Getting Started in 5 Minutes

### Prerequisites Check
- ✅ Windows 11 (or Windows 10)
- ✅ .NET 6.0 SDK or later
- ✅ Visual Studio 2022 OR VS Code with C# extension

### Option 1: Visual Studio (Recommended)

1. **Create new project structure:**
```
TaskFolder/
├── TaskFolder.csproj          (project file)
├── Program.cs                 (main entry point)
├── build.bat                  (build script)
├── Models/
│   └── ShortcutItem.cs
├── Services/
│   ├── ShortcutManager.cs
│   └── JumpListManager.cs
├── Views/
│   └── SettingsForm.cs
└── Utilities/
    ├── IconExtractor.cs
    └── DragDropHelper.cs
```

2. **Copy files to appropriate folders:**
   - Place `TaskFolder.csproj` in root
   - Place `Program.cs` in root
   - Place other .cs files in their respective folders (Models/, Services/, Views/, Utilities/)

3. **Open and Build:**
   - Open `TaskFolder.csproj` in Visual Studio
   - Press F5 or click Build → Build Solution
   - Run the application

### Option 2: Command Line

1. **Create project structure:**
```bash
mkdir TaskFolder
cd TaskFolder
mkdir Models Services Views Utilities

# Copy files to their folders
# ... place the .cs files in appropriate folders
```

2. **Build:**
```bash
# Easy way - use the build script
build.bat

# OR manual way
dotnet restore
dotnet build -c Release
dotnet run
```

### Project File Organization

Move each file to its proper location:

**Root Directory:**
- `TaskFolder.csproj`
- `Program.cs`
- `build.bat`
- `README.md`
- `TaskFolder_Implementation_Plan.md`

**Models Folder:**
- `ShortcutItem.cs`

**Services Folder:**
- `ShortcutManager.cs`
- `JumpListManager.cs`

**Views Folder:**
- `SettingsForm.cs`

**Utilities Folder:**
- `IconExtractor.cs`
- `DragDropHelper.cs`

### First Run

1. Run the application - you'll see a tray icon appear
2. Right-click the tray icon → "Add Application..."
3. Select any .exe file (e.g., notepad.exe)
4. Click the tray icon again - your app appears in the menu!

### Testing Jump List

1. Pin TaskFolder to taskbar (drag .exe to taskbar)
2. Right-click the taskbar icon
3. Your shortcuts appear in the jump list
4. Click any item to launch

## Key Features to Test

### 1. Add Shortcuts
- Menu method: Right-click tray → "Add Application..."
- File explorer: Open shortcuts folder and copy .lnk files

### 2. Launch Applications
- Left-click tray icon to see menu
- Click any application to launch
- Or use jump list from taskbar

### 3. Auto-Start
- Right-click tray → Settings
- Enable "Start TaskFolder when Windows starts"
- Restart Windows to test

### 4. Remove Shortcuts
- Right-click any shortcut → "Remove"
- Or delete .lnk file from shortcuts folder

## Common Build Issues

### Issue: "IWshRuntimeLibrary not found"
**Solution:** The COM reference should auto-add. If not:
```xml
<!-- Add to .csproj -->
<ItemGroup>
  <COMReference Include="IWshRuntimeLibrary">
    <Guid>f935dc20-1cf0-11d0-adb9-00c04fd58a0b</Guid>
    <VersionMajor>1</VersionMajor>
    <VersionMinor>0</VersionMinor>
    <WrapperTool>tlbimp</WrapperTool>
  </COMReference>
</ItemGroup>
```

### Issue: "System.Windows.Shell not found"
**Solution:** Ensure `<UseWPF>true</UseWPF>` is in .csproj

### Issue: Icons not displaying
**Solution:** This is normal for some apps. The code falls back to default icons.

## Customization Ideas

### Change Tray Icon
```csharp
// In TaskFolderApplicationContext.InitializeTrayIcon()
trayIcon.Icon = new Icon("path/to/your/icon.ico");
```

### Add Keyboard Shortcut
1. Register global hotkey using Windows API
2. Call menu display on hotkey press

### Custom Themes
1. Modify ContextMenuStrip colors
2. Use custom fonts
3. Add background colors

## Next Steps

1. ✅ Build and test basic functionality
2. ✅ Add your favorite applications
3. ✅ Enable auto-start
4. 📝 Read full documentation in README.md
5. 🎨 Customize to your preferences
6. 🚀 Share with friends!

## Shortcuts Folder Location

Default location:
```
C:\Users\[YourName]\AppData\Roaming\TaskFolder\Shortcuts\
```

Quick access:
- Press Win+R
- Type: `%APPDATA%\TaskFolder\Shortcuts`
- Press Enter

## Troubleshooting Quick Fixes

1. **Tray icon not showing**: Check Task Manager → TaskFolder.exe running
2. **Shortcuts not appearing**: Check shortcuts folder has .lnk files
3. **Can't launch app**: Verify target .exe still exists
4. **Auto-start not working**: Run as administrator once to set registry key

## Advanced: Creating Installer

Use Inno Setup to create installer:
```
1. Download Inno Setup from https://jrsoftware.org/isinfo.php
2. Create installer script (.iss file)
3. Include all files from bin/Release/
4. Add registry keys for auto-start
5. Compile installer
```

## Performance Tips

- TaskFolder uses <5MB RAM
- No CPU usage when idle
- FileSystemWatcher auto-updates menu
- Icon extraction cached in memory

## Contributing

Want to add features? Areas needing improvement:
- [ ] Search functionality
- [ ] Categories/folders
- [ ] Statistics tracking
- [ ] Cloud sync
- [ ] Themes support
- [ ] Portable apps support

## Support

Questions? Issues?
- Check README.md for full documentation
- Review TaskFolder_Implementation_Plan.md for architecture details
- Submit issues on GitHub

---

**Happy Launching! 🚀**
