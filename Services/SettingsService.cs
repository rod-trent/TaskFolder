using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    /// <summary>
    /// Persistence facade: existing v2 state takes precedence; legacy data is never migrated implicitly.
    /// </summary>
    public class SettingsService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string _dataRoot;
        private readonly string _settingsPath;
        private readonly string _metadataPath;

        private Dictionary<string, ShortcutMetadata> _metadata;
        private readonly LauncherStateStore _stateStore;
        private LauncherState _state;
        private readonly Dictionary<Guid, ShortcutEntry> _entries = new Dictionary<Guid, ShortcutEntry>();
        private readonly Dictionary<string, Guid> _bindings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        private bool _legacyLoadFailed;
        private ShortcutOperationService _operations;
        public ShortcutOperationService Operations
        {
            get
            {
                if (_operations == null)
                {
                    _operations = new ShortcutOperationService(_dataRoot);
                    _operations.CatalogChanged += (_, _) => { Load(); SettingsChanged?.Invoke(this, EventArgs.Empty); };
                }
                return _operations;
            }
        }
        public bool UsesStateV2 => _state != null;
        public long Revision => _state?.Revision ?? 0;
        public StateLoadResult StateLoad { get; private set; }
        public LauncherState StateSnapshot => _state == null ? null : LauncherStateStore.Clone(_state);

        public AppSettings Settings { get; private set; } = new AppSettings();

        public event EventHandler SettingsChanged;

        /// <summary>
        /// Constructs the service. Pass isPortable=true (from --portable CLI arg) to
        /// store all data next to the executable instead of in %APPDATA%.
        /// </summary>
        public SettingsService(bool isPortable = false)
            : this(isPortable ? AppContext.BaseDirectory : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFolder"),
                isPortable)
        {
        }

        /// <summary>
        /// Uses an explicit data root, allowing isolated fixtures without opening the user's data.
        /// IsPortable describes the instance; it never overrides this explicit location.
        /// </summary>
        public SettingsService(string dataRoot, bool isPortable = false)
        {
            if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
                throw new ArgumentException("Data root must be an absolute directory path.", nameof(dataRoot));

            IsPortable = isPortable;
            _dataRoot = Path.GetFullPath(dataRoot);

            Directory.CreateDirectory(_dataRoot);

            _settingsPath = Path.Combine(_dataRoot, "settings.json");
            _metadataPath = Path.Combine(_dataRoot, "metadata.json");
            _metadata = new Dictionary<string, ShortcutMetadata>(StringComparer.OrdinalIgnoreCase);
            _stateStore = new LauncherStateStore(_dataRoot);

            Load();
        }

        /// <summary>The root data directory (everything lives under here).</summary>
        public string DataRoot => _dataRoot;

        public bool IsPortable { get; }

        // ── Load ────────────────────────────────────────────────────────────────

        public void Load()
        {
            StateLoad = _stateStore.Load();
            if (StateLoad.State != null)
            {
                // Widgets retain this object. Adopt values without invalidating their settings reference.
                AdoptSettings(StateLoad.State.Settings);
                _state = StateLoad.State;
                _state.Settings = Settings;
                IndexState();
                var pending = Operations.InspectPending();
                if (!pending.Success) { StateLoad.ReadOnly = true; StateLoad.Result = pending; }
                return;
            }
            if (!StateLoad.Result.Success) throw new StatePersistenceException(StateLoad.Result);
            _state = null;
            _legacyLoadFailed = false;
            // settings.json
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    AdoptSettings(JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SettingsService: failed to load settings.json: {ex.Message}");
                    AdoptSettings(new AppSettings());
                    _legacyLoadFailed = true;
                }
            }

            // metadata.json
            if (File.Exists(_metadataPath))
            {
                try
                {
                    string json = File.ReadAllText(_metadataPath);
                    var list = JsonSerializer.Deserialize<List<ShortcutMetadata>>(json, _jsonOptions);
                    _metadata.Clear();
                    if (list != null)
                    {
                        foreach (var m in list)
                        {
                            if (!string.IsNullOrEmpty(m.FileName))
                                _metadata[m.FileName] = m;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SettingsService: failed to load metadata.json: {ex.Message}");
                    _metadata.Clear();
                    _legacyLoadFailed = true;
                }
            }
        }

        // ── Save ────────────────────────────────────────────────────────────────

        public void Save()
        {
            var result = TrySave();
            if (!result.Success) throw new StatePersistenceException(result);
        }

        public OperationResult TrySave()
        {
            if (_state != null)
            {
                if (StateLoad.ReadOnly) return StateLoad.Result;
                var request = NewOperation(ShortcutOperationKind.SaveSnapshot);
                request.Snapshot = StateSnapshot;
                var result = Operations.Execute(request);
                if (!result.Success)
                {
                    if (result.NewRevision.HasValue)
                    { StateLoad.ReadOnly = true; StateLoad.Result = result; }
                    return result;
                }
                return result;
            }
            if (_legacyLoadFailed) return OperationResult.Fail(OperationStatus.RecoveryRequired, "legacy.invalid-read-only");
            try
            {
                AtomicWrite(_settingsPath, JsonSerializer.Serialize(Settings, _jsonOptions));
                AtomicWrite(_metadataPath, JsonSerializer.Serialize(new List<ShortcutMetadata>(_metadata.Values), _jsonOptions));
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                return new OperationResult();
            }
            catch (UnauthorizedAccessException) { return OperationResult.Fail(OperationStatus.AccessDenied, "legacy.access-denied"); }
            catch (IOException) { return OperationResult.Fail(OperationStatus.IoFailure, "legacy.write-failed"); }
        }

        private static void AtomicWrite(string path, string json)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }

        // ── Metadata helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns existing metadata for a shortcut file, or a fresh default if not found.
        /// </summary>
        public ShortcutMetadata GetMetadata(string fileName)
        {
            if (_state != null)
            {
                if (string.IsNullOrWhiteSpace(fileName) || !Path.IsPathFullyQualified(fileName))
                    throw new ArgumentException("V2 metadata requires an ID or an absolute binding.", nameof(fileName));
                string fullPath = Path.GetFullPath(fileName);
                if (!_bindings.TryGetValue(fullPath, out var id))
                    throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "state.unbound-shortcut"));
                return GetMetadata(id);
            }
            if (string.IsNullOrEmpty(fileName))
                return new ShortcutMetadata();

            string key = Path.GetFileName(fileName); // strip any directory part
            if (_metadata.TryGetValue(key, out var m))
                return m;

            return new ShortcutMetadata { FileName = key };
        }

        /// <summary>
        /// Upserts metadata for a shortcut file (in memory only; call Save() to persist).
        /// </summary>
        public void UpsertMetadata(ShortcutMetadata meta)
        {
            if (_state != null)
            {
                if (meta == null) throw new ArgumentNullException(nameof(meta));
                if (!_entries.TryGetValue(meta.Id, out var entry))
                    throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "state.unknown-shortcut-id"));
                entry.LaunchCount = meta.LaunchCount;
                entry.SortOrder = meta.SortOrder;
                // Do not reinterpret a legacy local timestamp merely because an icon/order changed.
                var previous = GetMetadata(entry.Id).LastUsed;
                if (meta.LastUsed != previous)
                    entry.LastUsedUtc = meta.LastUsed.HasValue ? new DateTimeOffset(meta.LastUsed.Value).ToUniversalTime() : null;
                string previousIcon = entry.CustomIcon == null ? null : entry.CustomIcon.Source == IconSource.ManagedAsset
                    ? SafePath.Resolve(_dataRoot, entry.CustomIcon.Path) : entry.CustomIcon.Path;
                if (!string.Equals(previousIcon, meta.CustomIconPath, StringComparison.OrdinalIgnoreCase))
                    entry.CustomIcon = string.IsNullOrEmpty(meta.CustomIconPath) ? null : new CustomIconReference
                    { Source = IconSource.ExternalReference, Path = meta.CustomIconPath, Index = meta.CustomIconIndex };
                if (entry.CustomIcon != null) entry.CustomIcon.Index = meta.CustomIconIndex;
                return;
            }
            if (meta == null || string.IsNullOrEmpty(meta.FileName))
                return;

            string key = Path.GetFileName(meta.FileName);
            meta.FileName = key;
            _metadata[key] = meta;
        }

        /// <summary>
        /// Convenience: upsert + save immediately.
        /// </summary>
        public void SaveMetadata(ShortcutMetadata meta)
        {
            UpsertMetadata(meta);
            Save();
        }

        /// <summary>
        /// Removes metadata for a deleted shortcut.
        /// </summary>
        public void RemoveMetadata(string fileName)
        {
            if (_state != null)
            {
                RemoveMetadata(GetMetadata(fileName).Id);
                return;
            }
            if (string.IsNullOrEmpty(fileName)) return;
            string key = Path.GetFileName(fileName);
            if (_metadata.Remove(key))
                Save();
        }

        public ShortcutMetadata GetMetadata(ShortcutItem shortcut) => _state != null
            ? GetMetadata(shortcut.Id) : GetMetadata(shortcut.ShortcutFilePath);

        public ShortcutMetadata GetMetadata(Guid id)
        {
            if (_state == null || !_entries.TryGetValue(id, out var entry))
                throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "state.unknown-shortcut-id"));
            DateTime? lastUsed = entry.LastUsedUtc?.LocalDateTime;
            if (!lastUsed.HasValue && entry.LegacyRecord.HasValue && entry.LegacyRecord.Value.ValueKind == JsonValueKind.Object &&
                entry.LegacyRecord.Value.TryGetProperty("LastUsed", out var original) && original.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(original.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var legacyDate)) lastUsed = legacyDate;
            string icon = entry.CustomIcon == null ? null : entry.CustomIcon.Source == IconSource.ManagedAsset
                ? SafePath.Resolve(_dataRoot, entry.CustomIcon.Path) : entry.CustomIcon.Path;
            return new ShortcutMetadata { Id = entry.Id, ProfileId = entry.ProfileId, FileName = EntryPath(entry),
                IsPinned = entry.IsPinned, PinnedOrder = entry.PinnedOrder,
                LaunchCount = entry.LaunchCount, LastUsed = lastUsed, SortOrder = entry.SortOrder,
                CustomIconPath = icon, CustomIconIndex = entry.CustomIcon?.Index ?? 0 };
        }

        private string EntryPath(ShortcutEntry entry)
        {
            var profile = _state.Profiles.Single(p => p.Id == entry.ProfileId);
            return SafePath.Resolve(SafePath.Resolve(_dataRoot, profile.RelativeRoot), entry.RelativePath);
        }

        private void IndexState()
        {
            _entries.Clear();
            _bindings.Clear();
            foreach (var entry in _state.Shortcuts)
            { _entries.Add(entry.Id, entry); _bindings.Add(EntryPath(entry), entry.Id); }
        }

        private void AdoptSettings(AppSettings source)
        {
            foreach (var property in typeof(AppSettings).GetProperties())
                if (property.CanWrite) property.SetValue(Settings, property.GetValue(source));
        }

        internal bool IsSuppressed(Guid id) => _entries.TryGetValue(id, out var entry) && entry.Suppressed;
        internal bool NeedsReview(Guid id) => _state.LegacyAmbiguities.Any(a =>
            a.ResolutionState == "Unresolved" && a.CandidateIds.Contains(id));

        internal OperationRequest NewOperation(ShortcutOperationKind kind, Guid profileId = default)
        {
            RequireV2();
            if (StateLoad.ReadOnly) throw new StatePersistenceException(StateLoad.Result);
            return new OperationRequest { Kind = kind, ExpectedRevision = Revision,
                ExpectedStateFingerprint = StateLoad.Fingerprint, ProfileId = profileId == Guid.Empty
                    ? _state.Profiles.Single(p => p.Name.Equals(Settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase)).Id : profileId };
        }

        internal OperationResult ExecuteOperation(OperationRequest request)
        {
            RequireV2();
            if (StateLoad.ReadOnly) return StateLoad.Result;
            return Operations.Execute(request);
        }

        internal void RequireV2()
        {
            if (!UsesStateV2) throw new StatePersistenceException(OperationResult.Fail(OperationStatus.RecoveryRequired, "migration.required"));
        }

        internal string ResolveProfileRoot(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Default";
            if (_state != null)
            {
                var profile = _state.Profiles.SingleOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (profile == null) throw new StatePersistenceException(OperationResult.Fail(OperationStatus.Conflict, "state.unknown-profile"));
                return SafePath.Resolve(_dataRoot, profile.RelativeRoot);
            }
            return SafePath.Resolve(_dataRoot, name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                ? "Shortcuts" : Path.Combine("Profiles", name));
        }

        internal void RegisterProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A new profile needs a valid non-default leaf name.", nameof(name));
            string relative = Path.Combine("Profiles", name);
            SafePath.Resolve(_dataRoot, relative);
            RequireV2();
            var request = NewOperation(ShortcutOperationKind.CreateProfile);
            request.Name = name;
            var result = ExecuteOperation(request);
            if (!result.Success) throw new StatePersistenceException(result);
        }

        public void RemoveMetadata(Guid id)
        {
            var request = NewOperation(ShortcutOperationKind.Remove);
            request.SelectedIds = new[] { id };
            var result = ExecuteOperation(request);
            if (!result.Success) throw new StatePersistenceException(result);
        }

    }
}
