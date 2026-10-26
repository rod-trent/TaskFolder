using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    /// <summary>
    /// Manages shortcuts in the TaskFolder.
    /// Supports .lnk, .exe, and .url files in a recursive folder hierarchy.
    /// </summary>
    public class ShortcutManager : IDisposable
    {
        private string _shortcutsFolder;
        private readonly SettingsService _settingsService;
        private readonly Func<string, ShortcutItem> _shortcutReader;
        private readonly Func<string, int, Icon> _customIconReader;
        private readonly Func<string, ShortcutItem> _organizerReader;
        private readonly bool _watchForChanges;
        private bool _catalogSubscribed;
        private readonly List<ShortcutItem> _shortcuts;
        private readonly List<string> _folders;
        private readonly object _lock = new object();
        private FileSystemWatcher _fileWatcher;
        private Timer _debounceTimer;
        private readonly Dictionary<string, string> _renamedBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _externalRefreshPending;
        private int _executingOperation;
        private string _loadedDiskStamp;
        private long _watcherGeneration;

        public event EventHandler ShortcutsChanged;

        /// <summary>Gets the folder where shortcuts are stored.</summary>
        public string ShortcutsFolder => _shortcutsFolder;

        public ShortcutManager(SettingsService settingsService)
            : this(settingsService, null, null, true)
        {
        }

        /// <summary>Fixture seam: readers replace Shell/icon access, and watching can be disabled.</summary>
        internal ShortcutManager(SettingsService settingsService,
            Func<string, ShortcutItem> shortcutReader,
            Func<string, int, Icon> customIconReader,
            bool watchForChanges)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _shortcutReader = shortcutReader ?? LoadShortcutFromFile;
            _organizerReader = shortcutReader ?? ReadLnkDefinitionOnly;
            _customIconReader = customIconReader ?? IconExtractor.ExtractIcon;
            _watchForChanges = watchForChanges;

            _shortcutsFolder = ResolveShortcutsFolder(settingsService.Settings.ActiveProfileName);
            if (!_settingsService.UsesStateV2) Directory.CreateDirectory(_shortcutsFolder);

            _shortcuts = new List<ShortcutItem>();
            _folders = new List<string>();
            _debounceTimer = new Timer(OnDebounceTimerFired, null, Timeout.Infinite, Timeout.Infinite);
            LoadShortcuts();
            InitializeFileWatcher();
            EnsureCatalogSubscription();
        }

        // ── Folder resolution ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the shortcuts directory for a given profile name.
        /// "Default" maps to the legacy Shortcuts\ folder for backwards compatibility.
        /// Other profiles use Profiles\&lt;name&gt;\.
        /// </summary>
        public string ResolveShortcutsFolder(string profileName)
        {
            return _settingsService.ResolveProfileRoot(profileName);
        }

        /// <summary>Switches the active shortcuts folder (for profile switching).</summary>
        public void SetShortcutsFolder(string path)
        {
            if (_settingsService.UsesStateV2)
            {
                string full = Path.GetFullPath(path);
                if (!_settingsService.StateSnapshot.Profiles.Any(profile => !profile.Suppressed && string.Equals(
                    SafePath.Resolve(_settingsService.DataRoot, profile.RelativeRoot), full, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Shortcut folders must belong to a registered profile.");
                path = full;
            }
            if (string.Equals(_shortcutsFolder, path, StringComparison.OrdinalIgnoreCase)) return;
            _shortcutsFolder = path;
            if (!_settingsService.UsesStateV2) Directory.CreateDirectory(_shortcutsFolder);
            _fileWatcher?.Dispose();
            LoadShortcuts();
            InitializeFileWatcher();
            ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── FileSystemWatcher ───────────────────────────────────────────────────

        private void InitializeFileWatcher()
        {
            if (!_watchForChanges || !Directory.Exists(_shortcutsFolder)) return;

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
            lock (_lock) { _externalRefreshPending = true; _watcherGeneration++; }
            _debounceTimer.Change(500, Timeout.Infinite);
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            lock (_lock)
            {
                _watcherGeneration++;
                _externalRefreshPending = true;
                if (e is RenamedEventArgs rename)
                {
                    string oldPath = Path.GetRelativePath(_settingsService.DataRoot, rename.OldFullPath);
                    string newPath = Path.GetRelativePath(_settingsService.DataRoot, rename.FullPath);
                    foreach (string key in _renamedBindings.Where(pair => pair.Value.Equals(oldPath, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
                        _renamedBindings[key] = newPath;
                    _renamedBindings[oldPath] = newPath;
                }
            }
            _debounceTimer.Change(500, Timeout.Infinite);
        }

        private void OnDebounceTimerFired(object state)
        {
            if (Volatile.Read(ref _executingOperation) != 0) { _debounceTimer.Change(500, Timeout.Infinite); return; }
            long generation;
            lock (_lock) generation = _watcherGeneration;
            string current = DiskStamp();
            lock (_lock)
            {
                if (current == _loadedDiskStamp && _renamedBindings.Count == 0 && generation == _watcherGeneration)
                { _externalRefreshPending = false; return; }
            }
            ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnCatalogChanged(object sender, CatalogChangedEventArgs args)
        {
            if (Volatile.Read(ref _executingOperation) == 0) ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EnsureCatalogSubscription()
        {
            if (!_catalogSubscribed && _settingsService.UsesStateV2)
            { _settingsService.Operations.CatalogChanged += OnCatalogChanged; _catalogSubscribed = true; }
        }

        private string DiskStamp()
        {
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                void Visit(string directory)
                {
                    if (!Directory.Exists(directory)) return;
                    foreach (string file in Directory.GetFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        SafePath.Resolve(_shortcutsFolder, Path.GetRelativePath(_shortcutsFolder, file));
                        var info = new FileInfo(file);
                        hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(_shortcutsFolder, file) + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks));
                    }
                    foreach (string child in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(_shortcutsFolder, child)));
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) Visit(child);
                    }
                }
                Visit(_shortcutsFolder);
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // ── Loading ─────────────────────────────────────────────────────────────

        private void LoadShortcuts()
        {
            lock (_lock)
            {
                string beforeStamp = DiskStamp();
                _shortcuts.Clear();
                _folders.Clear();

                try
                {
                    // Load the complete hierarchy so intermediate and empty folders
                    // remain visible in the tray menu.
                    if (Directory.Exists(_shortcutsFolder)) LoadFromDirectory(_shortcutsFolder, null);
                    if (_settingsService.UsesStateV2)
                    {
                        var snapshot = _settingsService.StateSnapshot;
                        var profile = snapshot.Profiles.Single(p => string.Equals(
                            SafePath.Resolve(_settingsService.DataRoot, p.RelativeRoot), _shortcutsFolder, StringComparison.OrdinalIgnoreCase));
                        var hiddenFolders = snapshot.Folders.Where(f => f.ProfileId == profile.Id && f.Suppressed).Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        _folders.RemoveAll(folder => hiddenFolders.Contains(folder));
                        foreach (var entry in snapshot.Shortcuts.Where(s => s.ProfileId == profile.Id && !s.Suppressed && !_shortcuts.Any(item => item.Id == s.Id)))
                        {
                            var metadata = _settingsService.GetMetadata(entry.Id);
                            _shortcuts.Add(new ShortcutItem { Id = entry.Id, ProfileId = entry.ProfileId, ShortcutFilePath = metadata.FileName,
                                Name = Path.GetFileNameWithoutExtension(entry.RelativePath), Category = Path.GetDirectoryName(entry.RelativePath),
                                LaunchCount = metadata.LaunchCount, LastUsed = metadata.LastUsed, SortOrder = metadata.SortOrder,
                                IsPinned = metadata.IsPinned, PinnedOrder = metadata.PinnedOrder, Status = ShortcutStatus.NeedsReview });
                        }
                    }

                    // Sort by SortOrder, then alphabetically by display name
                    _shortcuts.Sort((a, b) =>
                    {
                        int cmp = a.SortOrder.CompareTo(b.SortOrder);
                        return cmp != 0 ? cmp
                            : string.Compare(a.GetDisplayName(), b.GetDisplayName(), StringComparison.OrdinalIgnoreCase);
                    });
                    _folders.Sort(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ShortcutManager.LoadShortcuts: {ex.Message}");
                }
                _loadedDiskStamp = beforeStamp;
                if (_watchForChanges && DiskStamp() != beforeStamp) _debounceTimer.Change(500, Timeout.Infinite);
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
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    var shortcut = _shortcutReader(file);
                    if (shortcut == null && _settingsService.UsesStateV2)
                        shortcut = new ShortcutItem { Name = Path.GetFileNameWithoutExtension(file),
                            ShortcutFilePath = file, Status = ShortcutStatus.Invalid };
                    if (shortcut == null) continue;

                    // Apply persisted metadata
                    var meta = _settingsService.GetMetadata(file);
                    shortcut.Id = meta.Id;
                    shortcut.ProfileId = meta.ProfileId;
                    shortcut.IsPinned = meta.IsPinned;
                    shortcut.PinnedOrder = meta.PinnedOrder;
                    shortcut.LaunchCount = meta.LaunchCount;
                    shortcut.LastUsed = meta.LastUsed;
                    shortcut.SortOrder = meta.SortOrder;
                    shortcut.Category = category;

                    // Apply custom icon override
                    if (!string.IsNullOrEmpty(meta.CustomIconPath) && File.Exists(meta.CustomIconPath))
                    {
                        var customIcon = _customIconReader(meta.CustomIconPath, meta.CustomIconIndex);
                        if (customIcon != null)
                            shortcut.Icon = customIcon;
                    }

                    if (_settingsService.UsesStateV2)
                    {
                        if (_settingsService.IsSuppressed(shortcut.Id)) continue;
                        if (shortcut.Status != ShortcutStatus.Invalid)
                            shortcut.Status = IsUnavailableTarget(shortcut.TargetPath) ? ShortcutStatus.Unavailable :
                                ext == ".url" || shortcut.IsValid() ? ShortcutStatus.Ready : ShortcutStatus.MissingTarget;
                        if (_settingsService.NeedsReview(shortcut.Id)) shortcut.Status = ShortcutStatus.NeedsReview;
                    }
                    if (_settingsService.UsesStateV2 || shortcut.IsValid() || ext == ".url")
                        _shortcuts.Add(shortcut);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load shortcut {file}: {ex.Message}");
                }
            }

            foreach (string subDirectory in Directory.GetDirectories(directory))
            {
                string folderName = Path.GetFileName(subDirectory);
                string relativePath = string.IsNullOrEmpty(category)
                    ? folderName
                    : Path.Combine(category, folderName);

                _folders.Add(relativePath);

                // Directory junctions can point back into the shortcut tree. Show
                // them as folders, but do not recurse and risk an infinite loop.
                FileAttributes attributes = File.GetAttributes(subDirectory);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                    LoadFromDirectory(subDirectory, relativePath);
            }
        }

        private static bool IsUnavailableTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (target.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            try
            {
                string root = Path.GetPathRoot(target);
                return !string.IsNullOrEmpty(root) && !new DriveInfo(root).IsReady;
            }
            catch (IOException) { return true; }
            catch (ArgumentException) { return false; }
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

                string targetPath = link.TargetPath;
                if (string.IsNullOrEmpty(targetPath)) targetPath = WindowsShellLink.ReadDefinition(linkPath).Target;

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
                    TargetPath = targetPath,
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

        /// <summary>
        /// Returns relative folder paths, including empty and intermediate folders.
        /// </summary>
        public List<string> GetAllFolders()
        {
            lock (_lock)
                return new List<string>(_folders);
        }

        internal LauncherMenuSnapshot CaptureMenuSnapshot()
        {
            string activeRoot = ResolveShortcutsFolder(_settingsService.Settings.ActiveProfileName);
            if (!string.Equals(activeRoot, _shortcutsFolder, StringComparison.OrdinalIgnoreCase)) ReloadShortcuts();
            lock (_lock)
            {
                var state = _settingsService.StateSnapshot;
                var preferences = state?.Settings ?? _settingsService.Settings;
                var profile = state?.Profiles.Single(p => !p.Suppressed && p.Name.Equals(preferences.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
                var entries = state?.Shortcuts.ToDictionary(entry => entry.Id);
                var items = new List<ShortcutItem>();
                foreach (var source in _shortcuts)
                {
                    var item = source.CloneForMenu();
                    if (state != null)
                    {
                        if (!entries.TryGetValue(item.Id, out var entry) || entry.ProfileId != profile.Id || entry.Suppressed) continue;
                        item.IsPinned = entry.IsPinned; item.PinnedOrder = entry.PinnedOrder;
                        item.Aliases = entry.Aliases.ToArray();
                        item.SortOrder = entry.SortOrder; item.LaunchCount = entry.LaunchCount;
                        if (entry.LastUsedUtc.HasValue) item.LastUsed = entry.LastUsedUtc.Value.LocalDateTime;
                    }
                    items.Add(item);
                }
                var all = items.OrderBy(item => item.SortOrder).ThenBy(item => item.GetDisplayName(), StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToArray();
                var pins = all.Where(item => item.IsPinned).OrderBy(item => item.PinnedOrder)
                    .ThenBy(item => item.GetDisplayName(), StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ShortcutFilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id).ToArray();
                return new LauncherMenuSnapshot { Revision = state?.Revision ?? 0, ProfileId = profile?.Id ?? Guid.Empty,
                    ProfileName = profile?.Name ?? preferences.ActiveProfileName, IsV2 = state != null,
                    ShowMostUsed = preferences.ShowMostUsed, ShowRecent = preferences.ShowRecent,
                    AllShortcuts = all, FolderPaths = _folders.ToArray(), Pins = pins,
                    DirectPins = pins.Take(LauncherMenuSnapshot.DirectPinLimit).ToArray(),
                    MostUsed = preferences.ShowMostUsed ? all.Where(item => item.LaunchCount > 0).OrderByDescending(item => item.LaunchCount).ThenBy(item => item.GetDisplayName(), StringComparer.OrdinalIgnoreCase).Take(5).ToArray() : Array.Empty<ShortcutItem>(),
                    Recent = preferences.ShowRecent ? all.Where(item => item.LastUsed.HasValue).OrderByDescending(item => item.LastUsed).ThenBy(item => item.GetDisplayName(), StringComparer.OrdinalIgnoreCase).Take(5).ToArray() : Array.Empty<ShortcutItem>() };
            }
        }

        internal ShortcutItem ReadOrganizerShortcut(Guid id, LauncherState snapshot = null)
        {
            var state = snapshot ?? _settingsService.StateSnapshot;
            var entry = state?.Shortcuts.SingleOrDefault(item => item.Id == id && !item.Suppressed)
                ?? throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "organizer.item-unavailable"));
            var metadata = _settingsService.GetMetadata(id);
            var item = new ShortcutItem { Id = id, ProfileId = entry.ProfileId, Name = Path.GetFileNameWithoutExtension(entry.RelativePath),
                ShortcutFilePath = metadata.FileName, IsPinned = entry.IsPinned, PinnedOrder = entry.PinnedOrder,
                LaunchCount = entry.LaunchCount, LastUsed = metadata.LastUsed, SortOrder = entry.SortOrder };
            if (!File.Exists(metadata.FileName)) { item.Status = ShortcutStatus.NeedsReview; return item; }
            try
            {
                if (entry.Kind == ShortcutKind.Executable) item.TargetPath = metadata.FileName;
                else if (entry.Kind == ShortcutKind.Url)
                {
                    string url = File.ReadLines(metadata.FileName).FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?.Substring(4).Trim();
                    item.TargetPath = url;
                    item.Status = Uri.TryCreate(url, UriKind.Absolute, out _) ? ShortcutStatus.Ready : ShortcutStatus.Invalid;
                }
                else
                {
                    var parsed = _organizerReader(metadata.FileName);
                    if (parsed == null) item.Status = ShortcutStatus.Invalid;
                    else { item.TargetPath = parsed.TargetPath; item.Arguments = parsed.Arguments; item.WorkingDirectory = parsed.WorkingDirectory;
                        item.Status = IsUnavailableTarget(item.TargetPath) ? ShortcutStatus.Unavailable : parsed.IsValid() ? ShortcutStatus.Ready : ShortcutStatus.MissingTarget; }
                }
                if (_settingsService.NeedsReview(id)) item.Status = ShortcutStatus.NeedsReview;
            }
            catch (IOException) { item.Status = ShortcutStatus.Unavailable; }
            catch (UnauthorizedAccessException) { item.Status = ShortcutStatus.Unavailable; }
            return item;
        }

        private static ShortcutItem ReadLnkDefinitionOnly(string path)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Shell definitions require the STA UI thread.");
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic factory = shell;
                shortcut = factory.CreateShortcut(path);
                dynamic link = shortcut;
                string target = link.TargetPath;
                if (string.IsNullOrEmpty(target)) target = WindowsShellLink.ReadDefinition(path).Target;
                return new ShortcutItem { TargetPath = target, Arguments = link.Arguments,
                    WorkingDirectory = link.WorkingDirectory, Description = link.Description, ShortcutFilePath = path };
            }
            catch (COMException) { return null; }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }

        public void SetPinned(ShortcutItem shortcut, bool pinned)
        {
            var request = _settingsService.NewOperation(pinned ? ShortcutOperationKind.Pin : ShortcutOperationKind.Unpin);
            if (shortcut == null || shortcut.ProfileId != request.ProfileId)
                throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "favorites.profile-changed"));
            request.SelectedIds = new[] { shortcut.Id };
            ExecuteMutation(request);
        }

        public void ReorderPins(IReadOnlyList<Guid> orderedIds)
        {
            var request = _settingsService.NewOperation(ShortcutOperationKind.ReorderPins);
            request.SelectedIds = orderedIds.ToArray();
            for (int index = 0; index < orderedIds.Count; index++) request.Orders.Add(orderedIds[index], index);
            ExecuteMutation(request);
        }

        /// <summary>Must be called from an STA thread (COM required for .lnk).</summary>
        public void ReloadShortcuts()
        {
            EnsureCatalogSubscription();
            if (_settingsService.UsesStateV2)
            {
                string activeRoot = ResolveShortcutsFolder(_settingsService.Settings.ActiveProfileName);
                if (!string.Equals(activeRoot, _shortcutsFolder, StringComparison.OrdinalIgnoreCase))
                {
                    _shortcutsFolder = activeRoot;
                    _fileWatcher?.Dispose();
                    InitializeFileWatcher();
                }
                Dictionary<string, string> renames;
                bool refresh;
                long generation;
                lock (_lock)
                {
                    generation = _watcherGeneration;
                    refresh = _externalRefreshPending || DiskStamp() != _loadedDiskStamp;
                    renames = new Dictionary<string, string>(_renamedBindings, StringComparer.OrdinalIgnoreCase);
                }
                if (refresh && !_settingsService.StateLoad.ReadOnly)
                {
                    var request = _settingsService.NewOperation(ShortcutOperationKind.Reconcile);
                    request.RenamedBindings = renames;
                    ExecuteMutation(request, notify: false);
                    lock (_lock)
                    {
                        foreach (var pair in renames)
                            if (_renamedBindings.TryGetValue(pair.Key, out var current) && current == pair.Value)
                                _renamedBindings.Remove(pair.Key);
                        _externalRefreshPending = _watcherGeneration != generation || _renamedBindings.Count != 0;
                        if (_externalRefreshPending && _watchForChanges) _debounceTimer.Change(500, Timeout.Infinite);
                    }
                }
            }
            LoadShortcuts();
        }

        private OperationResult ExecuteMutation(OperationRequest request, bool notify = true)
        {
            Interlocked.Increment(ref _executingOperation);
            OperationResult result;
            try { result = _settingsService.ExecuteOperation(request); }
            finally { Interlocked.Decrement(ref _executingOperation); }
            if (!result.Success) throw new StatePersistenceException(result);
            LoadShortcuts();
            if (notify)
            {
                var snapshot = _settingsService.StateSnapshot;
                var bindings = snapshot.Shortcuts.Select(entry => Path.Combine(snapshot.Profiles.Single(p => p.Id == entry.ProfileId).RelativeRoot, entry.RelativePath))
                    .Concat(snapshot.Folders.Select(folder => Path.Combine(snapshot.Profiles.Single(p => p.Id == folder.ProfileId).RelativeRoot, folder.RelativePath)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                lock (_lock)
                {
                    foreach (var pair in _renamedBindings.ToArray())
                        if (!bindings.Any(path => path.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                            path.StartsWith(pair.Key + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) && bindings.Contains(pair.Value))
                            _renamedBindings.Remove(pair.Key);
                }
            }
            if (notify) ShortcutsChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }

        /// <summary>
        /// Adds a new shortcut (.exe or .lnk → creates a .lnk; .url → copies the file).
        /// </summary>
        public ShortcutItem AddShortcut(string targetPath, string customName = null)
        {
            _settingsService.RequireV2();
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
            if (string.IsNullOrWhiteSpace(shortcutName) || Path.GetFileName(shortcutName) != shortcutName)
                throw new ArgumentException("Shortcut name must be a nonempty file name.", nameof(customName));
            string addedPath = UniqueFilePath(_shortcutsFolder, shortcutName, ext == ".url" ? ".url" : ".lnk");
            SafePath.Resolve(_shortcutsFolder, Path.GetFileName(addedPath));
            var request = _settingsService.NewOperation(ShortcutOperationKind.Add);
            byte[] bytes = ext == ".url" || ext == ".lnk" ? File.ReadAllBytes(targetPath)
                : ShortcutDefinitionEditor.CreateLink(_settingsService.DataRoot, targetPath, shortcutName);
            request.AddItems.Add(new OperationAddItem { RelativePath = Path.GetFileName(addedPath),
                Kind = ext == ".url" ? ShortcutKind.Url : ShortcutKind.ShellLink, Bytes = bytes });
            ExecuteMutation(request);

            lock (_lock)
                return _shortcuts.FirstOrDefault(s => string.Equals(s.ShortcutFilePath, addedPath, StringComparison.OrdinalIgnoreCase));
        }

        private string UniqueFilePath(string folder, string name, string extension)
        {
            var snapshot = _settingsService.StateSnapshot;
            var reserved = snapshot == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : snapshot.Shortcuts
                .Select(entry => SafePath.Resolve(SafePath.Resolve(_settingsService.DataRoot, snapshot.Profiles.Single(profile => profile.Id == entry.ProfileId).RelativeRoot), entry.RelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(folder, name + extension);
            int counter = 1;
            while (File.Exists(path) || Directory.Exists(path) || reserved.Contains(path))
                path = Path.Combine(folder, $"{name} ({counter++}){extension}");
            return path;
        }

        /// <summary>Removes a shortcut's file from disk and from the in-memory list.</summary>
        public bool RemoveShortcut(ShortcutItem shortcut)
        {
            if (shortcut == null || string.IsNullOrEmpty(shortcut.ShortcutFilePath))
                return false;

            RemoveShortcuts(new[] { shortcut });
            return true;
        }

        public void RemoveShortcuts(IEnumerable<ShortcutItem> shortcuts)
        {
            var request = _settingsService.NewOperation(ShortcutOperationKind.Remove);
            request.SelectedIds = shortcuts.Select(s => s.Id).Distinct().ToArray();
            if (request.SelectedIds.Length != 0) ExecuteMutation(request);
        }

        /// <summary>Renames a shortcut file on disk (works for .lnk and .url).</summary>
        public bool RenameShortcut(ShortcutItem shortcut, string newName)
        {
            if (shortcut == null || string.IsNullOrEmpty(newName))
                return false;

            try
            {
                if (string.IsNullOrWhiteSpace(newName) || Path.GetFileName(newName) != newName)
                    throw new ArgumentException("Shortcut name must be a nonempty file name.", nameof(newName));
                string ext = Path.GetExtension(shortcut.ShortcutFilePath);
                string newPath = Path.Combine(
                    Path.GetDirectoryName(shortcut.ShortcutFilePath),
                    newName + ext);
                SafePath.Resolve(_shortcutsFolder, Path.GetRelativePath(_shortcutsFolder, newPath));

                if (File.Exists(newPath))
                    throw new InvalidOperationException("A shortcut with this name already exists.");

                var request = _settingsService.NewOperation(ShortcutOperationKind.Rename, shortcut.ProfileId);
                request.SelectedIds = new[] { shortcut.Id };
                request.Name = newName;
                ExecuteMutation(request);
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

            var meta = _settingsService.GetMetadata(shortcut);
            meta.LaunchCount = checked(meta.LaunchCount + 1);
            meta.LastUsed = DateTime.Now;
            _settingsService.SaveMetadata(meta);
            shortcut.LaunchCount = meta.LaunchCount;
            shortcut.LastUsed = meta.LastUsed;
        }

        /// <summary>Updates the sort order for a shortcut and persists it.</summary>
        public void UpdateSortOrder(ShortcutItem shortcut, int newOrder)
        {
            if (shortcut == null) return;

            if (_settingsService.UsesStateV2)
            {
                var group = GetAllShortcuts().Where(item => item.ProfileId == shortcut.ProfileId &&
                    string.Equals(item.Category ?? string.Empty, shortcut.Category ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToArray();
                UpdateSortOrders(group, new Dictionary<Guid, int> { [shortcut.Id] = newOrder });
                return;
            }

            var meta = _settingsService.GetMetadata(shortcut);
            meta.SortOrder = newOrder;
            _settingsService.UpsertMetadata(meta);
            _settingsService.Save();
            shortcut.SortOrder = newOrder;
        }

        public void UpdateSortOrders(IReadOnlyList<ShortcutItem> group, Dictionary<Guid, int> orders = null)
        {
            if (!_settingsService.UsesStateV2)
            {
                for (int index = 0; index < group.Count; index++)
                {
                    var metadata = _settingsService.GetMetadata(group[index]);
                    metadata.SortOrder = orders != null && orders.TryGetValue(group[index].Id, out int specified) ? specified : index * 10;
                    _settingsService.UpsertMetadata(metadata);
                }
                _settingsService.Save();
                return;
            }
            var request = _settingsService.NewOperation(ShortcutOperationKind.Reorder);
            request.SelectedIds = group.Select(item => item.Id).ToArray();
            var used = new HashSet<int>(orders?.Values ?? Enumerable.Empty<int>());
            int next = 0;
            foreach (var item in group)
            {
                if (orders != null && orders.TryGetValue(item.Id, out int specified)) request.Orders[item.Id] = specified;
                else
                {
                    int value = item.SortOrder;
                    if (!used.Add(value))
                    {
                        while (used.Contains(next)) next++;
                        value = next++;
                        used.Add(value);
                    }
                    request.Orders[item.Id] = value;
                }
            }
            ExecuteMutation(request);
        }

        public void RepairTarget(ShortcutItem shortcut, string newTarget, string expectedFingerprint = null)
        {
            var request = _settingsService.NewOperation(ShortcutOperationKind.Repair, shortcut.ProfileId);
            request.SelectedIds = new[] { shortcut.Id };
            string source = _settingsService.GetMetadata(shortcut.Id).FileName;
            string extension = Path.GetExtension(source);
            if (!extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Raw executable payloads cannot be repaired as shortcuts.");
            byte[] original = File.ReadAllBytes(source);
            string fingerprint = Convert.ToHexString(SHA256.HashData(original));
            if (expectedFingerprint != null && expectedFingerprint != fingerprint)
                throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "repair.changed-since-preview"));
            request.ExpectedFileFingerprints[shortcut.Id] = fingerprint;
            request.ReplacementBytes = extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                ? ShortcutDefinitionEditor.RetargetUrl(original, newTarget)
                : ShortcutDefinitionEditor.RetargetLink(_settingsService.DataRoot, original, newTarget);
            ExecuteMutation(request);
        }

        /// <summary>Saves a custom icon override for a shortcut.</summary>
        public void SetCustomIcon(ShortcutItem shortcut, string iconPath, int iconIndex)
        {
            if (shortcut == null) return;

            var meta = _settingsService.GetMetadata(shortcut);
            meta.CustomIconPath = iconPath;
            meta.CustomIconIndex = iconIndex;
            _settingsService.SaveMetadata(meta);

            // Update the in-memory icon immediately
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                shortcut.Icon = _customIconReader(iconPath, iconIndex);
        }

        public void Dispose()
        {
            if (_catalogSubscribed) { _settingsService.Operations.CatalogChanged -= OnCatalogChanged; _catalogSubscribed = false; }
            _debounceTimer?.Dispose();
            _fileWatcher?.Dispose();
        }
    }
}
