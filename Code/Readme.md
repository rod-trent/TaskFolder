# TaskFolder - Windows 11 Application Launcher

TaskFolder is a lightweight Windows 11 application that provides quick access to your favorite applications through an easy-to-manage system tray launcher with Windows Jump List integration.

## Features

✨ **Quick Access** - Launch applications from a convenient system tray menu  
📌 **Jump List Integration** - Access shortcuts from the Windows taskbar  
🎨 **Windows 11 Native** - Follows Windows 11 design principles  
⚡ **Lightweight** - Minimal resource usage  
🔄 **Auto-Start** - Optional startup with Windows  
📁 **Easy Management** - Simple drag-and-drop shortcut management  

## System Requirements

- Windows 11 (may work on Windows 10 with minor modifications)
- .NET 6.0 or later runtime
- Approximately 10 MB disk space

## Installation

### Option 1: Download Pre-built Release
1. Download the latest release from the Releases page
2. Extract the ZIP file to your preferred location
3. Run `TaskFolder.exe`
4. (Optional) Enable "Start with Windows" in Settings

### Option 2: Build from Source

#### Prerequisites
- Visual Studio 2022 or later with .NET desktop development workload
- .NET 6.0 SDK or later

#### Build Steps
1. Clone or download this repository
2. Open `TaskFolder.sln` in Visual Studio
3. Restore NuGet packages (right-click solution → Restore NuGet Packages)
4. Build the solution (Ctrl+Shift+B)
5. The executable will be in `bin/Debug/net6.0-windows/` or `bin/Release/net6.0-windows/`

#### Command Line Build
```bash
# Clone the repository
git clone https://github.com/yourusername/TaskFolder.git
cd TaskFolder

# Build the project
dotnet build -c Release

# Run the application
dotnet run
```

## Usage

### Getting Started

1. **Launch TaskFolder**: Double-click `TaskFolder.exe`
2. **Access Menu**: Click the TaskFolder icon in the system tray
3. **Add Applications**: 
   - Click "Add Application..." in the tray menu
   - Browse and select an executable (.exe) or shortcut (.lnk) file
   - Or drag and drop files into the shortcuts folder

### Managing Shortcuts

#### Add a Shortcut
- Right-click the tray icon → "Add Application..."
- Select an .exe or .lnk file
- The shortcut will appear in your menu

#### Remove a Shortcut
- Right-click any shortcut in the menu → "Remove"
- Or delete the .lnk file from the shortcuts folder

#### Organize Shortcuts
- Open the shortcuts folder (right-click tray icon → "Open Shortcuts Folder")
- Rename .lnk files to change display names
- Use Windows Explorer to organize files

### Keyboard Shortcuts
*(Future feature - not yet implemented)*
- `Win + Shift + T` - Open TaskFolder menu

### Jump List Integration
- Right-click the TaskFolder taskbar icon (when pinned)
- Your shortcuts appear in the jump list
- Click any item to launch that application

## Configuration

### Settings Dialog
Access via: Right-click tray icon → "Settings"

**Startup Options:**
- ☑ Start TaskFolder when Windows starts
- ☑ Show notifications when applications launch

**Shortcuts Management:**
- Open Shortcuts Folder - Browse your shortcuts directory
- Remove All Shortcuts - Clear all shortcuts at once

### Manual Configuration

**Shortcuts Folder Location:**
```
%APPDATA%\TaskFolder\Shortcuts\
```
Typical path: `C:\Users\YourName\AppData\Roaming\TaskFolder\Shortcuts\`

**Auto-Start Registry Key:**
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\TaskFolder
```

## Project Structure

```
TaskFolder/
├── Program.cs                  # Application entry point
├── Models/
│   └── ShortcutItem.cs        # Shortcut data model
├── Services/
│   ├── ShortcutManager.cs     # Shortcut management logic
│   └── JumpListManager.cs     # Windows Jump List integration
├── Views/
│   └── SettingsForm.cs        # Settings dialog
├── Utilities/
│   └── IconExtractor.cs       # Icon extraction from executables
└── Resources/
    └── Icons/                  # Application icons
```

## Technical Details

### Architecture
- **Framework**: .NET 6.0 with Windows Forms and WPF
- **UI**: System tray with NotifyIcon control
- **Storage**: Windows shortcuts (.lnk files) in AppData
- **Integration**: Windows Shell COM APIs for Jump Lists

### Key Technologies
- `IWshRuntimeLibrary` - Windows Script Host for .lnk file handling
- `System.Windows.Shell.JumpList` - Windows 7+ Jump List API
- `NotifyIcon` - System tray integration
- `FileSystemWatcher` - Automatic shortcut folder monitoring

### Windows 11 Limitations
Windows 11 restricted many taskbar customization APIs that were available in Windows 10 and earlier. TaskFolder works around these limitations by:
- Using Jump Lists instead of custom toolbars
- Providing system tray access as the primary interface
- Following Windows 11 design guidelines

## Troubleshooting

### TaskFolder doesn't start with Windows
- Check Settings → "Start TaskFolder when Windows starts" is enabled
- Verify registry key exists: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TaskFolder`
- Ensure the path in the registry points to the correct TaskFolder.exe location

### Shortcuts don't appear in menu
- Verify shortcuts folder exists: `%APPDATA%\TaskFolder\Shortcuts\`
- Check that .lnk files in the folder point to valid executables
- Try removing and re-adding the shortcut

### Icons don't display correctly
- Ensure target application still exists at the original path
- Try removing and recreating the shortcut
- Some portable applications may not have extractable icons

### Jump List not updating
- Pin TaskFolder to the taskbar (drag exe to taskbar)
- Restart TaskFolder
- Right-click the taskbar icon to verify jump list items

## Known Issues

1. **Icon extraction may fail** for some executables (uses default icon as fallback)
2. **Jump List has 10-item limit** (Windows limitation)
3. **No support for Windows Store apps** (UWP apps use different launching mechanism)

## Future Enhancements

- [ ] Keyboard shortcut activation
- [ ] Search functionality for large shortcut collections
- [ ] Categories/folders for organizing shortcuts
- [ ] Import shortcuts from other sources
- [ ] Statistics tracking (most-used apps)
- [ ] Customizable themes
- [ ] Cloud sync across devices
- [ ] Portable apps support

## Contributing

Contributions are welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

MIT License - See LICENSE file for details

## Credits

Developed by [Your Name]
Icon design by [Designer Name]

## Support

- Report issues: [GitHub Issues](https://github.com/yourusername/TaskFolder/issues)
- Discussions: [GitHub Discussions](https://github.com/yourusername/TaskFolder/discussions)
- Email: support@taskfolder.example

## Changelog

### Version 1.0.0 (2025-01-05)
- Initial release
- System tray integration
- Jump List support
- Basic shortcut management
- Auto-start capability
- Windows 11 compatibility

---

**Note**: This application is not affiliated with or endorsed by Microsoft Corporation.
