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

        // Tray menu appearance
        public int MenuItemHeight { get; set; } = 36;
        public float MenuFontSize { get; set; } = 10.0f;
        // Retained for compatibility with existing settings.json files. The menu header is logo-only.
        public string MenuHeaderText { get; set; } = string.Empty;
        public string MenuHeaderLogoPath { get; set; } = string.Empty;

        public bool PortableMode { get; set; } = false;
    }
}
