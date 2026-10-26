using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    public sealed class MigrationPreview
    {
        public LauncherState State { get; set; }
        public OperationResult Result { get; set; }
        public bool AlreadyMigrated { get; set; }
        public Dictionary<string, string> SourceFingerprints { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public long AssignedLaunchCount { get; set; }
        public long UnresolvedLaunchCount { get; set; }
        public string SnapshotPath { get; set; }
        public long SnapshotBytes { get; set; }
        public string RecoveryRoute => "Stop TaskFolder; restore the migration snapshot only before further file operations. Keep raw executables in place.";
        internal List<string> SnapshotFiles { get; set; } = new List<string>();
        internal string CandidateFingerprint { get; set; }
        internal string DataRoot { get; set; }
    }

    /// <summary>Read-only inventory first. Commit is explicit and must follow approval of the preview.</summary>
    public sealed class StateMigrationService
    {
        private readonly string _root;
        private readonly LauncherStateStore _store;
        public StateMigrationService(string dataRoot)
        {
            _store = new LauncherStateStore(dataRoot);
            _root = Path.GetFullPath(dataRoot);
        }

        public MigrationPreview Preview()
        {
            var preview = new MigrationPreview { DataRoot = _root,
                SnapshotPath = Path.Combine(_root, "Migration", Guid.NewGuid().ToString("N")), Result = new OperationResult() };
            try
            {
                var existing = _store.Load();
                if (!existing.Result.Success) { preview.Result = existing.Result; return preview; }
                if (existing.State != null)
                {
                    preview.State = existing.State;
                    preview.AlreadyMigrated = true;
                    preview.CandidateFingerprint = Fingerprint(JsonSerializer.SerializeToUtf8Bytes(existing.State));
                    return preview;
                }
                var state = new LauncherState();
                preview.State = state;
                var legacySettings = ReadLegacy("settings.json", preview);
                bool legacy = legacySettings.HasValue;
                if (legacySettings.HasValue)
                {
                    state.LegacySettings = legacySettings;
                    state.Settings = legacySettings.Value.Deserialize<AppSettings>() ?? throw new InvalidDataException("Empty settings.");
                    if (!legacySettings.Value.TryGetProperty("Language", out _)) state.Settings.Language = "English";
                    if (!legacySettings.Value.TryGetProperty("Theme", out _)) state.Settings.Theme = "Light";
                }
                state.Profiles.Add(new ProfileState { Id = ProfileState.DefaultId, Name = "Default", RelativeRoot = "Shortcuts" });
                string profilesRoot = SafePath.Resolve(_root, "Profiles");
                if (Directory.Exists(profilesRoot))
                    foreach (string directory in Directory.GetDirectories(profilesRoot).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        string name = Path.GetFileName(directory);
                        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("A named Default profile conflicts with the legacy Default root.");
                        SafePath.Resolve(_root, Path.Combine("Profiles", name));
                        state.Profiles.Add(new ProfileState { Id = Guid.NewGuid(), Name = name, RelativeRoot = Path.Combine("Profiles", name) });
                    }
                var directories = new List<string>();
                foreach (var profile in state.Profiles)
                {
                    string root = SafePath.Resolve(_root, profile.RelativeRoot);
                    if (Directory.Exists(root)) Inventory(root, root, profile.Id, preview, directories);
                }
                directories.Sort(StringComparer.OrdinalIgnoreCase);
                preview.SourceFingerprints["@directories"] = Fingerprint(JsonSerializer.SerializeToUtf8Bytes(directories));
                var metadata = ReadLegacy("metadata.json", preview);
                legacy |= metadata.HasValue || state.Shortcuts.Count != 0 || directories.Count != 0;
                if (legacy && !legacySettings.HasValue)
                { state.Settings.Language = "English"; state.Settings.Theme = "Light"; }
                if (metadata.HasValue)
                {
                    if (metadata.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Legacy metadata is not an array.");
                    var records = metadata.Value.EnumerateArray().Select(r => r.Clone()).ToArray();
                    var names = records.Select(LegacyName).ToArray();
                    for (int index = 0; index < records.Length; index++)
                    {
                        var record = records[index];
                        string name = names[index];
                        long count = LegacyCounter(record);
                        var matches = state.Shortcuts.Where(s => string.Equals(Path.GetFileName(s.RelativePath), name,
                            StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (!string.IsNullOrEmpty(name) && matches.Length == 1 &&
                            names.Count(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) == 1)
                        {
                            var entry = matches[0];
                            entry.LegacyRecord = record;
                            entry.LaunchCount = count;
                            if (record.TryGetProperty("SortOrder", out var order)) entry.SortOrder = order.GetInt32();
                            if (record.TryGetProperty("LastUsed", out var last) && last.ValueKind == JsonValueKind.String)
                            {
                                string original = last.GetString();
                                bool zoned = original.EndsWith('Z') || original.Length > 6 &&
                                    (original[^6] == '+' || original[^6] == '-') && original[^3] == ':';
                                if (zoned && DateTimeOffset.TryParse(original, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                                    entry.LastUsedUtc = timestamp.ToUniversalTime();
                            }
                            if (record.TryGetProperty("CustomIconPath", out var icon) && icon.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(icon.GetString()))
                            {
                                string iconPath = icon.GetString();
                                if (!Path.IsPathFullyQualified(iconPath)) iconPath = Path.GetFullPath(Path.Combine(_root, iconPath));
                                entry.CustomIcon = new CustomIconReference { Source = IconSource.ExternalReference, Path = iconPath,
                                    Index = record.TryGetProperty("CustomIconIndex", out var iconIndex) ? iconIndex.GetInt32() : 0 };
                                IncludeManagedImage(iconPath, preview);
                            }
                            preview.AssignedLaunchCount = checked(preview.AssignedLaunchCount + count);
                        }
                        else
                        {
                            state.LegacyAmbiguities.Add(new LegacyAmbiguity { OriginalRecord = record,
                                CandidateIds = matches.Select(s => s.Id).ToArray() });
                            preview.UnresolvedLaunchCount = checked(preview.UnresolvedLaunchCount + count);
                        }
                    }
                    long total = records.Aggregate(0L, (sum, record) => checked(sum + LegacyCounter(record)));
                    if (total != checked(preview.AssignedLaunchCount + preview.UnresolvedLaunchCount))
                        throw new InvalidDataException("Legacy counter reconciliation failed.");
                }
                IncludeManagedImage(state.Settings.MenuHeaderLogoPath, preview);
                _store.Validate(state);
                preview.CandidateFingerprint = Fingerprint(JsonSerializer.SerializeToUtf8Bytes(state));
            }
            catch (UnauthorizedAccessException) { preview.Result = OperationResult.Fail(OperationStatus.AccessDenied, "migration.access-denied"); }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException || ex is ArgumentException ||
                ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
            { preview.Result = OperationResult.Fail(OperationStatus.ValidationError, "migration.invalid-legacy-data"); }
            catch (IOException) { preview.Result = OperationResult.Fail(OperationStatus.IoFailure, "migration.inventory-failed"); }
            return preview;
        }

        private JsonElement? ReadLegacy(string relative, MigrationPreview preview)
        {
            string path = SafePath.Resolve(_root, relative);
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            preview.SourceFingerprints[relative] = Fingerprint(bytes);
            AddSnapshotFile(relative, bytes.LongLength, preview);
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }

        private void Inventory(string root, string directory, Guid profileId, MigrationPreview preview, List<string> directories)
        {
            directories.Add(Path.GetRelativePath(_root, directory));
            foreach (string file in Directory.GetFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".lnk" && extension != ".url" && extension != ".exe") continue;
                string relative = Path.GetRelativePath(_root, file);
                SafePath.Resolve(_root, relative);
                using (var stream = File.OpenRead(file))
                {
                    preview.SourceFingerprints[relative] = Convert.ToHexString(SHA256.HashData(stream));
                    if (extension != ".exe") AddSnapshotFile(relative, stream.Length, preview);
                }
                preview.State.Shortcuts.Add(new ShortcutEntry { Id = Guid.NewGuid(), ProfileId = profileId,
                    RelativePath = Path.GetRelativePath(root, file), Kind = extension == ".lnk" ? ShortcutKind.ShellLink :
                        extension == ".url" ? ShortcutKind.Url : ShortcutKind.Executable });
            }
            foreach (string child in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(root, child);
                SafePath.Resolve(root, relative);
                preview.State.Folders.Add(new FolderState { Id = Guid.NewGuid(), ProfileId = profileId, RelativePath = relative });
                Inventory(root, child, profileId, preview, directories);
            }
        }

        private static string LegacyName(JsonElement record) => record.ValueKind == JsonValueKind.Object &&
            record.TryGetProperty("FileName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;

        private static long LegacyCounter(JsonElement record)
        {
            if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty("LaunchCount", out var counter)) return 0;
            long value = counter.GetInt64();
            if (value < 0) throw new InvalidDataException("Negative legacy counter.");
            return value;
        }

        private void IncludeManagedImage(string path, MigrationPreview preview)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
            string rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full) ||
                !new[] { ".ico", ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(full).ToLowerInvariant())) return;
            string relative = Path.GetRelativePath(_root, full);
            SafePath.Resolve(_root, relative);
            byte[] bytes = File.ReadAllBytes(full);
            preview.SourceFingerprints[relative] = Fingerprint(bytes);
            AddSnapshotFile(relative, bytes.LongLength, preview);
        }

        private static void AddSnapshotFile(string relative, long size, MigrationPreview preview)
        {
            if (preview.SnapshotFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) return;
            preview.SnapshotFiles.Add(relative);
            preview.SnapshotBytes = checked(preview.SnapshotBytes + size);
        }

        private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        /// <summary>Call only after approval of this exact preview. S1 invokes this on fixtures only.</summary>
        public OperationResult Commit(MigrationPreview preview)
        {
            if (preview == null || !preview.Result.Success || preview.DataRoot != _root || preview.State == null ||
                preview.CandidateFingerprint != Fingerprint(JsonSerializer.SerializeToUtf8Bytes(preview.State)))
                return OperationResult.Fail(OperationStatus.ValidationError, "migration.invalid-preview");
            if (preview.AlreadyMigrated)
            {
                var current = _store.Load();
                if (!current.Result.Success) return current.Result;
                return current.State != null && preview.CandidateFingerprint == Fingerprint(JsonSerializer.SerializeToUtf8Bytes(current.State))
                    ? new OperationResult { NewRevision = current.State.Revision }
                    : OperationResult.Fail(OperationStatus.Conflict, "migration.changed-since-preview");
            }
            try
            {
                if (!MatchesSources(preview)) return OperationResult.Fail(OperationStatus.Conflict, "migration.changed-since-preview");
                string snapshotRelative = Path.GetRelativePath(_root, preview.SnapshotPath);
                if (!snapshotRelative.StartsWith("Migration" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return OperationResult.Fail(OperationStatus.ValidationError, "migration.invalid-snapshot-path");
                string snapshot = SafePath.Resolve(_root, snapshotRelative);
                if (Directory.Exists(snapshot)) return OperationResult.Fail(OperationStatus.Conflict, "migration.snapshot-already-exists");
                Directory.CreateDirectory(snapshot);
                foreach (string relative in preview.SnapshotFiles)
                {
                    string destination = SafePath.Resolve(snapshot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    byte[] bytes = File.ReadAllBytes(SafePath.Resolve(_root, relative));
                    if (Fingerprint(bytes) != preview.SourceFingerprints[relative])
                        return OperationResult.Fail(OperationStatus.Conflict, "migration.changed-during-snapshot");
                    using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    file.Write(bytes);
                    file.Flush(true);
                }
                using (var manifest = new FileStream(Path.Combine(snapshot, "manifest.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(manifest, new { SchemaVersion = 1, preview.SourceFingerprints,
                        CopiedFiles = preview.SnapshotFiles, preview.SnapshotBytes, preview.RecoveryRoute }, LauncherStateStore.JsonOptions);
                    manifest.Flush(true);
                }
                if (!MatchesSources(preview)) return OperationResult.Fail(OperationStatus.Conflict, "migration.changed-during-snapshot");
                return _store.Commit(preview.State, 0);
            }
            catch (UnauthorizedAccessException) { return OperationResult.Fail(OperationStatus.AccessDenied, "migration.access-denied"); }
            catch (IOException) { return OperationResult.Fail(OperationStatus.IoFailure, "migration.snapshot-failed"); }
            catch (ArgumentException) { return OperationResult.Fail(OperationStatus.ValidationError, "migration.invalid-path"); }
        }

        private bool MatchesSources(MigrationPreview original)
        {
            var current = Preview();
            return current.Result.Success && !current.AlreadyMigrated &&
                original.SourceFingerprints.Count == current.SourceFingerprints.Count &&
                original.SourceFingerprints.All(pair => current.SourceFingerprints.TryGetValue(pair.Key, out var hash) && hash == pair.Value);
        }
    }
}
