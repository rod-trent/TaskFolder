# TaskFolder

**A lightweight, modern application launcher for Windows 11**

TaskFolder brings back quick-access application launching to Windows 11's taskbar area through a clean system tray menu. Launch your favorite apps, PWAs, and scripts with a single click—no taskbar clutter required.

![Version](https://img.shields.io/badge/Version-1.1.0-green.svg)
![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2011%2F10-blue.svg)

## ✨ Features

- **🎯 System Tray Integration** - Quick access without cluttering your taskbar
- **🌐 Perfect PWA Support** - Edge/Chrome Progressive Web Apps work flawlessly with correct icons
- **⚡ Lightning Fast** - Under 200 KB, <5 MB RAM, zero CPU when idle
- **🔄 Auto-Refresh** - Monitors shortcuts folder for automatic updates
- **🎨 Smart Icons** - Extracts proper icons from PWAs, executables, and custom locations
- **🚀 Auto-Start** - Optional startup with Windows
- **📁 Simple Management** - Drag-and-drop shortcuts or use the built-in browser
- **🔍 Search/Filter** - Instantly filter your shortcuts by name
- **📂 Categories** - Organize shortcuts into subfolders that appear as submenus
- **🌍 URL Shortcut Support** - Launch `.url` Internet Shortcuts directly
- **⌨️ Global Hotkey** - User-configurable hotkey to pop the menu from anywhere
- **📊 Launch Statistics** - Tracks most-used and recently-used shortcuts across restarts
- **🖼️ Custom Icons** - Override any shortcut's icon with a file + index picker
- **👤 Profiles** - Multiple named shortcut sets, switchable from the tray menu
- **💾 Portable Mode** - Run from a USB drive with `--portable` flag
- **🆓 Completely Free** - Open source under MIT License

## 🚀 Quick Start

### Prerequisites

- Windows 11 or Windows 10
- .NET 8.0 Runtime ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))

### Installation

1. Download the latest release from [Releases](../../releases) *(or build from source)*
2. Look for the icon in your system tray
3. Right-click → "Add Application..." to get started!

### Building from Source

```bash
git clone https://github.com/rod-trent/TaskFolder.git
dotnet build TaskFolder.csproj -c Release
```

Output: `bin/Release/net8.0-windows/TaskFolder.exe`

## 📖 Usage

### Adding Applications

**Method 1: GUI**
1. Right-click TaskFolder system tray icon
2. Click "Add Application..."
3. Browse to any `.exe`, `.lnk`, or `.url` file
4. Done!

**Method 2: Direct**
- Drop shortcut files into: `%APPDATA%\TaskFolder\Shortcuts\`
- TaskFolder automatically detects and adds them

### Launching Applications

- **Left-click** the tray icon to see your menu
- **Global hotkey** (configurable in Settings) to open the menu from anywhere
- **Click** any application to launch it
- **Right-click** a shortcut for per-item options

### Managing Shortcuts

- **Remove**: Right-click shortcut → Remove
- **Rename**: Right-click shortcut → Rename...
- **Reorder**: Right-click shortcut → Move Up / Move Down
- **Change Icon**: Right-click shortcut → Change Icon...
- **Open File Location**: Right-click shortcut → Open File Location

### Search & Filter

Type in the search box at the top of the tray menu to instantly filter shortcuts by name. Clear the box to show all shortcuts.

### Categories (Subfolders)

Create subfolders inside your Shortcuts folder — each subfolder becomes a submenu in the tray menu.

```
%APPDATA%\TaskFolder\Shortcuts\
├── Notepad.lnk          ← appears at root
├── Dev\
│   ├── VS Code.lnk      ← appears under "Dev" submenu
│   └── Terminal.lnk
└── Web\
    └── GitHub.url        ← appears under "Web" submenu
```

### URL Shortcuts

Add `.url` Internet Shortcut files alongside `.lnk` and `.exe` files. They open in your default browser and display with a globe icon.

### Most Used & Recently Used

After launching shortcuts, **Most Used** and **Recently Used** submenus appear at the top of the menu (top 5 each). Launch counts and timestamps persist across restarts.

### Profiles

Switch between named sets of shortcuts without mixing them together — great for separating Work and Personal shortcuts.

- **Switch Profile**: Tray menu → Switch Profile → pick a profile
- **New Profile**: Tray menu → Switch Profile → New Profile...
- Each profile is a separate folder under `%APPDATA%\TaskFolder\Profiles\`
- The **Default** profile maps to the existing `Shortcuts\` folder

### Portable Mode

Run TaskFolder from a USB drive or a folder without touching `%APPDATA%`:

```bash
TaskFolder.exe --portable
```

All data (`settings.json`, `metadata.json`, `Shortcuts\`) is stored next to the executable.

## 💡 Use Cases

### For Power Users
- Quick access to development tools
- Launch different IDE profiles
- Access PowerShell scripts instantly
- Open VMs and databases

### For Productivity
- Email and calendar (as PWAs)
- Slack, Teams, Discord
- Notion, Todoist, Trello
- Reference documents

### For Everyone
- Favorite websites as apps (`.url` shortcuts)
- Frequently-used Office apps
- Remote desktop connections
- Anything with a shortcut!

## 🎨 PWA Support

TaskFolder has excellent support for Progressive Web Apps:

**Correct Icon Extraction**
- Gmail shows ✉️ Gmail icon (not Edge icon)
- YouTube shows ▶️ YouTube icon
- Twitter shows 🐦 Twitter icon

**Proper Launch Behavior**
- PWAs open as dedicated app windows
- All command-line arguments preserved
- Works identically to taskbar-pinned PWAs

**How?** TaskFolder launches the `.lnk` file itself (not the executable), letting Windows handle all the parameters correctly.

## ⚙️ Settings

Access via: Right-click tray icon → "Settings"

- ☑️ **Start with Windows** - Auto-start on boot
- 🔔 **Show Notifications** - Balloon tip when an app is launched
- ⌨️ **Global Hotkey** - Enable and configure the hotkey (e.g. Ctrl+Alt+T)
- 📁 **Open Shortcuts Folder** - Open the active profile's shortcuts directory
- 🗑️ **Remove All Shortcuts** - Clear the current profile's shortcuts

## 📂 File Locations

**Normal mode:**
```
%APPDATA%\TaskFolder\
├── settings.json          ← app settings
├── metadata.json          ← per-shortcut stats, sort order, custom icons
├── Shortcuts\             ← Default profile
└── Profiles\
    └── <ProfileName>\     ← Additional profiles
```

**Portable mode** (`--portable` flag): all of the above lives next to `TaskFolder.exe`.

**Auto-Start Registry:**
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\TaskFolder
```

## 🏗️ Architecture

### Tech Stack
- **.NET 8.0** - Modern Windows framework
- **Windows Forms** - System tray and UI
- **Dynamic COM** - Shortcut (.lnk) manipulation
- **Win32 APIs** - Icon extraction, global hotkey (`RegisterHotKey`)
- **System.Text.Json** - Settings and metadata persistence

### Project Structure
```
TaskFolder/
├── Program.cs                  # Entry point, tray logic, menu construction
├── Models/
│   ├── AppSettings.cs          # JSON-serializable app settings
│   ├── ShortcutItem.cs         # Shortcut data model
│   └── ShortcutMetadata.cs     # Per-shortcut persisted stats & overrides
├── Services/
│   ├── SettingsService.cs      # Reads/writes settings.json + metadata.json
│   ├── ShortcutManager.cs      # Core shortcut management (.lnk, .url, .exe)
│   ├── ProfileService.cs       # Named profile switching
│   └── HotkeyService.cs        # Global hotkey via RegisterHotKey P/Invoke
├── Views/
│   ├── SettingsForm.cs         # Settings dialog
│   ├── RenameDialog.cs         # Inline rename dialog
│   └── IconPickerDialog.cs     # Custom icon picker with live preview
└── Utilities/
    ├── IconExtractor.cs        # Icon extraction from exe/dll/ico/lnk/url
    └── DragDropHelper.cs       # Drag-drop support
```

### Design Principles

**Why launch `.lnk` files instead of executables?**

Most launcher apps make the mistake of launching the target executable directly, which breaks:
- PWAs with `--app` flags
- Applications with specific parameters
- Shortcuts with custom working directories

TaskFolder launches the shortcut file itself, letting Windows handle everything correctly.

**Why system tray instead of taskbar toolbar?**

Windows 11 deprecated the toolbar APIs that Windows 7 used. The system tray approach is actually better:
- Always accessible (no need to pin)
- Takes zero taskbar space
- Faster to access (single click or global hotkey)
- More reliable across Windows updates

**Why atomic JSON writes?**

`settings.json` and `metadata.json` are written via a `.tmp` + `File.Replace` pattern so a crash or power loss mid-write can never corrupt your data.

## 🔧 Development

### Requirements
- Visual Studio 2022 or VS Code
- .NET 8.0 SDK

### Build Commands
```bash
# Debug build
dotnet build TaskFolder.csproj

# Release build
dotnet build TaskFolder.csproj -c Release

# Run
dotnet run --project TaskFolder.csproj

# Create self-contained executable
dotnet publish TaskFolder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Building the Installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bash
# Publish first, then:
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" TaskFolder.iss
```

Output: `installer\TaskFolder-Setup-1.1.0.exe`

### Code Style
- Standard C# conventions
- XML documentation comments
- Async/await where appropriate
- Exception handling with user-friendly messages

## 🐛 Known Issues

None currently reported! 🎉

If you find a bug, please [open an issue](../../issues).

## 📋 Changelog

### v1.1.0 (April 2026)

**New Features:**
- **Search/filter box** — `ToolStripTextBox` at the top of the tray menu filters shortcuts live as you type
- **Category submenus** — Create subfolders inside the Shortcuts folder; each becomes a submenu in the tray menu
- **URL shortcut support** — `.url` Internet Shortcut files are now fully supported alongside `.lnk` and `.exe`
- **Global hotkey** — Register a system-wide hotkey (e.g. Ctrl+Alt+T) to pop the tray menu from anywhere; configurable in Settings
- **Persistent launch statistics** — `LaunchCount` and `LastUsed` now survive restarts (stored in `metadata.json`)
- **Most Used submenu** — Top 5 shortcuts by launch count, shown at the top of the menu
- **Recently Used submenu** — Top 5 shortcuts by last-launched time, shown at the top of the menu
- **Per-shortcut right-click menu** — Rename, Remove, Open File Location, Change Icon, Move Up, Move Down; all accessible by right-clicking any shortcut in the menu
- **Custom icon picker** — Override any shortcut's icon with a file + index picker dialog; persisted across restarts
- **Move Up / Move Down** — Reorder shortcuts via right-click; order is persisted in `metadata.json`
- **Profiles** — Multiple named shortcut sets switchable from the tray menu; Default profile is backwards-compatible
- **Portable mode** — `--portable` CLI flag stores all data next to the executable instead of in `%APPDATA%`
- **Balloon tip notifications** — Optional toast when an application is launched (toggle in Settings)
- **Live tray tooltip** — Hover text now shows shortcut count and active profile name

**Improvements:**
- Settings are now fully persisted to `settings.json` (previously only auto-start was saved)
- "Show notifications" checkbox in Settings is now wired up and functional
- `FileSystemWatcher` now monitors subdirectories (`IncludeSubdirectories = true`) for category folder changes
- Removed dead `JumpListManager` code and `<UseWPF>` dependency — smaller build output
- Added `.gitignore`
- GitHub URL corrected in all Inno Setup scripts

### v1.0.1 (February 2026)

**Bug Fixes:**
- **Fixed application list not auto-refreshing** — The `FileSystemWatcher` debounce used `Task.Delay().ContinueWith()` which spawned a new background task per event, causing race conditions on the shortcuts list and calling COM objects from MTA threads where they silently fail. Replaced with a proper `System.Threading.Timer` debounce.
- **Fixed cross-thread UI updates** — Shortcut change notifications now correctly marshal to the UI (STA) thread via `BeginInvoke`, and the menu handle is eagerly created so `InvokeRequired` works reliably.
- **Fixed thread safety** — Added locking around the shared shortcuts list to prevent corruption from concurrent access.
- **Fixed silent failures when adding applications** — The "Add Application" handler now catches and displays errors instead of silently swallowing exceptions.
- **Fixed property name casing typo** — Corrected `ShortCutFilePath` → `ShortcutFilePath` references that prevented compilation.

### v1.0.0 (Initial Release)

- System tray application launcher for Windows 11
- PWA support with correct icon extraction
- Auto-start with Windows option
- FileSystemWatcher-based auto-refresh
- Inno Setup installer

## 🚧 Roadmap

Potential future features (contributions welcome!):

- [ ] **Themes** - Light/dark mode menu styling
- [ ] **Cloud sync** - Sync shortcuts across machines
- [ ] **Unit tests** - Test coverage for services
- [ ] **Drag-and-drop reorder** - True drag reorder in the tray menu (currently Move Up/Down)

## 🤝 Contributing

Contributions welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Good First Issues
- Add unit tests for `SettingsService` and `ShortcutManager`
- Implement theme/dark mode support
- Add cloud sync (e.g. OneDrive folder as the Shortcuts folder)

## 📜 License

MIT License - see [LICENSE](LICENSE) file for details.

**TL;DR:** Do whatever you want with this code. Use it, modify it, distribute it, sell it—I don't care. Just don't blame me if something breaks. 😊

## 🙏 Acknowledgments

- Inspired by Windows 7's taskbar toolbars (RIP)
- Built out of frustration with Windows 11's limitations
- Powered by coffee and stubbornness

## 📞 Support

- **Issues**: [GitHub Issues](../../issues)
- **Discussions**: [GitHub Discussions](../../discussions)
- **Blog Post**: [Read the full story](#) *(link to your blog post)*

## ⭐ Star History

If you find TaskFolder useful, consider giving it a star! ⭐

It helps others discover the project and motivates continued development.

---

**Made with ❤️ by [@rod-trent](https://github.com/rod-trent)**

*A human-written tool for humans who are tired of Windows 11's taskbar limitations.*
