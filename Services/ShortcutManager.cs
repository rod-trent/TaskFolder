using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    /// <summary>
    /// Manages shortcuts in the TaskFolder.
    /// Supports .lnk, .exe, and .url files; one level of category subfolders.
    /// </summary>
    public class ShortcutManager : IDisposable
    {
        private string _shortcutsFolder;
        private readonly SettingsService _settingsService;
        private readonly List<ShortcutItem> _shortcuts;
        private readonly object _lock = new object();
        private FileSystemWatcher _fileWatcher;
        private Timer _debounceTimer;

        public event EventHandler ShortcutsChanged;

        /// <summary>Gets the folder where shortcuts are stored.</summary>
        public string ShortcutsFolder => _shortcutsFolder;

        public ShortcutManager(SettingsService settingsService)
        {
            _settingsService = settingsService;

            _shortcutsFolder = ResolveShortcutsFolder(settingsService.Settings.ActiveProfileName);
            Directory.CreateDirectory(_shortcutsFolder);

            _shortcuts = new List<ShortcutItem>();
            _debounceTimer = new Timer(OnDebounceTimerFired, null, Timeout.Infinite, Timeout.Infinite);
            LoadShortcuts();
            InitializeFileWatcher();
        }

        // ── Folder resolution ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the shortcuts directory for a given profile name.
        /// "Default" maps to the legacy Shortcuts\ folder for backwards compatibility.
        /// Other profiles use Profiles\&lt;name&gt;\.
        /// </summary>
        public string ResolveShortcutsFolder(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName) ||
                profileName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(_settingsService.DataRoot, "Shortcuts");
            }
            return Path.Combine(_settingsService.DataRoot, "Profiles", profileName);
        }

        /// <summary>Switches the active shortcuts folder (for profile switching).</summary>
        public void SetShortcutsFolder(string path)
        {
            _shortcutsFolder = path;
            Directory.CreateDirectory(_shortcutsFolder);
            _fileWatcher?.Dispose();
            LoadShortcuts();
            InitializeFileWatcher();
            ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── FileSystemWatcher ───────────────────────────────────────────────────

        private void InitializeFileWatcher()
        {
            _fileWatcher = new FileSystemWatcher(_shortcutsFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _fileWatcher.Created += OnFileSystemChanged;
            _fileWatcher.Deleted += OnFileSystemChanged;
            _fileWatcher.Renamed += OnFileSystemChanged;
            _fileWatcher.Changed += OnFileSystemChanged;
            _fileWatcher.Error += OnFileWatcherError;
        }

        private void OnFileWatcherError(object sender, ErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"FileSystemWatcher error: {e.GetException().Message}");
            try { _fileWatcher?.Dispose(); } catch { }
            InitializeFileWatcher();
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            _debounceTimer.Change(500, Timeout.Infinite);
        }

        private void OnDebounceTimerFired(object state)
        {
            ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Loading ─────────────────────────────────────────────────────────────

        private void LoadShortcuts()
        {
            lock (_lock)
            {
                _shortcuts.Clear();

                try
                {
                    // Root-level shortcuts (no category)
                    LoadFromDirectory(_shortcutsFolder, null);

                    // One level of subdirectories = categories
                    foreach (string subDir in Directory.GetDirectories(_shortcutsFolder))
                    {
                        string category = Path.GetFileName(subDir);
                        LoadFromDirectory(subDir, category);
                    }

                    // Sort by SortOrder, then alphabetically by display name
                    _shortcuts.Sort((a, b) =>
                    {
                        int cmp = a.SortOrder.CompareTo(b.SortOrder);
                        return cmp != 0 ? cmp
                            : string.Compare(a.GetDisplayName(), b.GetDisplayName(), StringComparison.OrdinalIgnoreCase);
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ShortcutManager.LoadShortcuts: {ex.Message}");
                }
            }
        }

        private void LoadFromDirectory(string directory, string category)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".lnk" && ext != ".url" && ext != ".exe")
                    continue;

                try
                {
                    var shortcut = LoadShortcutFromFile(file);
                    if (shortcut == null) continue;

                    // Apply persisted metadata
                    var meta = _settingsService.GetMetadata(Path.GetFileName(file));
                    shortcut.LaunchCount = meta.LaunchCount;
                    shortcut.LastUsed = meta.LastUsed;
                    shortcut.SortOrder = meta.SortOrder;
                    shortcut.Category = category;

                    // Apply custom icon override
                    if (!string.IsNullOrEmpty(meta.CustomIconPath) && File.Exists(meta.CustomIconPath))
                    {
                        var customIcon = IconExtractor.ExtractIcon(meta.CustomIconPath, meta.CustomIconIndex);
                        if (customIcon != null)
                            shortcut.Icon = customIcon;
                    }

                    if (shortcut.IsValid() || ext == ".url")
                        _shortcuts.Add(shortcut);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load shortcut {file}: {ex.Message}");
                }
            }
        }

        private ShortcutItem LoadShortcutFromFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".url")
                return LoadUrlShortcut(filePath);

            if (ext == ".exe")
                return LoadExeShortcut(filePath);

            return LoadLnkShortcut(filePath);
        }

        private ShortcutItem LoadLnkShortcut(string linkPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(linkPath);

                Icon icon = null;
                try
                {
                    string iconLocation = link.IconLocation;
                    if (!string.IsNullOrEmpty(iconLocation))
                    {
                        var parts = iconLocation.Split(',');
                        string iconPath = parts[0].Trim();
                        int iconIndex = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int idx) ? idx : 0;
                        if (File.Exists(iconPath))
                            icon = IconExtractor.ExtractIcon(iconPath, iconIndex);
                    }
                }
                catch { }

                if (icon == null) icon = IconExtractor.GetShortcutIcon(linkPath);
                if (icon == null) icon = IconExtractor.ExtractIcon(link.TargetPath);

                return new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(linkPath),
                    TargetPath = link.TargetPath,
                    WorkingDirectory = link.WorkingDirectory,
                    Arguments = link.Arguments,
                    Description = link.Description,
                    ShortcutFilePath = linkPath,
                    DateCreated = File.GetCreationTime(linkPath),
                    Icon = icon
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadLnkShortcut failed for {linkPath}: {ex.Message}");
                return null;
            }
        }

        private ShortcutItem LoadUrlShortcut(string urlPath)
        {
            try
            {
                string url = null;
                foreach (string line in File.ReadAllLines(urlPath))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        url = line.Substring(4).Trim();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(url))
                    return null;

                // Use the globe icon from shell32 for URL shortcuts
                Icon icon = IconExtractor.ExtractUrlIcon();

                return new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(urlPath),
                    TargetPath = url,
                    ShortcutFilePath = urlPath,
                    DateCreated = File.GetCreationTime(urlPath),
                    Icon = icon,
                    Description = $"Open {url}"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadUrlShortcut failed for {urlPath}: {ex.Message}");
                return null;
            }
        }

        private ShortcutItem LoadExeShortcut(string exePath)
        {
            try
            {
                return new ShortcutItem
                {
                    Name = Path.GetFileNameWithoutExtension(exePath),
                    TargetPath = exePath,
                    ShortcutFilePath = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    DateCreated = File.GetCreationTime(exePath),
                    Icon = IconExtractor.ExtractIcon(exePath)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadExeShortcut failed for {exePath}: {ex.Message}");
                return null;
            }
        }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Returns a snapshot of all loaded shortcuts.</summary>
        public List<ShortcutItem> GetAllShortcuts()
        {
            lock (_lock)
                return new List<ShortcutItem>(_shortcuts);
        }

        /// <summary>Must be called from an STA thread (COM required for .lnk).</summary>
        public void ReloadShortcuts()
        {
            LoadShortcuts();
        }

        /// <summary>
        /// Adds a new shortcut (.exe or .lnk → creates a .lnk; .url → copies the file).
        /// </summary>
        public ShortcutItem AddShortcut(string targetPath, string customName = null)
        {
            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                throw new ArgumentException("Target path is invalid or file does not exist.");

            string ext = Path.GetExtension(targetPath).ToLowerInvariant();

            // Prevent duplicate targets (for .lnk/.exe)
            if (ext != ".url")
            {
                lock (_lock)
                {
                    if (_shortcuts.Any(s => string.Equals(s.TargetPath, targetPath,
                            StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("A shortcut for this application already exists.");
                }
            }

            string shortcutName = customName ?? Path.GetFileNameWithoutExtension(targetPath);

            if (ext == ".url")
            {
                // Copy the .url file directly
                string destPath = UniqueFilePath(_shortcutsFolder, shortcutName, ".url");
                File.Copy(targetPath, destPath);
            }
            else if (ext == ".lnk")
            {
                // Copy the .lnk file directly
                string destPath = UniqueFilePath(_shortcutsFolder, shortcutName, ".lnk");
                File.Copy(targetPath, destPath);
            }
            else
            {
                // Create a new .lnk pointing at the .exe
                string linkPath = UniqueFilePath(_shortcutsFolder, shortcutName, ".lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(linkPath);
                link.TargetPath = targetPath;
                link.WorkingDirectory = Path.GetDirectoryName(targetPath);
                link.Description = $"Launch {shortcutName}";
                link.Save();
            }

            LoadShortcuts();
            ShortcutsChanged?.Invoke(this, EventArgs.Empty);

            lock (_lock)
                return _shortcuts.FirstOrDefault(s =>
                    string.Equals(Path.GetFileNameWithoutExtension(s.ShortcutFilePath),
                        shortcutName, StringComparison.OrdinalIgnoreCase));
        }

        private string UniqueFilePath(string folder, string name, string extension)
        {
            string path = Path.Combine(folder, name + extension);
            int counter = 1;
            while (File.Exists(path))
                path = Path.Combine(folder, $"{name} ({counter++}){extension}");
            return path;
        }

        /// <summary>Removes a shortcut's file from disk and from the in-memory list.</summary>
        public bool RemoveShortcut(ShortcutItem shortcut)
        {
            if (shortcut == null || string.IsNullOrEmpty(shortcut.ShortcutFilePath))
                return false;

            if (File.Exists(shortcut.ShortcutFilePath))
                File.Delete(shortcut.ShortcutFilePath);

            _settingsService.RemoveMetadata(Path.GetFileName(shortcut.ShortcutFilePath));

            lock (_lock)
                _shortcuts.Remove(shortcut);

            ShortcutsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Renames a shortcut file on disk (works for .lnk and .url).</summary>
        public bool RenameShortcut(ShortcutItem shortcut, string newName)
        {
            if (shortcut == null || string.IsNullOrEmpty(newName))
                return false;

            try
            {
                string ext = Path.GetExtension(shortcut.ShortcutFilePath);
                string newPath = Path.Combine(
                    Path.GetDirectoryName(shortcut.ShortcutFilePath),
                    newName + ext);

                if (File.Exists(newPath))
                    throw new InvalidOperationException("A shortcut with this name already exists.");

                // Migrate metadata to new filename key
                var meta = _settingsService.GetMetadata(Path.GetFileName(shortcut.ShortcutFilePath));
                _settingsService.RemoveMetadata(Path.GetFileName(shortcut.ShortcutFilePath));
                meta.FileName = newName + ext;
                _settingsService.UpsertMetadata(meta);
                _settingsService.Save();

                File.Move(shortcut.ShortcutFilePath, newPath);

                LoadShortcuts();
                ShortcutsChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenameShortcut failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Records a launch: increments counter, sets timestamp, persists to disk.</summary>
        public void RecordLaunch(ShortcutItem shortcut)
        {
            if (shortcut == null) return;

            shortcut.LaunchCount++;
            shortcut.LastUsed = DateTime.Now;

            var meta = _settingsService.GetMetadata(Path.GetFileName(shortcut.ShortcutFilePath));
            meta.FileName = Path.GetFileName(shortcut.ShortcutFilePath);
            meta.LaunchCount = shortcut.LaunchCount;
            meta.LastUsed = shortcut.LastUsed;
            _settingsService.SaveMetadata(meta);
        }

        /// <summary>Updates the sort order for a shortcut and persists it.</summary>
        public void UpdateSortOrder(ShortcutItem shortcut, int newOrder)
        {
            if (shortcut == null) return;

            shortcut.SortOrder = newOrder;

            var meta = _settingsService.GetMetadata(Path.GetFileName(shortcut.ShortcutFilePath));
            meta.FileName = Path.GetFileName(shortcut.ShortcutFilePath);
            meta.SortOrder = newOrder;
            _settingsService.UpsertMetadata(meta);
            _settingsService.Save();
        }

        /// <summary>Saves a custom icon override for a shortcut.</summary>
        public void SetCustomIcon(ShortcutItem shortcut, string iconPath, int iconIndex)
        {
            if (shortcut == null) return;

            var meta = _settingsService.GetMetadata(Path.GetFileName(shortcut.ShortcutFilePath));
            meta.FileName = Path.GetFileName(shortcut.ShortcutFilePath);
            meta.CustomIconPath = iconPath;
            meta.CustomIconIndex = iconIndex;
            _settingsService.SaveMetadata(meta);

            // Update the in-memory icon immediately
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                shortcut.Icon = IconExtractor.ExtractIcon(iconPath, iconIndex);
        }

        public void Dispose()
        {
            _debounceTimer?.Dispose();
            _fileWatcher?.Dispose();
        }
    }
}
