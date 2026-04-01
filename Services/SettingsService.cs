using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskFolder.Models;

namespace TaskFolder.Services
{
    /// <summary>
    /// Central service for reading/writing settings.json and metadata.json.
    /// All other services get their persistence needs through this class.
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

        public AppSettings Settings { get; private set; } = new AppSettings();

        public event EventHandler SettingsChanged;

        /// <summary>
        /// Constructs the service. Pass isPortable=true (from --portable CLI arg) to
        /// store all data next to the executable instead of in %APPDATA%.
        /// </summary>
        public SettingsService(bool isPortable = false)
        {
            if (isPortable)
            {
                _dataRoot = AppContext.BaseDirectory;
            }
            else
            {
                _dataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskFolder");
            }

            Directory.CreateDirectory(_dataRoot);

            _settingsPath = Path.Combine(_dataRoot, "settings.json");
            _metadataPath = Path.Combine(_dataRoot, "metadata.json");
            _metadata = new Dictionary<string, ShortcutMetadata>(StringComparer.OrdinalIgnoreCase);

            Load();
        }

        /// <summary>The root data directory (everything lives under here).</summary>
        public string DataRoot => _dataRoot;

        // ── Load ────────────────────────────────────────────────────────────────

        public void Load()
        {
            // settings.json
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                               ?? new AppSettings();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SettingsService: failed to load settings.json: {ex.Message}");
                    Settings = new AppSettings();
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
                }
            }
        }

        // ── Save ────────────────────────────────────────────────────────────────

        public void Save()
        {
            AtomicWrite(_settingsPath, JsonSerializer.Serialize(Settings, _jsonOptions));
            AtomicWrite(_metadataPath, JsonSerializer.Serialize(
                new List<ShortcutMetadata>(_metadata.Values), _jsonOptions));

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void AtomicWrite(string path, string json)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService: AtomicWrite failed for {path}: {ex.Message}");
            }
        }

        // ── Metadata helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns existing metadata for a shortcut file, or a fresh default if not found.
        /// </summary>
        public ShortcutMetadata GetMetadata(string fileName)
        {
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
            if (string.IsNullOrEmpty(fileName)) return;
            string key = Path.GetFileName(fileName);
            if (_metadata.Remove(key))
                Save();
        }
    }
}
