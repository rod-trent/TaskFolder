using System;

namespace TaskFolder.Models
{
    /// <summary>
    /// Application-level settings, serialized to settings.json.
    /// </summary>
    public class AppSettings
    {
        public bool ShowNotifications { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;

        /// <summary>Active profile name. "Default" maps to the Shortcuts\ folder.</summary>
        public string ActiveProfileName { get; set; } = "Default";

        // Global hotkey
        public string HotkeyModifiers { get; set; } = "Ctrl+Alt";
        public string HotkeyKey { get; set; } = "T";
        public bool HotkeyEnabled { get; set; } = false;

        public bool PortableMode { get; set; } = false;

        /// <summary>
        /// Optional root path for all shortcut profiles (Shortcuts\ + Profiles\).
        /// When set, shortcuts live here instead of under %APPDATA%\TaskFolder\.
        /// Point this at a OneDrive or network folder to sync across machines.
        /// </summary>
        public string ProfilesRootPath { get; set; } = null;
    }
}
