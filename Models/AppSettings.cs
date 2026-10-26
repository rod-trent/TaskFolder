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
        public bool TaskbarButtonEnabled { get; set; } = true;
        public bool TaskbarHoverEnabled { get; set; } = true;
        // Keep existing preference keys compatible; they control the desktop widget.
        public string WidgetDisplayName { get; set; } = string.Empty;
        public double? WidgetPositionX { get; set; }
        public double? WidgetPositionY { get; set; }
        // Retained legacy taskbar offset; desktop placement uses normalized X/Y only.
        public double? WidgetTaskbarPosition { get; set; }

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
        public string Language { get; set; } = "System";
        public string Theme { get; set; } = "System";
        public bool WidgetPositionLocked { get; set; }
        public int WidgetHoverDelayMs { get; set; } = 300;
        public int WidgetSizePercent { get; set; } = 100;
        public int WidgetMenuHideDelayMs { get; set; } = 600;
        public bool ShowRecent { get; set; } = true;
        public bool ShowMostUsed { get; set; } = true;
    }
}
