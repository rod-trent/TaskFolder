# TaskFolder - Windows 11 Taskbar Application Launcher

## Overview
TaskFolder is a Windows 11 application that creates a customizable folder on the taskbar where users can organize and quickly access their application shortcuts. Users can create shortcuts, drag and drop applications, and manage their taskbar launcher collection.

## Technical Architecture

### Technology Stack
- **Language**: C# with .NET 6.0+ or .NET Framework 4.8
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Windows APIs**: 
  - Shell32.dll for taskbar integration
  - User32.dll for window management
  - ITaskbarList3/ITaskbarList4 COM interfaces

### Core Components

1. **TaskFolder.exe** - Main application
   - System tray icon
   - Background service
   - Taskbar toolbar integration

2. **ShortcutManager** - Handles shortcut operations
   - Create shortcuts
   - Delete shortcuts
   - Monitor folder changes

3. **TaskbarIntegration** - Windows Shell integration
   - Register custom taskbar toolbar
   - Handle drag-and-drop operations
   - Manage taskbar button behavior

4. **ConfigurationManager** - Settings and preferences
   - Shortcut folder location
   - Display preferences
   - Auto-start configuration

## Implementation Approach

### Phase 1: Core Infrastructure
- Create Windows Forms/WPF application structure
- Implement system tray icon
- Set up configuration system
- Create shortcuts folder management

### Phase 2: Taskbar Integration
- Implement custom toolbar using Windows Shell APIs
- Register toolbar with taskbar
- Handle click events on toolbar items

### Phase 3: Shortcut Management
- Drag-and-drop functionality to taskbar toolbar
- Context menu for shortcuts (rename, delete, properties)
- File system watcher for automatic updates
- Icon extraction and caching

### Phase 4: Polish & Features
- Settings dialog
- Auto-start with Windows
- Keyboard shortcuts
- Theming support for Windows 11

## Windows 11 Taskbar Considerations

**Important Note**: Windows 11 significantly restricted taskbar customization compared to Windows 10. The traditional toolbar approach (used in Windows 7/10) is no longer fully supported.

### Alternative Approaches:

1. **Jump List Integration**
   - Add shortcuts to the application's jump list
   - Accessible via right-click on taskbar icon
   - Most reliable method for Windows 11

2. **Overlay Icon**
   - Show folder icon in system tray
   - Click to show popup menu with shortcuts
   - Similar to traditional start menu

3. **AppBar Approach**
   - Create a small always-on-top window
   - Position it near taskbar
   - Provides folder-like experience

## Recommended Implementation: Jump List + System Tray

Given Windows 11's restrictions, the most practical approach combines:

1. **System Tray Icon**: Always visible, provides quick access
2. **Jump List**: Shows recent/pinned shortcuts when right-clicking taskbar icon
3. **Popup Menu**: Left-click tray icon shows full folder contents

This provides the intended functionality while working within Windows 11's constraints.

## File Structure
```
TaskFolder/
├── TaskFolder.sln
├── TaskFolder/
│   ├── App.xaml / Program.cs
│   ├── MainWindow.xaml / MainForm.cs
│   ├── Models/
│   │   ├── ShortcutItem.cs
│   │   └── AppConfig.cs
│   ├── Services/
│   │   ├── ShortcutManager.cs
│   │   ├── JumpListManager.cs
│   │   └── ConfigurationService.cs
│   ├── Views/
│   │   ├── SettingsWindow.xaml
│   │   └── ShortcutMenu.xaml
│   ├── Resources/
│   │   ├── Icons/
│   │   └── Styles/
│   └── Utilities/
│       ├── IconExtractor.cs
│       └── ShellHelper.cs
├── Setup/
│   └── Installer.iss (Inno Setup script)
└── README.md
```

## Key Features

1. **Shortcut Management**
   - Add applications via drag-and-drop
   - Create shortcuts from file browser
   - Remove shortcuts
   - Rename shortcuts
   - Organize in custom order

2. **Quick Access**
   - System tray icon for instant access
   - Jump list integration for recent items
   - Keyboard shortcut support (e.g., Win+Shift+T)

3. **Configuration**
   - Custom shortcuts folder location
   - Auto-start with Windows
   - Icon size preferences
   - Theme customization

4. **User Experience**
   - Familiar folder-like interface
   - Windows 11 design language
   - Smooth animations
   - Responsive performance

## Installation & Deployment

- Installer using Inno Setup or WiX Toolset
- Auto-start registry configuration
- Minimal footprint (<10MB installed)
- No administrator rights required for normal operation

## Future Enhancements

- Cloud sync for shortcuts across devices
- Portable apps support
- Folder categorization
- Search functionality
- Usage statistics and most-used apps
- Integration with Windows Store apps
