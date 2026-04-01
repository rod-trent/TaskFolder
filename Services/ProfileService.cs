using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskFolder.Services;

namespace TaskFolder.Services
{
    /// <summary>
    /// Manages named shortcut profiles (sets of shortcuts).
    /// "Default" is backwards-compatible with the legacy Shortcuts\ folder.
    /// Other profiles live under Profiles\&lt;name&gt;\.
    /// </summary>
    public class ProfileService
    {
        private readonly SettingsService _settingsService;
        private readonly ShortcutManager _shortcutManager;

        public ProfileService(SettingsService settingsService, ShortcutManager shortcutManager)
        {
            _settingsService = settingsService;
            _shortcutManager = shortcutManager;
        }

        /// <summary>Lists all available profile names (always includes "Default").</summary>
        public List<string> GetProfileNames()
        {
            var names = new List<string> { "Default" };

            string profilesRoot = Path.Combine(_settingsService.DataRoot, "Profiles");
            if (Directory.Exists(profilesRoot))
            {
                foreach (string dir in Directory.GetDirectories(profilesRoot))
                {
                    string name = Path.GetFileName(dir);
                    if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }
            }

            return names;
        }

        /// <summary>Gets the currently active profile name.</summary>
        public string ActiveProfile => _settingsService.Settings.ActiveProfileName;

        /// <summary>Switches to the named profile, updating ShortcutManager and persisting the choice.</summary>
        public void SwitchProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            string folder = _shortcutManager.ResolveShortcutsFolder(name);
            Directory.CreateDirectory(folder);

            _settingsService.Settings.ActiveProfileName = name;
            _settingsService.Save();

            _shortcutManager.SetShortcutsFolder(folder);
        }

        /// <summary>Creates a new profile directory.</summary>
        public void CreateProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Profile name cannot be empty.");

            if (GetProfileNames().Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A profile named '{name}' already exists.");

            string folder = _shortcutManager.ResolveShortcutsFolder(name);
            Directory.CreateDirectory(folder);
        }

        /// <summary>Deletes a profile's directory. Cannot delete the active profile.</summary>
        public void DeleteProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Profile name cannot be empty.");

            if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot delete the Default profile.");

            if (name.Equals(ActiveProfile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot delete the currently active profile. Switch to another profile first.");

            string folder = _shortcutManager.ResolveShortcutsFolder(name);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
