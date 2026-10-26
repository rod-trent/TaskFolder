using L = TaskFolder.Services.LocalizationService;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    internal sealed class BackupService
    {
        internal const int MaxEntries = 10000, MaxIconBytes = 16 * 1024 * 1024;
        internal const long MaxBytes = 256L * 1024 * 1024;
        private readonly SettingsService _settings;
        private readonly Func<string, byte[]> _rawLink;
        private readonly Func<string, (string Target, string Arguments, string WorkingDirectory)> _read;
        internal BackupService(SettingsService settings, Func<string, byte[]> rawLink = null, Func<string, (string, string, string)> read = null)
        {
            _settings = settings;
            _read = read ?? WindowsShellLink.ReadDefinition;
            _rawLink = rawLink ?? (target => { string path = SafePath.Resolve(settings.DataRoot, Path.Combine("Staging", Guid.NewGuid().ToString("N") + ".lnk"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)); WindowsShellLink.SaveDefinition(path, target, "", Path.GetDirectoryName(target)); return File.ReadAllBytes(path); });
        }
        internal BackupManifest Export(string path, CancellationToken cancellation = default)
        {
            _settings.Load(); if (_settings.StateSnapshot == null || _settings.StateLoad.ReadOnly) throw new InvalidDataException(L.Text("Writable v2 catalog required."));
            var state = _settings.StateSnapshot; var manifest = new BackupManifest { State = LauncherStateStore.Clone(state) };
            manifest.State.CommittedOperationIds.Clear();
            var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.State.Shortcuts.Where(e => !e.Suppressed && manifest.State.Profiles.Any(p => p.Id == e.ProfileId && !p.Suppressed)))
            {
                cancellation.ThrowIfCancellationRequested(); var original = state.Shortcuts.Single(e => e.Id == entry.Id); string source = _settings.GetMetadata(original.Id).FileName;
                if (!File.Exists(source)) { entry.Suppressed = true; manifest.Warnings.Add(L.Text("Missing definition: ") + entry.RelativePath); continue; }
                byte[] bytes;
                if (entry.Kind == ShortcutKind.Executable) { bytes = _rawLink(source); entry.Kind = ShortcutKind.ShellLink; entry.RelativePath = Path.ChangeExtension(entry.RelativePath, ".lnk"); manifest.Warnings.Add(L.Text("Raw executable exported as external link: ") + entry.RelativePath); }
                else bytes = ReadBounded(source, MaxBytes, cancellation);
                string archivePath = "links/" + entry.Id.ToString("N") + Path.GetExtension(entry.RelativePath);
                var definition = new BackupPayload { ArchivePath = archivePath, EntryId = entry.Id, Sha256 = Hash(bytes) };
                try { if (original.Kind == ShortcutKind.ShellLink) { var parsed = _read(source); definition.Target = parsed.Target; definition.Arguments = parsed.Arguments; definition.WorkingDirectory = parsed.WorkingDirectory; }
                    else if (original.Kind == ShortcutKind.Executable) definition.Target = source; else definition.Target = AddItemService.ReadUrl(bytes); }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is InvalidDataException || ex is IOException) { manifest.Warnings.Add(L.Text("Launch definition needs review: ") + entry.RelativePath); }
                payloads.Add(archivePath, bytes); manifest.Payloads.Add(definition);
                if (entry.CustomIcon == null) continue;
                string iconPath = entry.CustomIcon.Source == IconSource.ManagedAsset ? SafePath.Resolve(_settings.DataRoot, entry.CustomIcon.Path) : entry.CustomIcon.Path;
                try
                {
                    byte[] iconBytes; string extension = Path.GetExtension(iconPath).ToLowerInvariant();
                    if (extension == ".ico" || extension == ".png") iconBytes = ReadBounded(iconPath, MaxIconBytes, cancellation);
                    else { using var icon = IconExtractor.ExtractIcon(iconPath, entry.CustomIcon.Index); if (icon == null) throw new InvalidDataException(L.Text("Icon extraction unavailable.")); using var output = new MemoryStream(); icon.Save(output); iconBytes = output.ToArray(); extension = ".ico"; }
                    string asset = Path.Combine("Assets", "Icons", Hash(iconBytes).ToLowerInvariant() + extension); string archiveAsset = "icons/" + Path.GetFileName(asset);
                    if (!payloads.ContainsKey(archiveAsset)) { payloads.Add(archiveAsset, iconBytes); manifest.Payloads.Add(new BackupPayload { ArchivePath = archiveAsset, AssetPath = asset, Sha256 = Hash(iconBytes) }); }
                    entry.CustomIcon = new CustomIconReference { Source = IconSource.ManagedAsset, Path = asset, Index = 0 };
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException)
                { manifest.Warnings.Add(L.Text("External icon dependency retained: ") + entry.RelativePath); }
            }
            if (state.Shortcuts.Count + state.Folders.Count + state.Profiles.Count > MaxEntries || payloads.Values.Sum(bytes => (long)bytes.Length) > MaxBytes) throw new InvalidDataException(L.Text("Export exceeds archive limits."));
            new LauncherStateStore(_settings.DataRoot).Validate(manifest.State);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, LauncherStateStore.JsonOptions);
            if (manifestBytes.LongLength + payloads.Values.Sum(bytes => (long)bytes.Length) > MaxBytes) throw new InvalidDataException(L.Text("Export exceeds archive limits."));
            cancellation.ThrowIfCancellationRequested(); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            { WriteEntry(archive, "manifest.json", manifestBytes); foreach (var payload in payloads) { cancellation.ThrowIfCancellationRequested(); WriteEntry(archive, payload.Key, payload.Value); } }
            cancellation.ThrowIfCancellationRequested(); File.Move(temporary, path); return manifest;
        }

        internal ImportPreview Preview(string archivePath, ImportOptions options = null, CancellationToken cancellation = default)
        {
            options ??= new ImportOptions(); var preview = new ImportPreview();
            try
            {
                cancellation.ThrowIfCancellationRequested();
                using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length > MaxBytes) throw new InvalidDataException(L.Text("Compressed archive exceeds quota."));
                preview.ArchiveHash = Convert.ToHexString(SHA256.HashData(file)); file.Position = 0;
                using var archive = new ZipArchive(file, ZipArchiveMode.Read); if (archive.Entries.Count > MaxEntries + 1) throw new InvalidDataException(L.Text("Too many archive entries."));
                var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long total = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellation.ThrowIfCancellationRequested(); ValidateArchivePath(entry.FullName);
                    int mode = (entry.ExternalAttributes >> 16) & 0xf000;
                    if (mode == 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || mode != 0 && mode != 0x8000) throw new InvalidDataException(L.Text("Nonregular archive payload."));
                    if (entry.Length < 0 || entry.Length > MaxBytes - total || payloads.ContainsKey(entry.FullName)) throw new InvalidDataException("Duplicate/case-colliding path or quota exceeded.");
                    if (entry.FullName.StartsWith("icons/", StringComparison.OrdinalIgnoreCase) && entry.Length > MaxIconBytes) throw new InvalidDataException(L.Text("Icon quota exceeded."));
                    using var input = entry.Open(); using var output = new MemoryStream(); byte[] buffer = new byte[65536]; int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) != 0) { cancellation.ThrowIfCancellationRequested(); total += read; if (total > MaxBytes || output.Length + read > entry.Length) throw new InvalidDataException(L.Text("Streaming quota exceeded.")); output.Write(buffer, 0, read); }
                    if (output.Length != entry.Length) throw new InvalidDataException(L.Text("Truncated payload.")); payloads.Add(entry.FullName, output.ToArray());
                }
                preview.Bytes = total;
                if (!payloads.Remove("manifest.json", out var metadata) || metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException(L.Text("Missing or oversized manifest."));
                var manifest = JsonSerializer.Deserialize<BackupManifest>(metadata, LauncherStateStore.JsonOptions); preview.Manifest = manifest;
                if (manifest == null || manifest.SchemaVersion != 1 || manifest.State == null || manifest.Payloads == null || manifest.Warnings == null) throw new InvalidDataException(L.Text("Unsupported archive schema."));
                new LauncherStateStore(_settings.DataRoot).Validate(manifest.State);
                if (manifest.State.Profiles.Count + manifest.State.Folders.Count + manifest.State.Shortcuts.Count > MaxEntries) throw new InvalidDataException(L.Text("Catalog entry quota exceeded."));
                var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var payload in manifest.Payloads)
                {
                    if (payload == null || !declared.Add(payload.ArchivePath) || !payloads.TryGetValue(payload.ArchivePath, out var bytes) || payload.Sha256 != Hash(bytes)) throw new InvalidDataException("Payload checksum/manifest mismatch.");
                    if (payload.EntryId != Guid.Empty)
                    { var entry = manifest.State.Shortcuts.SingleOrDefault(e => e.Id == payload.EntryId && !e.Suppressed && e.Kind != ShortcutKind.Executable); if (entry == null || payload.AssetPath != null || payload.ArchivePath != "links/" + entry.Id.ToString("N") + Path.GetExtension(entry.RelativePath)) throw new InvalidDataException(L.Text("Invalid link payload binding.")); }
                    else if (payload.AssetPath == null || Path.GetDirectoryName(payload.AssetPath) != Path.Combine("Assets", "Icons") || payload.ArchivePath != "icons/" + Path.GetFileName(payload.AssetPath) || !new[] { ".ico", ".png" }.Contains(Path.GetExtension(payload.AssetPath).ToLowerInvariant())) throw new InvalidDataException(L.Text("Invalid icon asset binding."));
                }
                if (declared.Count != payloads.Count) throw new InvalidDataException(L.Text("Undeclared payload."));
                foreach (var entry in manifest.State.Shortcuts.Where(e => !e.Suppressed && e.CustomIcon?.Source == IconSource.ManagedAsset))
                    if (!manifest.Payloads.Any(p => p.AssetPath == entry.CustomIcon.Path)) throw new InvalidDataException(L.Text("Managed icon asset is missing."));
                preview.Warnings.AddRange(manifest.Warnings); preview.Warnings.Add(L.Text("Link targets, arguments and external dependencies are untrusted; nothing is launched. Installed programs and local Windows integration are not restored."));
                BuildImport(preview, payloads, options, cancellation);
            }
            catch (OperationCanceledException) { preview.Result = OperationResult.Fail(OperationStatus.Cancelled, "backup.cancelled"); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is OverflowException)
            { preview.Result = OperationResult.Fail(OperationStatus.ValidationError, "backup.invalid-archive"); preview.Result.ItemErrors.Add(ex.Message); }
            return preview;
        }

        private void BuildImport(ImportPreview preview, Dictionary<string, byte[]> payloads, ImportOptions options, CancellationToken cancellation)
        {
            _settings.Load(); if (_settings.StateSnapshot == null || _settings.StateLoad.ReadOnly) throw new InvalidDataException(L.Text("Writable v2 catalog required."));
            var before = _settings.StateSnapshot; var source = preview.Manifest.State;
            string optionKey = options.ProfilePrefix + ":" + options.MergeExisting + ":" + options.ApplyPresentationSettings + ":" + string.Join(",", options.SelectedProfiles?.OrderBy(id => id) ?? Enumerable.Empty<Guid>()) + ":" +
                string.Join(",", options.SelectedItems?.OrderBy(id => id) ?? Enumerable.Empty<Guid>()) + ":" + string.Join(",", options.Conflicts.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)) + ":" + string.Join(",", options.RenamedPaths.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)) + (options.CreateCopy ? options.CopyToken.ToString() : "");
            Guid operationId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(preview.ArchiveHash + optionKey)).Take(16).ToArray());
            if (before.CommittedOperationIds.Contains(operationId)) { preview.Result = new OperationResult { OperationId = operationId, Code = "backup.already-imported" }; return; }
            var after = LauncherStateStore.Clone(before); var request = new OperationRequest { OperationId = operationId, Kind = ShortcutOperationKind.ApplyImport,
                ProfileId = ProfileState.DefaultId, ExpectedRevision = before.Revision, ExpectedStateFingerprint = _settings.StateLoad.Fingerprint, Snapshot = after, ImportFiles = new List<ImportFile>(), ExpectedDirectoryFingerprints = new Dictionary<string, string>() };
            preview.Request = request;
            var profiles = new Dictionary<Guid, ProfileState>(); var identities = new Dictionary<Guid, Guid>();
            foreach (var original in source.Profiles.Where(p => !p.Suppressed && (options.SelectedProfiles == null || options.SelectedProfiles.Contains(p.Id))))
            {
                cancellation.ThrowIfCancellationRequested(); string name = options.ProfilePrefix + original.Name;
                var destination = after.Profiles.SingleOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (destination != null && (!options.MergeExisting || destination.Suppressed)) throw new InvalidDataException(L.Text("Profile exists: choose another prefix or explicitly merge."));
                if (destination == null) { destination = new ProfileState { Id = Guid.NewGuid(), Name = name, RelativeRoot = Path.Combine("Profiles", name) }; SafePath.Resolve(_settings.DataRoot, destination.RelativeRoot); after.Profiles.Add(destination); }
                profiles.Add(original.Id, destination); preview.Summary.Add(original.Name + " → " + destination.Name);
                request.ExpectedDirectoryFingerprints[destination.RelativeRoot] = _settings.Operations.DirectoryFingerprint(destination.RelativeRoot);
                foreach (var folder in source.Folders.Where(f => f.ProfileId == original.Id && !f.Suppressed).OrderBy(f => f.RelativePath.Length))
                    if (!after.Folders.Any(f => f.ProfileId == destination.Id && f.RelativePath.Equals(folder.RelativePath, StringComparison.OrdinalIgnoreCase)))
                        after.Folders.Add(new FolderState { Id = Guid.NewGuid(), ProfileId = destination.Id, RelativePath = folder.RelativePath, SortOrder = folder.SortOrder });
                foreach (var entry in source.Shortcuts.Where(e => e.ProfileId == original.Id && !e.Suppressed && (options.SelectedItems == null || options.SelectedItems.Contains(e.Id))).OrderBy(e => e.IsPinned ? e.PinnedOrder : int.MaxValue).ThenBy(e => e.SortOrder))
                {
                    var payload = preview.Manifest.Payloads.SingleOrDefault(p => p.EntryId == entry.Id) ?? throw new InvalidDataException(L.Text("Link definition is missing."));
                    string relative = options.RenamedPaths.TryGetValue(entry.Id, out string renamed) ? renamed : entry.RelativePath; SafePath.Resolve(_settings.DataRoot, relative);
                    var existing = after.Shortcuts.SingleOrDefault(e => e.ProfileId == destination.Id && e.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
                    var conflict = options.Conflicts.TryGetValue(entry.Id, out var choice) ? choice : ImportConflict.Skip;
                    if (existing != null && (conflict == ImportConflict.Skip || existing.Suppressed)) { preview.Skipped++; preview.Summary.Add("Skip: " + relative); continue; }
                    if (existing != null && conflict == ImportConflict.Rename) throw new InvalidDataException(L.Text("Enter an unused replacement path for this item."));
                    if (existing != null && existing.Kind != entry.Kind) throw new InvalidDataException(L.Text("Replacement must retain shortcut kind; raw EXE cannot be replaced."));
                    var imported = JsonSerializer.Deserialize<ShortcutEntry>(JsonSerializer.Serialize(entry)); imported.Id = existing?.Id ?? Guid.NewGuid(); imported.ProfileId = destination.Id; imported.RelativePath = relative;
                    if (imported.IsPinned && before.Profiles.Any(p => p.Id == destination.Id)) { var pins = after.Shortcuts.Where(e => e.ProfileId == destination.Id && !e.Suppressed && e.IsPinned).ToArray();
                        imported.PinnedOrder = existing?.IsPinned == true ? existing.PinnedOrder : pins.Length == 0 ? 0 : checked(pins.Max(p => p.PinnedOrder) + 1); }
                    if (existing == null) after.Shortcuts.Add(imported); else after.Shortcuts[after.Shortcuts.IndexOf(existing)] = imported;
                    identities[entry.Id] = imported.Id; preview.Imported++; request.ImportFiles.Add(new ImportFile { Path = Path.Combine(destination.RelativeRoot, relative), Bytes = payloads[payload.ArchivePath], Replace = existing != null });
                    preview.Summary.Add(relative + (existing == null ? " +" : " ↺"));
                }
            }
            if (profiles.Count == 0) throw new InvalidDataException(L.Text("Select at least one profile."));
            foreach (var asset in preview.Manifest.Payloads.Where(p => p.AssetPath != null))
            {
                var uses = after.Shortcuts.Where(e => identities.Values.Contains(e.Id) && e.CustomIcon?.Source == IconSource.ManagedAsset && e.CustomIcon.Path == asset.AssetPath).ToArray(); if (uses.Length == 0) continue;
                string relative = Path.Combine("Assets", "Icons", operationId.ToString("N") + "-" + Path.GetFileName(asset.AssetPath));
                foreach (var entry in uses) entry.CustomIcon.Path = relative;
                request.ImportFiles.Add(new ImportFile { Path = relative, Bytes = payloads[asset.ArchivePath] });
            }
            foreach (var ambiguity in source.LegacyAmbiguities)
            { var mapped = ambiguity.CandidateIds.Where(identities.ContainsKey).Select(id => identities[id]).ToArray(); if (mapped.Length != 0) after.LegacyAmbiguities.Add(new LegacyAmbiguity { OriginalRecord = ambiguity.OriginalRecord.Clone(), CandidateIds = mapped, ResolutionState = ambiguity.ResolutionState }); }
            if (options.ApplyPresentationSettings) { after.Settings.Language = source.Settings.Language; after.Settings.Theme = source.Settings.Theme; after.Settings.MenuFontSize = source.Settings.MenuFontSize; after.Settings.MenuItemHeight = source.Settings.MenuItemHeight; after.Settings.ShowNotifications = source.Settings.ShowNotifications; after.Settings.ShowRecent = source.Settings.ShowRecent; after.Settings.ShowMostUsed = source.Settings.ShowMostUsed; }
            preview.Result = _settings.Operations.Preview(request);
        }
        internal OperationResult Apply(ImportPreview preview) => preview?.Result.Success == true ? preview.Request == null ? preview.Result : _settings.ExecuteOperation(preview.Request) : preview?.Result ?? OperationResult.Fail(OperationStatus.ValidationError, "backup.preview-required");
        private static void ValidateArchivePath(string path)
        { SafePath.Resolve(Path.Combine(Path.GetTempPath(), "TaskFolder-archive-validation"), path); if (path.Contains('\\') || path != "manifest.json" && !path.StartsWith("links/", StringComparison.Ordinal) && !path.StartsWith("icons/", StringComparison.Ordinal)) throw new InvalidDataException(L.Text("Archive path is not whitelisted.")); }
        private static byte[] ReadBounded(string path, long max, CancellationToken cancellation)
        { using var input = File.OpenRead(path); if (input.Length > max) throw new InvalidDataException(L.Text("Payload exceeds quota.")); using var output = new MemoryStream(); byte[] buffer = new byte[65536]; int read; while ((read = input.Read(buffer, 0, buffer.Length)) != 0) { cancellation.ThrowIfCancellationRequested(); if (output.Length + read > max) throw new InvalidDataException(L.Text("Payload exceeds quota.")); output.Write(buffer, 0, read); } return output.ToArray(); }
        private static void WriteEntry(ZipArchive archive, string path, byte[] bytes) { using var output = archive.CreateEntry(path, CompressionLevel.Optimal).Open(); output.Write(bytes); }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    }
}
