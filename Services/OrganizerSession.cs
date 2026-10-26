using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    /// <summary>Window state and read-only previews; browsing never changes the launch profile.</summary>
    internal sealed class OrganizerSession
    {
        internal const string DragFormat = "TaskFolder.ShortcutSelection.v1";
        private readonly SettingsService _settings;
        private readonly ShortcutManager _manager;
        private readonly Dictionary<Guid, ShortcutStatus> _statuses = new Dictionary<Guid, ShortcutStatus>();
        private readonly Dictionary<string, OrganizerDragSelection> _drags = new Dictionary<string, OrganizerDragSelection>();
        private string _fingerprint;
        private Guid _folderId;
        internal LauncherState Snapshot { get; private set; }
        internal Guid ProfileId { get; private set; }
        internal string FolderPath { get; private set; } = string.Empty;
        internal string Query { get; set; } = string.Empty;
        internal bool AttentionOnly { get; set; }
        internal AliasDraft Draft { get; private set; }
        internal bool ReadOnly { get; private set; }

        internal OrganizerSession(SettingsService settings, ShortcutManager manager)
        { _settings = settings; _manager = manager; Refresh(); }

        internal OperationResult Refresh()
        {
            try
            {
                _settings.Load();
                Snapshot = _settings.StateSnapshot;
                ReadOnly = _settings.StateLoad.ReadOnly || Snapshot == null;
                _fingerprint = _settings.StateLoad.Fingerprint;
                _statuses.Clear();
                if (Snapshot == null) return OperationResult.Fail(OperationStatus.RecoveryRequired, "migration.required");
                if (!Snapshot.Profiles.Any(profile => profile.Id == ProfileId && !profile.Suppressed))
                {
                    ProfileId = Snapshot.Profiles.Single(profile => !profile.Suppressed && profile.Name.Equals(Snapshot.Settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase)).Id;
                    FolderPath = string.Empty;
                }
                if (_folderId != Guid.Empty)
                {
                    var folder = Snapshot.Folders.SingleOrDefault(item => item.Id == _folderId && !item.Suppressed && Snapshot.Profiles.Any(profile => profile.Id == item.ProfileId && !profile.Suppressed));
                    if (folder == null) { _folderId = Guid.Empty; FolderPath = string.Empty; }
                    else { ProfileId = folder.ProfileId; FolderPath = folder.RelativePath; }
                }
                return ReadOnly ? _settings.StateLoad.Result : new OperationResult();
            }
            catch (StatePersistenceException ex) { ReadOnly = true; return ex.Result; }
        }

        internal void Navigate(Guid profileId, string folder)
        {
            if (!Snapshot.Profiles.Any(profile => profile.Id == profileId && !profile.Suppressed) ||
                !string.IsNullOrEmpty(folder) && !Snapshot.Folders.Any(item => item.ProfileId == profileId && !item.Suppressed && Same(item.RelativePath, folder)))
                throw new InvalidDataException("The selected location no longer exists.");
            ProfileId = profileId; FolderPath = folder ?? string.Empty;
            _folderId = FolderPath.Length == 0 ? Guid.Empty : Snapshot.Folders.Single(item => item.ProfileId == profileId && !item.Suppressed && Same(item.RelativePath, FolderPath)).Id;
        }

        internal IReadOnlyList<OrganizerRow> Rows()
        {
            if (Snapshot == null) return Array.Empty<OrganizerRow>();
            bool MatchPath(string path) => Query.Length == 0 ? Same(Path.GetDirectoryName(path), FolderPath)
                : FolderPath.Length == 0 || path.StartsWith(FolderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            bool Contains(string text) => text?.Contains(Query, StringComparison.OrdinalIgnoreCase) == true;
            var rows = Snapshot.Folders.Where(folder => folder.ProfileId == ProfileId && !folder.Suppressed && MatchPath(folder.RelativePath) &&
                    !AttentionOnly && (Query.Length == 0 || Contains(folder.RelativePath)))
                .Select(folder => new OrganizerRow { Id = folder.Id, ProfileId = ProfileId, RelativePath = folder.RelativePath,
                    Name = Path.GetFileName(folder.RelativePath), IsFolder = true }).ToList();
            foreach (var entry in Snapshot.Shortcuts.Where(entry => entry.ProfileId == ProfileId && !entry.Suppressed && MatchPath(entry.RelativePath)))
            {
                if (Query.Length != 0 && !Contains(entry.RelativePath) && !entry.Aliases.Any(Contains)) continue;
                if (!_statuses.TryGetValue(entry.Id, out var status))
                {
                    status = _manager.ReadOrganizerShortcut(entry.Id, Snapshot).Status;
                    _statuses[entry.Id] = status;
                }
                if (AttentionOnly && status == ShortcutStatus.Ready) continue;
                rows.Add(new OrganizerRow { Id = entry.Id, ProfileId = entry.ProfileId, Name = Path.GetFileNameWithoutExtension(entry.RelativePath),
                    RelativePath = entry.RelativePath, Kind = entry.Kind, Status = status, IsPinned = entry.IsPinned });
            }
            return rows.OrderByDescending(row => row.IsFolder).ThenBy(row => row.IsFolder ? int.MinValue : Snapshot.Shortcuts.Single(entry => entry.Id == row.Id).SortOrder)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Id).ToArray();
        }

        internal OrganizerPreview Prepare(ShortcutOperationKind kind, Guid[] ids, Guid destinationProfile = default, string destinationFolder = "", string name = null, Guid sourceProfile = default)
        {
            var request = Request(kind, sourceProfile == Guid.Empty ? ProfileId : sourceProfile);
            request.SelectedIds = ids ?? Array.Empty<Guid>(); request.DestinationProfileId = destinationProfile;
            request.DestinationFolder = destinationFolder ?? string.Empty; request.Name = name;
            return Preview(request, protectDirectories: kind == ShortcutOperationKind.Move || kind == ShortcutOperationKind.Copy || kind == ShortcutOperationKind.Rename || kind == ShortcutOperationKind.Remove);
        }

        internal OrganizerPreview Reorder(Guid[] selected, Guid? beforeId = null, int direction = 0)
        {
            var request = Request(ShortcutOperationKind.Reorder, ProfileId);
            if (Query.Length != 0 || AttentionOnly) return Invalid(request, "organizer.reorder-clear-filters");
            var entries = Rows().Where(row => !row.IsFolder).Select(row => row.Id).ToList();
            var chosen = selected.ToHashSet();
            if (chosen.Count == 0 || !chosen.IsSubsetOf(entries)) return Invalid(request, "organizer.invalid-reorder-selection");
            if (direction != 0)
            {
                if (direction < 0)
                { for (int index = 1; index < entries.Count; index++) if (chosen.Contains(entries[index]) && !chosen.Contains(entries[index - 1]))
                    (entries[index], entries[index - 1]) = (entries[index - 1], entries[index]); }
                else
                { for (int index = entries.Count - 2; index >= 0; index--) if (chosen.Contains(entries[index]) && !chosen.Contains(entries[index + 1]))
                    (entries[index], entries[index + 1]) = (entries[index + 1], entries[index]); }
            }
            else
            {
                if (beforeId.HasValue && chosen.Contains(beforeId.Value)) return Invalid(request, "organizer.reorder-on-selection");
                var moved = entries.Where(chosen.Contains).ToArray(); entries.RemoveAll(chosen.Contains);
                int position = beforeId.HasValue ? entries.IndexOf(beforeId.Value) : entries.Count;
                if (position < 0) return Invalid(request, "organizer.invalid-drop-target");
                entries.InsertRange(position, moved);
            }
            request.SelectedIds = entries.ToArray();
            for (int index = 0; index < entries.Count; index++) request.Orders[entries[index]] = index * 10;
            return Preview(request, protectDirectories: true);
        }

        internal OrganizerPreview ExternalLinks(string[] paths, Guid destinationProfile, string folder)
        {
            var request = Request(ShortcutOperationKind.Add, destinationProfile); request.DestinationFolder = folder;
            try
            {
                if (paths == null || paths.Length == 0) return Invalid(request, "organizer.empty-drop");
                foreach (string path in paths)
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension != ".lnk" && extension != ".url") return Invalid(request, "organizer.external-links-only");
                    byte[] bytes = File.ReadAllBytes(path);
                    request.AddItems.Add(new OperationAddItem { RelativePath = Path.GetFileName(path),
                        Kind = extension == ".lnk" ? ShortcutKind.ShellLink : ShortcutKind.Url, Bytes = bytes });
                }
                var preview = Preview(request, protectDirectories: true);
                preview.Items = request.AddItems.Select(item => item.RelativePath).ToArray(); return preview;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { return Invalid(request, "organizer.drop-read-failed"); }
        }

        internal OperationResult Commit(OrganizerPreview preview)
        {
            if (preview == null || !preview.Result.Success) return preview?.Result ?? OperationResult.Fail(OperationStatus.ValidationError, "organizer.invalid-preview");
            var result = _settings.ExecuteOperation(preview.Request);
            if (result.Success) Refresh();
            return result;
        }

        internal OperationResult Reconcile(System.Collections.Generic.Dictionary<string, string> renames = null)
        {
            var refreshed = Refresh(); if (!refreshed.Success) return refreshed;
            var request = Request(ShortcutOperationKind.Reconcile, ProfileId);
            if (renames != null) request.RenamedBindings = new Dictionary<string, string>(renames, StringComparer.OrdinalIgnoreCase);
            var result = _settings.ExecuteOperation(request);
            if (result.Success) Refresh();
            return result;
        }

        internal void EditAliases(Guid id)
        {
            if (Draft?.Dirty == true) return;
            var entry = Snapshot.Shortcuts.Single(item => item.Id == id && !item.Suppressed);
            Draft = new AliasDraft { Id = id, Baseline = entry.Aliases.ToArray(), Text = string.Join(Environment.NewLine, entry.Aliases) };
        }
        internal void SetAliasText(string text) { if (Draft != null) { Draft.Text = text; Draft.Dirty = !Aliases(text).SequenceEqual(Draft.Baseline); } }
        internal void DiscardAliases() { Draft = null; }
        internal OperationResult SaveAliases()
        {
            if (Draft == null) return OperationResult.Fail(OperationStatus.ValidationError, "organizer.no-alias-draft");
            var result = Refresh(); if (!result.Success) return result;
            var target = Snapshot.Shortcuts.SingleOrDefault(entry => entry.Id == Draft.Id && !entry.Suppressed && Snapshot.Profiles.Any(profile => profile.Id == entry.ProfileId && !profile.Suppressed));
            if (target == null || !target.Aliases.SequenceEqual(Draft.Baseline)) return OperationResult.Fail(OperationStatus.Conflict, "organizer.aliases-changed");
            var request = Request(ShortcutOperationKind.SaveSnapshot, target.ProfileId);
            request.Snapshot = LauncherStateStore.Clone(Snapshot);
            request.Snapshot.Shortcuts.Single(entry => entry.Id == Draft.Id).Aliases = Aliases(Draft.Text);
            var saved = _settings.ExecuteOperation(request);
            if (saved.Success) { Draft = null; Refresh(); }
            return saved;
        }

        internal OrganizerDragSelection StartDrag(Guid[] ids)
        {
            if (ids.Length == 0) throw new InvalidDataException("Select items first.");
            var payload = new OrganizerDragSelection { Token = Guid.NewGuid().ToString("N"), Revision = Snapshot.Revision, ProfileId = ProfileId, Ids = ids.ToArray() };
            _drags[payload.Token] = payload; return payload;
        }
        internal OrganizerDragSelection FindDrag(string token) => token != null && _drags.TryGetValue(token, out var payload) && payload.Revision == Snapshot.Revision ? payload : null;
        internal void EndDrag(string token) { _drags.Remove(token); }
        internal string FullPath(Guid id) => _settings.GetMetadata(id).FileName;

        private OrganizerPreview Preview(OperationRequest request, bool protectDirectories)
        {
            try
            {
                if (ReadOnly) return Invalid(request, "organizer.read-only");
                var ids = request.SelectedIds.ToHashSet();
                var roots = Snapshot.Folders.Where(folder => ids.Contains(folder.Id)).ToArray();
                foreach (var entry in Snapshot.Shortcuts.Where(entry => !entry.Suppressed && entry.Kind != ShortcutKind.Executable &&
                    (ids.Contains(entry.Id) || roots.Any(folder => folder.ProfileId == entry.ProfileId && entry.RelativePath.StartsWith(folder.RelativePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))))
                {
                    string path = FullPath(entry.Id);
                    if (File.Exists(path)) { using var input = File.OpenRead(path); request.ExpectedFileFingerprints[entry.Id] = Convert.ToHexString(SHA256.HashData(input)); }
                }
                if (protectDirectories)
                {
                    request.ExpectedDirectoryFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var profileId in new[] { request.ProfileId, request.DestinationProfileId }.Where(id => id != Guid.Empty).Distinct())
                    {
                        string relative = Snapshot.Profiles.Single(profile => profile.Id == profileId).RelativeRoot;
                        request.ExpectedDirectoryFingerprints[relative] = _settings.Operations.DirectoryFingerprint(relative);
                    }
                }
                return new OrganizerPreview { Request = request, Result = _settings.Operations.Preview(request),
                    Items = request.SelectedIds.Select(id => Snapshot.Shortcuts.FirstOrDefault(entry => entry.Id == id)?.RelativePath
                        ?? Snapshot.Folders.FirstOrDefault(folder => folder.Id == id)?.RelativePath ?? id.ToString()).ToArray() };
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            { return Invalid(request, "organizer.preview-failed"); }
        }
        private OperationRequest Request(ShortcutOperationKind kind, Guid profileId) => new OperationRequest { Kind = kind, ProfileId = profileId,
            ExpectedRevision = Snapshot?.Revision ?? 0, ExpectedStateFingerprint = _fingerprint };
        private static OrganizerPreview Invalid(OperationRequest request, string code) => new OrganizerPreview { Request = request, Result = OperationResult.Fail(OperationStatus.ValidationError, code) };
        private static string[] Aliases(string text) => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(alias => alias.Trim()).Where(alias => alias.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        private static bool Same(string left, string right) => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
