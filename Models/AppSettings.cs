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
    }
}
