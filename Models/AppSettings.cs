using System;
using System.Collections.Generic;

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

        // Mouse hotkey
        public bool MouseHotkeyEnabled { get; set; } = false;
        /// <summary>"Middle", "XButton1" (back), or "XButton2" (forward).</summary>
        public string MouseHotkeyButton { get; set; } = "Middle";

        /// <summary>Menu icon size in pixels: 16 (small) or 32 (large).</summary>
        public int MenuIconSize { get; set; } = 16;

        /// <summary>Check GitHub for a new release on startup.</summary>
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>Font size (pt) for the tray menu. Range 7–20.</summary>
        public float MenuFontSize { get; set; } = 9f;

        /// <summary>Use a dark background for the tray menu.</summary>
        public bool DarkMode { get; set; } = false;

        /// <summary>
        /// Substrings stripped from shortcut display names (case-insensitive).
        /// Lets you turn "Total Commander – Shortcut" into "Total Commander".
        /// </summary>
        public List<string> NameCleanupTerms { get; set; } = new()
        {
            " - Shortcut", " - shortcut", ".lnk", ".exe", ".url"
        };
    }
}
