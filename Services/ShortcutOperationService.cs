using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    internal enum OperationPhase { Prepared, FileApplied, FilesApplied, StateCommitted, Completed, RolledBack, StatePersisted, PurgePrepared, BeforeFileApply }

    /// <summary>Coordinates managed file deltas and a single revisioned catalog commit.</summary>
    public sealed class ShortcutOperationService
    {
        private readonly string _root;
        private readonly LauncherStateStore _store;
        private readonly Action<OperationPhase> _fault;
        private bool _busy;
        public bool IsBusy => _busy;
        public event EventHandler<CatalogChangedEventArgs> CatalogChanged;

        public ShortcutOperationService(string dataRoot) : this(dataRoot, null) { }
        internal ShortcutOperationService(string dataRoot, Action<OperationPhase> fault)
        {
            _store = new LauncherStateStore(dataRoot);
            _root = Path.GetFullPath(dataRoot);
            _fault = fault;
        }

        private sealed class FileDelta
        {
            [JsonRequired] public string Path { get; set; }
            public bool Directory { get; set; }
            public bool BeforeExists { get; set; }
            public bool AfterExists { get; set; }
            public string BeforeHash { get; set; }
            public string AfterHash { get; set; }
            public string BeforePayload { get; set; }
            public string AfterPayload { get; set; }
            public string CapturedBefore { get; set; }
            public string CapturedRollback { get; set; }
            public bool Applied { get; set; }
            public bool PreservedDirectory { get; set; }
            [JsonIgnore] public byte[] BeforeBytes { get; set; }
            [JsonIgnore] public byte[] AfterBytes { get; set; }
        }

        private sealed class Journal
        {
            [JsonRequired] public int SchemaVersion { get; set; } = 1;
            [JsonRequired] public Guid OperationId { get; set; }
            [JsonRequired] public string RequestFingerprint { get; set; }
            [JsonRequired] public ShortcutOperationKind Kind { get; set; }
            [JsonRequired] public OperationPhase Phase { get; set; }
            [JsonRequired] public LauncherState Before { get; set; }
            [JsonRequired] public LauncherState After { get; set; }
            [JsonRequired] public string BeforeFingerprint { get; set; }
            [JsonRequired] public List<FileDelta> Files { get; set; } = new List<FileDelta>();
            public Guid ProfileId { get; set; }
            public Guid RestoreOperationId { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public Guid[] ChangedIds { get; set; } = Array.Empty<Guid>();
            public string[] AffectedPaths { get; set; } = Array.Empty<string>();
            public bool Purged { get; set; }
            [JsonRequired] public string IntegrityHash { get; set; }
        }

        private sealed class OperationConflict : Exception
        {
            public OperationConflict(string message) : base(message) { }
        }

        /// <summary>Read-only validation for manager confirmation; Execute rechecks the same request.</summary>
        public OperationResult Preview(OperationRequest request)
        {
            if (request == null || request.OperationId == Guid.Empty) return Fail(OperationStatus.ValidationError, "operation.invalid-request", Guid.Empty);
            if (request.Cancelled) return Fail(OperationStatus.Cancelled, "operation.cancelled", request.OperationId);
            try
            {
                var pending = InspectPending();
                if (!pending.Success) return pending;
                var loaded = _store.Load();
                if (!loaded.Result.Success || loaded.ReadOnly) return loaded.Result;
                if (loaded.State == null) return Fail(OperationStatus.ValidationError, "operation.v2-required", request.OperationId);
                if (loaded.State.Revision != request.ExpectedRevision || request.ExpectedStateFingerprint != null && request.ExpectedStateFingerprint != loaded.Fingerprint)
                    return Fail(OperationStatus.Conflict, "operation.stale-revision", request.OperationId);
                var journal = Build(request, loaded.State, loaded.Fingerprint);
                _store.Validate(journal.After);
                foreach (var delta in journal.Files) VerifyBefore(delta);
                return new OperationResult { OperationId = request.OperationId, NewRevision = loaded.State.Revision };
            }
            catch (OperationConflict ex) { return Fail(OperationStatus.Conflict, ex.Message, request.OperationId); }
            catch (UnauthorizedAccessException) { return Fail(OperationStatus.AccessDenied, "operation.access-denied", request.OperationId); }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException || ex is JsonException || ex is OverflowException)
            { return Fail(OperationStatus.ValidationError, "operation.invalid-data", request.OperationId); }
            catch (IOException) { return Fail(OperationStatus.IoFailure, "operation.preview-read-failed", request.OperationId); }
        }

        internal string DirectoryFingerprint(string relative)
        {
            string root = Resolve(relative);
            if (File.Exists(root)) throw new InvalidDataException("A directory binding is a file.");
            if (!Directory.Exists(root)) return "Missing";
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Visit(string directory)
            {
                foreach (var path in Directory.GetFileSystemEntries(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string safe = Resolve(Path.GetRelativePath(_root, path));
                    bool folder = Directory.Exists(safe);
                    string item = Path.GetRelativePath(root, safe) + (folder ? "/" : ":" + new FileInfo(safe).Length + ":" + File.GetLastWriteTimeUtc(safe).Ticks);
                    hash.AppendData(System.Text.Encoding.UTF8.GetBytes(item + "\n"));
                    if (folder) Visit(safe);
                    else if (new[] { ".lnk", ".url" }.Contains(Path.GetExtension(safe).ToLowerInvariant()))
                    { using var input = File.OpenRead(safe); hash.AppendData(SHA256.HashData(input)); }
                }
            }
            Visit(root);
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public OperationResult Execute(OperationRequest request)
        {
            if (request == null || request.OperationId == Guid.Empty)
                return Fail(OperationStatus.ValidationError, "operation.invalid-request", request?.OperationId ?? Guid.Empty);
            if (request.Cancelled) return Fail(OperationStatus.Cancelled, "operation.cancelled", request.OperationId);
            Journal journal = null;
            OperationResult result;
            try
            {
                _busy = true;
                using (var lease = _store.AcquireWriterLease())
                {
                    string requestHash = Hash(JsonSerializer.SerializeToUtf8Bytes(request));
                    string journalPath = JournalPath(request.OperationId);
                    if (File.Exists(journalPath))
                    {
                        journal = ReadJournal(request.OperationId);
                        if (journal.RequestFingerprint != requestHash)
                            return Fail(OperationStatus.Conflict, "operation.id-reused-for-different-request", request.OperationId);
                        if (journal.Phase == OperationPhase.Completed)
                        {
                            var replayState = _store.Load();
                            if (!replayState.Result.Success) return WithId(replayState.Result, request.OperationId);
                            if (!replayState.State.CommittedOperationIds.Contains(request.OperationId))
                                return Fail(OperationStatus.RecoveryRequired, "operation.marker-missing", request.OperationId);
                            return Success(journal);
                        }
                        return Fail(OperationStatus.RecoveryRequired, "operation.pending-or-rolled-back", request.OperationId);
                    }
                    var pending = InspectPending();
                    if (!pending.Success) return WithId(pending, request.OperationId);
                    var loaded = _store.Load();
                    if (!loaded.Result.Success || loaded.ReadOnly) return WithId(loaded.Result, request.OperationId);
                    if (loaded.State == null) return Fail(OperationStatus.ValidationError, "operation.v2-required", request.OperationId);
                    if (loaded.State.Revision != request.ExpectedRevision || request.ExpectedStateFingerprint != null &&
                        request.ExpectedStateFingerprint != loaded.Fingerprint)
                        return Fail(OperationStatus.Conflict, "operation.stale-revision", request.OperationId);
                    if (loaded.State.CommittedOperationIds.Contains(request.OperationId))
                        return Fail(OperationStatus.RecoveryRequired, "operation.journal-missing", request.OperationId);
                    journal = Build(request, loaded.State, loaded.Fingerprint);
                    _store.Validate(journal.After);
                    if (request.Kind == ShortcutOperationKind.Reconcile && Same(journal.Before, journal.After))
                        return new OperationResult { OperationId = request.OperationId, NewRevision = loaded.State.Revision };
                    // All paths, collision rules and source fingerprints are checked before staging.
                    foreach (var delta in journal.Files) VerifyBefore(delta);
                    Stage(journal);
                    journal.Phase = OperationPhase.Prepared;
                    WriteJournal(journal);
                    _fault?.Invoke(OperationPhase.Prepared);
                    foreach (var delta in journal.Files)
                    {
                        if (request.Cancelled) return CancelPrepared(journal);
                        Apply(delta, journal.OperationId);
                        delta.Applied = true;
                        WriteJournal(journal);
                        _fault?.Invoke(OperationPhase.FileApplied);
                    }
                    journal.Phase = OperationPhase.FilesApplied;
                    WriteJournal(journal);
                    _fault?.Invoke(OperationPhase.FilesApplied);
                    if (request.Cancelled) return CancelPrepared(journal);
                    var candidate = LauncherStateStore.Clone(journal.After);
                    candidate.CommittedOperationIds.Add(journal.OperationId);
                    var commit = _store.CommitWithLease(candidate, journal.Before.Revision, journal.BeforeFingerprint, lease);
                    if (!commit.Success) return WithId(commit, request.OperationId);
                    _fault?.Invoke(OperationPhase.StatePersisted);
                    journal.After = _store.Load().State ?? throw new IOException("Committed state could not be read.");
                    journal.Phase = OperationPhase.StateCommitted;
                    WriteJournal(journal);
                    _fault?.Invoke(OperationPhase.StateCommitted);
                    journal.Phase = OperationPhase.Completed;
                    WriteJournal(journal);
                    _fault?.Invoke(OperationPhase.Completed);
                    result = Success(journal);
                }
                Publish(journal);
                return result;
            }
            catch (OperationConflict ex) { return Fail(OperationStatus.Conflict, ex.Message, request.OperationId); }
            catch (UnauthorizedAccessException) { return Fail(OperationStatus.AccessDenied, "operation.access-denied", request.OperationId); }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException || ex is ArgumentException ||
                ex is InvalidOperationException || ex is OverflowException)
            { return Fail(journal == null ? OperationStatus.ValidationError : OperationStatus.RecoveryRequired, "operation.invalid-data", request.OperationId); }
            catch (IOException) { return Fail(OperationStatus.IoFailure, "operation.io-failed-recovery-may-be-required", request.OperationId); }
            finally { _busy = false; }
        }

        private Journal Build(OperationRequest request, LauncherState current, string fingerprint)
        {
            if (!Enum.IsDefined(request.Kind) || request.SelectedIds == null || request.SelectedIds.Distinct().Count() != request.SelectedIds.Length)
                throw new InvalidDataException("Invalid selection.");
            var journal = new Journal { OperationId = request.OperationId, Kind = request.Kind,
                RequestFingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(request)), CreatedUtc = DateTimeOffset.UtcNow,
                Before = LauncherStateStore.Clone(current), After = LauncherStateStore.Clone(current),
                BeforeFingerprint = fingerprint, ProfileId = request.ProfileId, RestoreOperationId = request.RestoreOperationId };
            switch (request.Kind)
            {
                case ShortcutOperationKind.Rename:
                case ShortcutOperationKind.Move:
                case ShortcutOperationKind.Copy: BuildMove(journal, request); break;
                case ShortcutOperationKind.Remove: BuildRemove(journal, request.SelectedIds, request.ProfileId); break;
                case ShortcutOperationKind.Reorder: BuildReorder(journal, request); break;
                case ShortcutOperationKind.Pin:
                case ShortcutOperationKind.Unpin:
                case ShortcutOperationKind.ReorderPins: BuildPins(journal, request); break;
                case ShortcutOperationKind.CreateFolder:
                    var folderProfile = ActiveProfile(journal.After, request.ProfileId);
                    string folderPath = Join(request.DestinationFolder, Leaf(request.Name));
                    AssertDestinationUnused(journal, folderProfile.Id, folderPath);
                    EnsureFolder(journal, folderProfile, folderPath); break;
                case ShortcutOperationKind.Add: BuildAdd(journal, request); break;
                case ShortcutOperationKind.Repair: BuildRepair(journal, request); break;
                case ShortcutOperationKind.CreateProfile: BuildCreateProfile(journal, request.Name); break;
                case ShortcutOperationKind.DeleteProfile: BuildDeleteProfile(journal, request.ProfileId); break;
                case ShortcutOperationKind.Restore: BuildRestore(journal, request); break;
                case ShortcutOperationKind.SaveSnapshot: BuildSnapshot(journal, request.Snapshot); break;
                case ShortcutOperationKind.Reconcile: BuildReconcile(journal, request.RenamedBindings); break;
                case ShortcutOperationKind.ApplyImport: BuildImport(journal, request); break;
                default: throw new InvalidDataException("Unknown operation.");
            }
            journal.ChangedIds = ChangedIds(journal.Before, journal.After);
            journal.AffectedPaths = journal.Files.Select(delta => delta.Path)
                .Concat(journal.Before.Shortcuts.Where(entry => journal.ChangedIds.Contains(entry.Id)).Select(entry =>
                    DataRelative(journal.Before.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath)))
                .Concat(journal.After.Shortcuts.Where(entry => journal.ChangedIds.Contains(entry.Id)).Select(entry =>
                    DataRelative(journal.After.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var pair in request.ExpectedFileFingerprints ?? new Dictionary<Guid, string>())
            {
                var entry = journal.Before.Shortcuts.SingleOrDefault(s => s.Id == pair.Key)
                    ?? throw new InvalidDataException("Unknown fingerprint ID.");
                string path = EntryPath(journal.Before, entry);
                if (!File.Exists(path) || HashFile(path) != pair.Value) throw new OperationConflict("operation.source-changed-since-preview");
            }
            foreach (var pair in request.ExpectedDirectoryFingerprints ?? new Dictionary<string, string>())
                if (DirectoryFingerprint(pair.Key) != pair.Value) throw new OperationConflict("operation.directory-changed-since-preview");
            return journal;
        }

        private void BuildMove(Journal journal, OperationRequest request)
        {
            ActiveProfile(journal.After, request.ProfileId);
            bool copy = request.Kind == ShortcutOperationKind.Copy;
            if (request.SelectedIds.Length == 0 || request.Kind == ShortcutOperationKind.Rename && request.SelectedIds.Length != 1)
                throw new InvalidDataException("Selection is required.");
            var destinationProfile = ActiveProfile(journal.After, request.Kind == ShortcutOperationKind.Rename
                ? request.ProfileId : request.DestinationProfileId == Guid.Empty ? request.ProfileId : request.DestinationProfileId);
            var roots = request.SelectedIds.Select(id => SelectionRoot(journal.Before, id, request.ProfileId)).ToArray();
            if (roots.Any(a => roots.Any(b => a.Id != b.Id && Under(a.Relative, b.Relative))))
                throw new InvalidDataException("Nested selections must not overlap.");
            foreach (var selected in roots)
            {
                string parent = request.Kind == ShortcutOperationKind.Rename ? Path.GetDirectoryName(selected.Relative) : request.DestinationFolder;
                string destination = Join(parent, request.Kind == ShortcutOperationKind.Rename ? Leaf(request.Name) +
                    (selected.Folder ? "" : Path.GetExtension(selected.Relative)) : Path.GetFileName(selected.Relative));
                if (selected.Folder && request.ProfileId == destinationProfile.Id &&
                    (SamePath(selected.Relative, destination) || Under(destination, selected.Relative)))
                    throw new InvalidDataException("A folder cannot move into itself.");
                // Tombstones retain their historical bindings for separate recovery; their files may be absent.
                var entries = journal.Before.Shortcuts.Where(s => s.ProfileId == request.ProfileId && !s.Suppressed &&
                    (selected.Folder ? Under(s.RelativePath, selected.Relative) : s.Id == selected.Id)).ToArray();
                if (entries.Any(s => s.Kind == ShortcutKind.Executable)) throw new InvalidDataException("Raw executable payloads cannot be moved or copied.");
                var folders = selected.Folder ? journal.Before.Folders.Where(f => f.ProfileId == request.ProfileId && !f.Suppressed &&
                    (SamePath(f.RelativePath, selected.Relative) || Under(f.RelativePath, selected.Relative)))
                    .OrderBy(f => f.RelativePath.Length).ToArray() : Array.Empty<FolderState>();
                foreach (var folder in folders)
                {
                    string target = ReplacePrefix(folder.RelativePath, selected.Relative, destination);
                    EnsureFolder(journal, destinationProfile, Path.GetDirectoryName(target));
                    AssertDestinationUnused(journal, destinationProfile.Id, target);
                    AddDirectory(journal, DataRelative(destinationProfile, target), true);
                    if (copy)
                    {
                        var clone = Clone(folder); clone.Id = Guid.NewGuid(); clone.ProfileId = destinationProfile.Id; clone.RelativePath = target;
                        journal.After.Folders.Add(clone);
                    }
                    else
                    {
                        var changed = journal.After.Folders.Single(f => f.Id == folder.Id);
                        changed.ProfileId = destinationProfile.Id; changed.RelativePath = target;
                    }
                }
                foreach (var entry in entries)
                {
                    string target = selected.Folder ? ReplacePrefix(entry.RelativePath, selected.Relative, destination) : destination;
                    EnsureFolder(journal, destinationProfile, Path.GetDirectoryName(target));
                    AssertDestinationUnused(journal, destinationProfile.Id, target);
                    string source = DataRelative(journal.Before.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath);
                    string output = DataRelative(destinationProfile, target);
                    byte[] bytes = ReadManaged(source);
                    AddFile(journal, output, bytes, mustBeAbsent: true);
                    if (!copy) AddFile(journal, source, null);
                    if (copy)
                    {
                        var clone = Clone(entry); clone.Id = Guid.NewGuid(); clone.ProfileId = destinationProfile.Id; clone.RelativePath = target;
                        clone.LaunchCount = 0; clone.LastUsedUtc = null; clone.IsPinned = false; clone.PinnedOrder = int.MaxValue;
                        clone.LegacyRecord = null; journal.After.Shortcuts.Add(clone);
                    }
                    else
                    {
                        var changed = journal.After.Shortcuts.Single(s => s.Id == entry.Id);
                        changed.ProfileId = destinationProfile.Id; changed.RelativePath = target;
                    }
                }
                if (!copy)
                    foreach (var folder in folders.OrderByDescending(f => f.RelativePath.Length))
                        AddDirectory(journal, DataRelative(journal.Before.Profiles.Single(p => p.Id == folder.ProfileId), folder.RelativePath), false);
            }
            if (!copy) AppendCrossProfilePins(journal);
        }

        private static void AppendCrossProfilePins(Journal journal)
        {
            var originals = journal.Before.Shortcuts.ToDictionary(entry => entry.Id);
            var moved = journal.After.Shortcuts.Where(entry => entry.IsPinned && !entry.Suppressed &&
                originals.TryGetValue(entry.Id, out var before) && before.ProfileId != entry.ProfileId).ToArray();
            foreach (var group in moved.GroupBy(entry => entry.ProfileId))
            {
                var ids = group.Select(entry => entry.Id).ToHashSet();
                var existing = journal.After.Shortcuts.Where(entry => entry.ProfileId == group.Key && entry.IsPinned && !entry.Suppressed && !ids.Contains(entry.Id))
                    .OrderBy(entry => entry.PinnedOrder).ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id).ToList();
                if (existing.Any(entry => entry.PinnedOrder > int.MaxValue - group.Count() - 1))
                    for (int index = 0; index < existing.Count; index++) existing[index].PinnedOrder = index;
                int next = existing.Count == 0 ? 0 : existing.Max(entry => entry.PinnedOrder) + 1;
                foreach (var entry in group.OrderBy(entry => originals[entry.Id].PinnedOrder)
                    .ThenBy(entry => originals[entry.Id].RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id))
                    entry.PinnedOrder = next++;
            }
        }

        private void BuildRemove(Journal journal, Guid[] selection, Guid profileId)
        {
            ActiveProfile(journal.After, profileId);
            if (selection.Length == 0) throw new InvalidDataException("Selection is required.");
            var roots = selection.Select(id => SelectionRoot(journal.Before, id, profileId)).ToArray();
            foreach (var entry in journal.After.Shortcuts.Where(s => s.ProfileId == profileId &&
                roots.Any(r => r.Folder ? Under(s.RelativePath, r.Relative) : r.Id == s.Id)))
            {
                if (entry.Suppressed) continue;
                if (entry.Kind != ShortcutKind.Executable)
                {
                    string relative = DataRelative(journal.After.Profiles.Single(p => p.Id == profileId), entry.RelativePath);
                    if (File.Exists(Resolve(relative))) AddFile(journal, relative, null);
                }
                entry.Suppressed = true;
            }
            var folders = journal.After.Folders.Where(f => f.ProfileId == profileId && roots.Any(r => r.Folder &&
                (SamePath(f.RelativePath, r.Relative) || Under(f.RelativePath, r.Relative)))).OrderByDescending(f => f.RelativePath.Length).ToArray();
            foreach (var folder in folders)
            {
                folder.Suppressed = true;
                AddDirectory(journal, DataRelative(journal.After.Profiles.Single(p => p.Id == profileId), folder.RelativePath), false);
            }
        }

        private void BuildReorder(Journal journal, OperationRequest request)
        {
            ActiveProfile(journal.After, request.ProfileId);
            var selected = request.SelectedIds.Select(id => journal.After.Shortcuts.SingleOrDefault(s => s.Id == id &&
                s.ProfileId == request.ProfileId && !s.Suppressed) ?? throw new InvalidDataException("Invalid reorder selection.")).ToArray();
            if (selected.Length == 0 || selected.Select(s => Path.GetDirectoryName(s.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                throw new InvalidDataException("Reorder needs one complete folder group.");
            string parent = Path.GetDirectoryName(selected[0].RelativePath);
            var group = journal.After.Shortcuts.Where(s => s.ProfileId == request.ProfileId && !s.Suppressed &&
                SamePath(Path.GetDirectoryName(s.RelativePath), parent)).Select(s => s.Id).ToHashSet();
            if (!group.SetEquals(request.SelectedIds) || request.Orders == null || !group.SetEquals(request.Orders.Keys) ||
                request.Orders.Values.Distinct().Count() != group.Count) throw new InvalidDataException("Reorder must cover the full group once.");
            foreach (var entry in selected) entry.SortOrder = request.Orders[entry.Id];
        }

        private void BuildPins(Journal journal, OperationRequest request)
        {
            ActiveProfile(journal.After, request.ProfileId);
            var selected = request.SelectedIds.Select(id => journal.After.Shortcuts.SingleOrDefault(entry =>
                entry.Id == id && entry.ProfileId == request.ProfileId && !entry.Suppressed)
                ?? throw new InvalidDataException("Unknown favorite identity.")).ToArray();
            if (selected.Length == 0) throw new InvalidDataException("Select at least one favorite.");
            var ordered = journal.After.Shortcuts.Where(entry => entry.ProfileId == request.ProfileId && !entry.Suppressed && entry.IsPinned)
                .OrderBy(entry => entry.PinnedOrder).ThenBy(entry => Path.GetFileNameWithoutExtension(entry.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id).ToList();
            if (request.Kind == ShortcutOperationKind.ReorderPins)
            {
                var ids = ordered.Select(entry => entry.Id).ToHashSet();
                if (!ids.SetEquals(request.SelectedIds) || request.Orders == null || !ids.SetEquals(request.Orders.Keys) ||
                    request.Orders.Values.Distinct().Count() != ids.Count)
                    throw new InvalidDataException("Reorder must include the complete favorites list.");
                ordered = ordered.OrderBy(entry => request.Orders[entry.Id]).ToList();
            }
            else foreach (var entry in selected)
            {
                if (request.Kind == ShortcutOperationKind.Unpin)
                { entry.IsPinned = false; entry.PinnedOrder = int.MaxValue; ordered.Remove(entry); }
                else if (!entry.IsPinned)
                {
                    if (ordered.Any(pin => pin.PinnedOrder == int.MaxValue))
                        for (int index = 0; index < ordered.Count; index++) ordered[index].PinnedOrder = index;
                    entry.IsPinned = true;
                    entry.PinnedOrder = ordered.Count == 0 ? 0 : checked(ordered.Max(pin => pin.PinnedOrder) + 1);
                    ordered.Add(entry);
                }
            }
            if (request.Kind == ShortcutOperationKind.ReorderPins)
                for (int index = 0; index < ordered.Count; index++) ordered[index].PinnedOrder = index;
        }

        private void BuildAdd(Journal journal, OperationRequest request)
        {
            var profile = ActiveProfile(journal.After, request.ProfileId);
            if (request.AddItems == null || request.AddItems.Count == 0) throw new InvalidDataException("Items are required.");
            foreach (var item in request.AddItems)
            {
                if (item == null || item.Bytes == null || item.Kind == ShortcutKind.Executable)
                    throw new InvalidDataException("Only explicit link payloads may be added.");
                string relative = Join(request.DestinationFolder, item.RelativePath);
                string extension = Path.GetExtension(relative).ToLowerInvariant();
                if (extension != (item.Kind == ShortcutKind.ShellLink ? ".lnk" : ".url")) throw new InvalidDataException("Kind mismatch.");
                EnsureFolder(journal, profile, Path.GetDirectoryName(relative));
                AssertDestinationUnused(journal, profile.Id, relative);
                AddFile(journal, DataRelative(profile, relative), item.Bytes, true);
                journal.After.Shortcuts.Add(new ShortcutEntry { Id = Guid.NewGuid(), ProfileId = profile.Id,
                    RelativePath = relative, Kind = item.Kind });
            }
        }

        private void BuildRepair(Journal journal, OperationRequest request)
        {
            if (request.SelectedIds.Length != 1 || request.ReplacementBytes == null) throw new InvalidDataException("Repair needs one explicit replacement.");
            var entry = journal.After.Shortcuts.SingleOrDefault(s => s.Id == request.SelectedIds[0] && s.ProfileId == request.ProfileId && !s.Suppressed)
                ?? throw new InvalidDataException("Unknown repair identity.");
            if (entry.Kind == ShortcutKind.Executable) throw new InvalidDataException("Raw executable payload is protected.");
            AddFile(journal, DataRelative(ActiveProfile(journal.After, request.ProfileId), entry.RelativePath), request.ReplacementBytes);
        }

        private void BuildCreateProfile(Journal journal, string name)
        {
            Leaf(name);
            if (journal.After.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new OperationConflict("operation.profile-exists");
            var profile = new ProfileState { Id = Guid.NewGuid(), Name = name, RelativeRoot = Path.Combine("Profiles", name) };
            Resolve(profile.RelativeRoot);
            journal.After.Profiles.Add(profile);
            AddDirectory(journal, "Profiles", true, allowExisting: true);
            AddDirectory(journal, profile.RelativeRoot, true);
        }

        private void BuildImport(Journal journal, OperationRequest request)
        {
            var after = request.Snapshot; _store.Validate(after);
            if (after.Revision != journal.Before.Revision || !after.CommittedOperationIds.SequenceEqual(journal.Before.CommittedOperationIds) ||
                !Same(after.LegacySettings, journal.Before.LegacySettings) || request.ImportFiles == null ||
                !Same(after.LegacyAmbiguities.Take(journal.Before.LegacyAmbiguities.Count).ToArray(), journal.Before.LegacyAmbiguities.ToArray())) throw new InvalidDataException("Invalid import snapshot.");
            // Import cannot modify local integration preferences or existing profile/folder bindings.
            var local = journal.Before.Settings; var candidate = after.Settings;
            if (candidate.ActiveProfileName != local.ActiveProfileName || candidate.StartWithWindows != local.StartWithWindows || candidate.PortableMode != local.PortableMode ||
                candidate.HotkeyEnabled != local.HotkeyEnabled || candidate.HotkeyKey != local.HotkeyKey || candidate.HotkeyModifiers != local.HotkeyModifiers ||
                candidate.WidgetPositionX != local.WidgetPositionX || candidate.WidgetPositionY != local.WidgetPositionY || candidate.WidgetTaskbarPosition != local.WidgetTaskbarPosition ||
                candidate.TaskbarButtonEnabled != local.TaskbarButtonEnabled || candidate.TaskbarHoverEnabled != local.TaskbarHoverEnabled || candidate.WidgetPositionLocked != local.WidgetPositionLocked || candidate.WidgetHoverDelayMs != local.WidgetHoverDelayMs ||
                candidate.WidgetSizePercent != local.WidgetSizePercent || candidate.WidgetMenuHideDelayMs != local.WidgetMenuHideDelayMs)
                throw new InvalidDataException("Import cannot change local integration settings.");
            foreach (var profile in journal.Before.Profiles) if (!Same(profile, after.Profiles.SingleOrDefault(p => p.Id == profile.Id))) throw new InvalidDataException("Import changed a profile.");
            foreach (var folder in journal.Before.Folders) if (!Same(folder, after.Folders.SingleOrDefault(f => f.Id == folder.Id))) throw new InvalidDataException("Import changed a folder.");
            foreach (var old in journal.Before.Shortcuts)
            {
                var entry = after.Shortcuts.SingleOrDefault(e => e.Id == old.Id);
                if (entry == null || old.ProfileId != entry.ProfileId || old.RelativePath != entry.RelativePath || old.Kind != entry.Kind || old.Suppressed != entry.Suppressed ||
                    old.Kind == ShortcutKind.Executable && !Same(old, entry)) throw new InvalidDataException("Import changed a protected binding.");
                if (!Same(old, entry) && !request.ImportFiles.Any(file => SamePath(file.Path, DataRelative(after.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath)) && file.Replace))
                    throw new InvalidDataException("Replaced metadata needs an explicit file conflict decision.");
            }
            journal.After = LauncherStateStore.Clone(after);
            foreach (var profile in after.Profiles.Where(p => !journal.Before.Profiles.Any(old => old.Id == p.Id)))
            { Leaf(profile.Name); AddDirectory(journal, "Profiles", true, true); AddDirectory(journal, profile.RelativeRoot, true); }
            foreach (var folder in after.Folders.Where(f => !journal.Before.Folders.Any(old => old.Id == f.Id)))
                EnsureFolder(journal, after.Profiles.Single(p => p.Id == folder.ProfileId), folder.RelativePath);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in request.ImportFiles)
            {
                if (file == null || file.Bytes == null || !paths.Add(Resolve(file.Path))) throw new InvalidDataException("Invalid import payload.");
                if (IsIconAsset(file.Path))
                { if (file.Replace || file.Bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Managed asset replacement is not supported."); AddDirectory(journal, Path.Combine("Assets", "Icons"), true, true); }
                else
                {
                    var binding = FindProfileBinding(after, file.Path);
                    var entry = after.Shortcuts.SingleOrDefault(e => e.ProfileId == binding.Profile.Id && SamePath(e.RelativePath, binding.Relative) && !e.Suppressed);
                    if (entry == null || entry.Kind == ShortcutKind.Executable) throw new InvalidDataException("Import payload is not a managed link.");
                    EnsureFolder(journal, binding.Profile, Path.GetDirectoryName(binding.Relative));
                }
                AddFile(journal, file.Path, file.Bytes, !file.Replace);
            }
            foreach (var entry in after.Shortcuts.Where(e => !journal.Before.Shortcuts.Any(old => old.Id == e.Id) && !e.Suppressed))
                if (entry.Kind == ShortcutKind.Executable || !paths.Contains(Resolve(DataRelative(after.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath))))
                    throw new InvalidDataException("Imported link has no validated payload.");
        }

        private static bool IsIconAsset(string path) => string.Equals(Path.GetDirectoryName(path), Path.Combine("Assets", "Icons"), StringComparison.OrdinalIgnoreCase) &&
            new[] { ".ico", ".png" }.Contains(Path.GetExtension(path).ToLowerInvariant());

        private void BuildDeleteProfile(Journal journal, Guid profileId)
        {
            var profile = ActiveProfile(journal.After, profileId);
            if (profile.Id == ProfileState.DefaultId || profile.Name.Equals(journal.After.Settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Default/active profiles cannot be removed.");
            var roots = journal.After.Shortcuts.Where(s => s.ProfileId == profileId && !s.Suppressed).Select(s => s.Id)
                .Concat(journal.After.Folders.Where(f => f.ProfileId == profileId && !f.Suppressed).Select(f => f.Id)).ToArray();
            if (roots.Length != 0) BuildRemove(journal, roots, profileId);
            profile.Suppressed = true;
            AddDirectory(journal, profile.RelativeRoot, false);
        }

        private void BuildSnapshot(Journal journal, LauncherState snapshot)
        {
            _store.Validate(snapshot);
            if (snapshot == null || snapshot.Revision != journal.Before.Revision ||
                !snapshot.CommittedOperationIds.SequenceEqual(journal.Before.CommittedOperationIds) ||
                !Same(snapshot.Profiles, journal.Before.Profiles) || !Same(snapshot.Folders, journal.Before.Folders) ||
                !Same(snapshot.LegacyAmbiguities, journal.Before.LegacyAmbiguities) || !Same(snapshot.LegacySettings, journal.Before.LegacySettings) ||
                snapshot.Shortcuts.Count != journal.Before.Shortcuts.Count)
                throw new InvalidDataException("A settings snapshot cannot change catalog structure or operation markers.");
            foreach (var entry in snapshot.Shortcuts)
            {
                var original = journal.Before.Shortcuts.SingleOrDefault(s => s.Id == entry.Id);
                if (original == null || original.ProfileId != entry.ProfileId || original.Kind != entry.Kind ||
                    original.RelativePath != entry.RelativePath || original.Suppressed != entry.Suppressed || !Same(original.LegacyRecord, entry.LegacyRecord))
                    throw new InvalidDataException("A metadata snapshot cannot change bindings.");
            }
            journal.After = LauncherStateStore.Clone(snapshot);
        }

        private void BuildReconcile(Journal journal, Dictionary<string, string> mappings)
        {
            foreach (var mapping in mappings ?? new Dictionary<string, string>())
            {
                string from = Path.GetRelativePath(_root, Resolve(mapping.Key));
                string to = Path.GetRelativePath(_root, Resolve(mapping.Value));
                if (!File.Exists(Resolve(to)) && !Directory.Exists(Resolve(to))) continue;
                foreach (var entry in journal.After.Shortcuts)
                {
                    string binding = DataRelative(journal.After.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath);
                    if (!SamePath(binding, from) && !Under(binding, from)) continue;
                    string replacement = ReplacePrefix(binding, from, to);
                    var destination = FindProfileBinding(journal.After, replacement);
                    entry.ProfileId = destination.Profile.Id; entry.RelativePath = destination.Relative;
                }
                foreach (var folder in journal.After.Folders)
                {
                    string binding = DataRelative(journal.After.Profiles.Single(p => p.Id == folder.ProfileId), folder.RelativePath);
                    if (!SamePath(binding, from) && !Under(binding, from)) continue;
                    var destination = FindProfileBinding(journal.After, ReplacePrefix(binding, from, to));
                    folder.ProfileId = destination.Profile.Id; folder.RelativePath = destination.Relative;
                }
            }
            foreach (var profile in journal.After.Profiles.Where(p => !p.Suppressed))
            {
                string root = Resolve(profile.RelativeRoot);
                if (Directory.Exists(root)) Inventory(journal.After, profile, root, root);
            }
        }

        private void Inventory(LauncherState state, ProfileState profile, string root, string directory)
        {
            Resolve(Path.GetRelativePath(_root, directory));
            foreach (string file in Directory.GetFiles(directory))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".lnk" && extension != ".url" && extension != ".exe") continue;
                string relative = Path.GetRelativePath(root, file);
                Resolve(DataRelative(profile, relative));
                if (!state.Shortcuts.Any(s => s.ProfileId == profile.Id && SamePath(s.RelativePath, relative)))
                    state.Shortcuts.Add(new ShortcutEntry { Id = Guid.NewGuid(), ProfileId = profile.Id, RelativePath = relative,
                        Kind = extension == ".lnk" ? ShortcutKind.ShellLink : extension == ".url" ? ShortcutKind.Url : ShortcutKind.Executable });
            }
            foreach (string child in Directory.GetDirectories(directory))
            {
                string relative = Path.GetRelativePath(root, child);
                Resolve(DataRelative(profile, relative));
                if (!state.Folders.Any(f => f.ProfileId == profile.Id && SamePath(f.RelativePath, relative)))
                    state.Folders.Add(new FolderState { Id = Guid.NewGuid(), ProfileId = profile.Id, RelativePath = relative });
                Inventory(state, profile, root, child);
            }
        }

        private void BuildRestore(Journal journal, OperationRequest request)
        {
            if (request.RestoreOperationId == Guid.Empty || request.RestoreOperationId == request.OperationId)
                throw new InvalidDataException("A recovery operation is required.");
            var original = ReadJournal(request.RestoreOperationId);
            if (!CanRestore(original) || !journal.Before.CommittedOperationIds.Contains(original.OperationId) ||
                JournalIds().Select(ReadJournal).Any(j => j.Kind == ShortcutOperationKind.Restore && j.RestoreOperationId == original.OperationId &&
                    journal.Before.CommittedOperationIds.Contains(j.OperationId))) throw new OperationConflict("operation.already-restored-or-unavailable");
            var replaceableIds = RestoreFileBindings(original).Keys.ToHashSet();
            if (request.ReplacementNames != null && request.ReplacementNames.Keys.Any(id => !replaceableIds.Contains(id)))
                throw new InvalidDataException("Only actual restored link files may receive replacement names.");
            var pathOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var old in original.Before.Shortcuts)
            {
                var after = original.After.Shortcuts.SingleOrDefault(s => s.Id == old.Id);
                if (after == null || Same(old, after)) continue;
                var current = journal.After.Shortcuts.SingleOrDefault(s => s.Id == old.Id)
                    ?? throw new OperationConflict("operation.restore-identity-missing");
                if (!Same(current, after)) throw new OperationConflict("operation.restore-metadata-changed");
                var restored = Clone(old);
                if (request.ReplacementNames != null && request.ReplacementNames.TryGetValue(old.Id, out var replacement))
                {
                    string renamed = Join(Path.GetDirectoryName(old.RelativePath), Leaf(replacement));
                    if (Path.GetExtension(renamed).ToLowerInvariant() != Path.GetExtension(old.RelativePath).ToLowerInvariant())
                        throw new InvalidDataException("Restore replacement must retain the shortcut extension.");
                    string oldBinding = DataRelative(original.Before.Profiles.Single(p => p.Id == old.ProfileId), old.RelativePath);
                    string newBinding = DataRelative(original.Before.Profiles.Single(p => p.Id == old.ProfileId), renamed);
                    pathOverrides[oldBinding] = newBinding;
                    restored.RelativePath = renamed;
                }
                journal.After.Shortcuts[journal.After.Shortcuts.IndexOf(current)] = restored;
            }
            foreach (var added in original.After.Shortcuts.Where(s => !original.Before.Shortcuts.Any(b => b.Id == s.Id)))
            {
                var current = journal.After.Shortcuts.SingleOrDefault(s => s.Id == added.Id);
                if (current == null || !Same(current, added)) throw new OperationConflict("operation.restore-created-item-changed");
                current.Suppressed = true;
            }
            foreach (var old in original.Before.Folders)
            {
                var after = original.After.Folders.SingleOrDefault(f => f.Id == old.Id);
                if (after == null || Same(old, after)) continue;
                var current = journal.After.Folders.SingleOrDefault(f => f.Id == old.Id);
                if (current == null || !Same(current, after)) throw new OperationConflict("operation.restore-folder-changed");
                journal.After.Folders[journal.After.Folders.IndexOf(current)] = Clone(old);
            }
            foreach (var added in original.After.Folders.Where(f => !original.Before.Folders.Any(b => b.Id == f.Id)))
            {
                var current = journal.After.Folders.SingleOrDefault(f => f.Id == added.Id);
                if (current == null || !Same(current, added)) throw new OperationConflict("operation.restore-folder-changed");
                current.Suppressed = true;
            }
            foreach (var old in original.Before.Profiles)
            {
                var after = original.After.Profiles.SingleOrDefault(p => p.Id == old.Id);
                if (after == null || Same(old, after)) continue;
                var current = journal.After.Profiles.SingleOrDefault(p => p.Id == old.Id);
                if (current == null || !Same(current, after)) throw new OperationConflict("operation.restore-profile-changed");
                journal.After.Profiles[journal.After.Profiles.IndexOf(current)] = Clone(old);
            }
            foreach (var added in original.After.Profiles.Where(p => !original.Before.Profiles.Any(b => b.Id == p.Id)))
            {
                var current = journal.After.Profiles.SingleOrDefault(p => p.Id == added.Id);
                if (current == null || !Same(current, added) || journal.After.Shortcuts.Any(s => s.ProfileId == added.Id && !s.Suppressed))
                    throw new OperationConflict("operation.restore-created-profile-changed");
                current.Suppressed = true;
            }
            RestorePinPositions(journal, original);
            // Inverse directory creation precedes files; inverse removal follows files.
            foreach (var delta in original.Files.AsEnumerable().Reverse().Where(d => d.Directory && d.BeforeExists))
                AddDirectory(journal, delta.Path, true, allowExisting: true);
            foreach (var delta in original.Files.AsEnumerable().Reverse().Where(d => !d.Directory))
            {
                string output = pathOverrides.TryGetValue(delta.Path, out var replacement) ? replacement : delta.Path;
                string actual = Resolve(output);
                if (delta.AfterExists && output == delta.Path && (!File.Exists(actual) || HashFile(actual) != delta.AfterHash))
                    throw new OperationConflict("operation.restore-file-changed");
                if (!delta.AfterExists && File.Exists(actual)) throw new OperationConflict("operation.restore-path-occupied");
                byte[] bytes = delta.BeforeExists ? ReadPayload(original.OperationId, delta.BeforePayload, delta.BeforeHash) : null;
                if (bytes != null) { if (IsIconAsset(output)) AddDirectory(journal, Path.GetDirectoryName(output), true, true); else EnsureDataParent(journal, output); }
                AddFile(journal, output, bytes, mustBeAbsent: !delta.AfterExists);
            }
            foreach (var delta in original.Files.AsEnumerable().Reverse().Where(d => d.Directory && !d.BeforeExists))
                AddDirectory(journal, delta.Path, false);
            if (original.Kind == ShortcutOperationKind.ApplyImport)
            {
                if (!Same(journal.Before.Settings, original.After.Settings)) throw new OperationConflict("operation.restore-settings-changed");
                journal.After.Settings = Clone(original.Before.Settings);
                if (!Same(journal.Before.LegacyAmbiguities, original.After.LegacyAmbiguities)) throw new OperationConflict("operation.restore-ambiguities-changed");
                journal.After.LegacyAmbiguities = original.Before.LegacyAmbiguities.Select(Clone).ToList();
            }
        }

        private static void RestorePinPositions(Journal journal, Journal original)
        {
            foreach (var profile in original.Before.Profiles)
            {
                var before = original.Before.Shortcuts.Where(entry => entry.ProfileId == profile.Id && entry.IsPinned && !entry.Suppressed)
                    .OrderBy(entry => entry.PinnedOrder).ThenBy(entry => Path.GetFileNameWithoutExtension(entry.RelativePath), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id).ToArray();
                var restored = before.Select((entry, index) => (Entry: entry, Index: index)).Where(pair =>
                    original.After.Shortcuts.Any(after => after.Id == pair.Entry.Id && (after.Suppressed || !after.IsPinned || after.ProfileId != profile.Id)) &&
                    journal.After.Shortcuts.Any(current => current.Id == pair.Entry.Id && current.IsPinned && !current.Suppressed)).ToArray();
                if (restored.Length == 0) continue;
                var current = journal.After.Shortcuts.Where(entry => entry.ProfileId == profile.Id && entry.IsPinned && !entry.Suppressed)
                    .OrderBy(entry => entry.PinnedOrder).ThenBy(entry => Path.GetFileNameWithoutExtension(entry.RelativePath), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id).ToList();
                if (current.Select(entry => entry.PinnedOrder).Distinct().Count() == current.Count && restored.All(pair =>
                    current.FindIndex(entry => entry.Id == pair.Entry.Id) == Math.Min(pair.Index, current.Count - 1))) continue;
                var ids = restored.Select(pair => pair.Entry.Id).ToHashSet();
                var merged = current.Where(entry => !ids.Contains(entry.Id)).ToList();
                foreach (var pair in restored)
                    merged.Insert(Math.Min(pair.Index, merged.Count), current.Single(entry => entry.Id == pair.Entry.Id));
                for (int index = 0; index < merged.Count; index++) merged[index].PinnedOrder = index;
            }
        }

        private void EnsureDataParent(Journal journal, string dataRelative)
        {
            var binding = FindProfileBinding(journal.After, dataRelative);
            EnsureFolder(journal, binding.Profile, Path.GetDirectoryName(binding.Relative));
        }

        private void EnsureFolder(Journal journal, ProfileState profile, string relative)
        {
            AddDirectory(journal, profile.RelativeRoot, true, allowExisting: true);
            if (string.IsNullOrEmpty(relative)) return;
            Resolve(DataRelative(profile, relative));
            string current = "";
            foreach (string part in relative.Replace('/', '\\').Split('\\'))
            {
                current = Join(current, part);
                var existing = journal.After.Folders.SingleOrDefault(f => f.ProfileId == profile.Id && SamePath(f.RelativePath, current));
                if (existing != null && existing.Suppressed) throw new OperationConflict("operation.destination-folder-suppressed");
                if (existing == null)
                    journal.After.Folders.Add(new FolderState { Id = Guid.NewGuid(), ProfileId = profile.Id, RelativePath = current });
                AddDirectory(journal, DataRelative(profile, current), true, allowExisting: true);
            }
        }

        private void AssertDestinationUnused(Journal journal, Guid profileId, string relative)
        {
            var profile = ActiveProfile(journal.After, profileId);
            string path = Resolve(DataRelative(profile, relative));
            if (File.Exists(path) || Directory.Exists(path) || journal.After.Shortcuts.Any(s => s.ProfileId == profileId && SamePath(s.RelativePath, relative)) ||
                journal.After.Folders.Any(f => f.ProfileId == profileId && SamePath(f.RelativePath, relative)))
                throw new OperationConflict("operation.destination-occupied");
        }

        private void AddFile(Journal journal, string relative, byte[] after, bool mustBeAbsent = false)
        {
            string full = Resolve(relative);
            if (journal.Files.Any(d => SamePath(d.Path, relative))) throw new InvalidDataException("Overlapping file deltas.");
            if (Directory.Exists(full)) throw new OperationConflict("operation.destination-is-directory");
            byte[] before = File.Exists(full) ? File.ReadAllBytes(full) : null;
            if (mustBeAbsent && before != null) throw new OperationConflict("operation.destination-occupied");
            if (before == null && after == null) return;
            journal.Files.Add(new FileDelta { Path = relative, BeforeExists = before != null, AfterExists = after != null,
                BeforeBytes = before, AfterBytes = after, BeforeHash = before == null ? null : Hash(before), AfterHash = after == null ? null : Hash(after) });
        }

        private void AddDirectory(Journal journal, string relative, bool exists, bool allowExisting = false)
        {
            string full = Resolve(relative);
            if (File.Exists(full)) throw new OperationConflict("operation.directory-is-file");
            var planned = journal.Files.SingleOrDefault(d => d.Directory && SamePath(d.Path, relative));
            if (planned != null)
            {
                if (planned.AfterExists != exists) throw new InvalidDataException("Overlapping directory deltas.");
                return;
            }
            bool before = Directory.Exists(full);
            if (before && exists && !allowExisting) throw new OperationConflict("operation.directory-exists");
            if (before == exists) return;
            if (exists)
            {
                string parent = Path.GetDirectoryName(relative);
                if (!string.IsNullOrEmpty(parent)) AddDirectory(journal, parent, true, allowExisting: true);
            }
            journal.Files.Add(new FileDelta { Path = relative, Directory = true, BeforeExists = before, AfterExists = exists });
        }

        private void Stage(Journal journal)
        {
            string directory = OperationDirectory(journal.OperationId);
            if (Directory.Exists(directory)) throw new OperationConflict("operation.directory-already-exists");
            Directory.CreateDirectory(directory);
            for (int index = 0; index < journal.Files.Count; index++)
            {
                var delta = journal.Files[index];
                if (delta.BeforeBytes != null)
                {
                    delta.BeforePayload = index.ToString("D6") + ".before";
                    delta.CapturedBefore = index.ToString("D6") + ".captured-before";
                    WriteNew(SafePath.Resolve(directory, delta.BeforePayload), delta.BeforeBytes);
                }
                if (delta.AfterBytes != null)
                {
                    delta.AfterPayload = index.ToString("D6") + ".after";
                    delta.CapturedRollback = index.ToString("D6") + ".captured-rollback";
                    WriteNew(SafePath.Resolve(directory, delta.AfterPayload), delta.AfterBytes);
                }
            }
        }

        private void VerifyBefore(FileDelta delta)
        {
            string path = Resolve(delta.Path);
            if (delta.Directory)
            {
                if (Directory.Exists(path) != delta.BeforeExists || File.Exists(path)) throw new OperationConflict("operation.directory-changed");
            }
            else if (Directory.Exists(path) || File.Exists(path) != delta.BeforeExists ||
                delta.BeforeExists && HashFile(path) != delta.BeforeHash) throw new OperationConflict("operation.source-changed");
        }

        private void Apply(FileDelta delta, Guid operationId)
        {
            VerifyBefore(delta);
            _fault?.Invoke(OperationPhase.BeforeFileApply);
            string path = Resolve(delta.Path);
            if (delta.Directory)
            {
                if (delta.AfterExists) Directory.CreateDirectory(path);
                else if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, recursive: false);
                else delta.PreservedDirectory = true;
            }
            else if (!delta.AfterExists)
            {
                string captured = CapturePath(operationId, delta.CapturedBefore);
                File.Move(path, captured);
                VerifyCaptured(captured, delta.BeforeHash);
            }
            else
            {
                byte[] bytes = ReadPayload(operationId, delta.AfterPayload, delta.AfterHash);
                string captured = delta.BeforeExists ? CapturePath(operationId, delta.CapturedBefore) : null;
                InstallBytes(path, bytes, delta.BeforeExists, captured);
                if (captured != null) VerifyCaptured(captured, delta.BeforeHash);
            }
        }

        private void Rollback(FileDelta delta, Guid operationId)
        {
            string path = Resolve(delta.Path);
            if (delta.Directory)
            {
                if (delta.BeforeExists)
                {
                    if (File.Exists(path)) throw new OperationConflict("recovery.directory-replaced-by-file");
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                }
                else if (Directory.Exists(path))
                {
                    if (Directory.EnumerateFileSystemEntries(path).Any()) throw new OperationConflict("recovery.directory-has-new-content");
                    Directory.Delete(path, recursive: false);
                }
                return;
            }
            if (Directory.Exists(path)) throw new OperationConflict("recovery.file-replaced-by-directory");
            bool exists = File.Exists(path);
            string actual = exists ? HashFile(path) : null;
            if (exists == delta.BeforeExists && actual == delta.BeforeHash) return;
            if (exists != delta.AfterExists || actual != delta.AfterHash) throw new OperationConflict("recovery.file-has-new-content");
            if (!delta.BeforeExists)
            {
                string captured = CapturePath(operationId, delta.CapturedRollback);
                File.Move(path, captured);
                VerifyCaptured(captured, delta.AfterHash);
            }
            else
            {
                string captured = exists ? CapturePath(operationId, delta.CapturedRollback) : null;
                InstallBytes(path, ReadPayload(operationId, delta.BeforePayload, delta.BeforeHash), exists, captured);
                if (captured != null) VerifyCaptured(captured, delta.AfterHash);
            }
        }

        private string CapturePath(Guid operationId, string name)
        {
            string path = SafePath.Resolve(OperationDirectory(operationId), name);
            if (File.Exists(path) || Directory.Exists(path)) throw new OperationConflict("operation.capture-path-occupied");
            return path;
        }

        private static void VerifyCaptured(string path, string expected)
        {
            if (HashFile(path) != expected)
                throw new OperationConflict("operation.concurrent-content-preserved-in-recovery");
        }

        private void InstallBytes(string destination, byte[] bytes, bool replace, string captured = null)
        {
            // Staging is inside the same validated data root/volume; payload copies remain immutable.
            string workRoot = Resolve("Recovery");
            Directory.CreateDirectory(workRoot);
            string temporary = SafePath.Resolve(workRoot, "work-" + Guid.NewGuid().ToString("N") + ".tmp");
            WriteNew(temporary, bytes);
            if (replace) File.Replace(temporary, destination, captured);
            else File.Move(temporary, destination);
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes); stream.Flush(true);
        }

        private void WriteJournal(Journal journal)
        {
            journal.IntegrityHash = null;
            journal.IntegrityHash = Hash(JsonSerializer.SerializeToUtf8Bytes(journal, LauncherStateStore.JsonOptions));
            string path = JournalPath(journal.OperationId);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            WriteNew(temporary, JsonSerializer.SerializeToUtf8Bytes(journal, LauncherStateStore.JsonOptions));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        public OperationResult InspectPending()
        {
            try { return InspectPendingCore(); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
            { return Fail(OperationStatus.RecoveryRequired, "operation.journal-unreadable", Guid.Empty); }
        }

        private OperationResult InspectPendingCore()
        {
            var state = _store.Load();
            if (!state.Result.Success || state.ReadOnly) return state.Result;
            foreach (Guid id in JournalIds())
            {
                var journal = ReadJournal(id);
                bool committed = state.State?.CommittedOperationIds.Contains(id) == true;
                if (journal.Phase == OperationPhase.Completed && !committed || journal.Phase == OperationPhase.RolledBack && committed)
                    return Fail(OperationStatus.RecoveryRequired, "operation.journal-marker-conflict", id);
                if (journal.Phase != OperationPhase.Completed && journal.Phase != OperationPhase.RolledBack)
                    return Fail(OperationStatus.RecoveryRequired, "operation.pending-journal", id);
            }
            return new OperationResult();
        }

        public IReadOnlyList<RecoveryOperationInfo> GetRecoveryOperations()
        {
            var result = new List<RecoveryOperationInfo>();
            try
            {
                var loaded = _store.Load();
                var state = loaded.State;
                var journals = new Dictionary<Guid, Journal>();
                foreach (Guid id in JournalIds())
                {
                    try
                    {
                        journals.Add(id, ReadJournal(id));
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
                    { result.Add(new RecoveryOperationInfo { OperationId = id, Kind = "Unknown", Phase = "Invalid", Pending = true, Error = "Recovery journal is invalid or incomplete." }); }
                }
                var restoredIds = journals.Values.Where(j => j.Kind == ShortcutOperationKind.Restore &&
                    state?.CommittedOperationIds.Contains(j.OperationId) == true).Select(j => j.RestoreOperationId).ToHashSet();
                foreach (var pair in journals)
                {
                    Guid id = pair.Key; var journal = pair.Value;
                    result.Add(new RecoveryOperationInfo { OperationId = id, CreatedUtc = journal.CreatedUtc, Kind = journal.Kind.ToString(),
                        Phase = journal.Phase.ToString(), ItemPaths = journal.AffectedPaths,
                        OriginalItemPaths = RestoreFileBindings(journal),
                        PayloadBytes = journal.Files.Sum(d => PayloadLength(id, d.BeforePayload) + PayloadLength(id, d.AfterPayload) +
                            PayloadLength(id, d.CapturedBefore) + PayloadLength(id, d.CapturedRollback)),
                        MetadataBytes = FileLength(JournalPath(id)) + FileLength(JournalPath(id) + ".bak"),
                        CanRestore = loaded.Result.Success && !loaded.ReadOnly && CanRestore(journal) && !restoredIds.Contains(id) && state?.CommittedOperationIds.Contains(id) == true,
                        Pending = journal.Phase != OperationPhase.Completed && journal.Phase != OperationPhase.RolledBack });
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
            { result.Add(new RecoveryOperationInfo { Pending = true, Phase = "Invalid", Error = "Recovery inventory could not be read." }); }
            return result.OrderByDescending(info => info.CreatedUtc).ToArray();
        }

        public OperationResult RecoverPending()
        {
            bool changed = false;
            try
            {
                _busy = true;
                using (var lease = _store.AcquireWriterLease())
                {
                    foreach (Guid id in JournalIds())
                    {
                        var journal = ReadJournal(id);
                        if (journal.Phase == OperationPhase.Completed || journal.Phase == OperationPhase.RolledBack) continue;
                        var current = _store.Load();
                        if (!current.Result.Success || current.ReadOnly || current.State == null) return current.Result;
                        if (current.State.CommittedOperationIds.Contains(id))
                        {
                            // The state replacement may have completed before its journal update.
                            journal.After.Revision = checked(journal.Before.Revision + 1);
                            if (!journal.After.CommittedOperationIds.Contains(id)) journal.After.CommittedOperationIds.Add(id);
                            journal.Phase = OperationPhase.Completed;
                            WriteJournal(journal); changed = true;
                            continue;
                        }
                        if (journal.Phase == OperationPhase.StateCommitted)
                            return Fail(OperationStatus.RecoveryRequired, "recovery.committed-marker-missing", id);
                        if (current.Fingerprint != journal.BeforeFingerprint)
                            return Fail(OperationStatus.RecoveryRequired, "recovery.state-changed", id);
                        // Validate every current path first, then compensate in reverse order.
                        foreach (var delta in journal.Files) VerifyRecoverable(delta);
                        foreach (var delta in journal.Files.AsEnumerable().Reverse()) Rollback(delta, id);
                        journal.Phase = OperationPhase.RolledBack;
                        WriteJournal(journal); changed = true;
                    }
                }
                if (changed)
                {
                    var state = _store.Load().State;
                    Publish(new CatalogChangedEventArgs { Revision = state?.Revision ?? 0, ChangeKind = ShortcutOperationKind.Restore });
                }
                return new OperationResult { NewRevision = _store.Load().State?.Revision };
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException ||
                ex is ArgumentException || ex is InvalidOperationException || ex is OperationConflict)
            { return Fail(OperationStatus.RecoveryRequired, "recovery.manual-resolution-required", Guid.Empty); }
            finally { _busy = false; }
        }

        /// <summary>Explicitly confirmed cleanup only; durable tombstones preserve replay protection.</summary>
        public OperationResult PurgeRecovery(Guid[] operationIds)
        {
            if (operationIds == null || operationIds.Length == 0 || operationIds.Any(id => id == Guid.Empty) ||
                operationIds.Distinct().Count() != operationIds.Length)
                return Fail(OperationStatus.ValidationError, "recovery.invalid-cleanup-selection", Guid.Empty);
            try
            {
                _busy = true;
                using var lease = _store.AcquireWriterLease();
                var pending = InspectPending();
                if (!pending.Success) return pending;
                var journals = operationIds.Select(ReadJournal).ToArray();
                // Validate every selected directory and immutable payload before revoking undo for any.
                foreach (var journal in journals)
                {
                    if (journal.Phase != OperationPhase.Completed && journal.Phase != OperationPhase.RolledBack)
                        return Fail(OperationStatus.RecoveryRequired, "recovery.pending-cannot-be-cleared", journal.OperationId);
                    string directory = OperationDirectory(journal.OperationId);
                    if (Directory.GetDirectories(directory).Length != 0)
                        return Fail(OperationStatus.Conflict, "recovery.unexpected-content", journal.OperationId);
                    var expected = journal.Files.SelectMany(d => new[] { d.BeforePayload, d.AfterPayload, d.CapturedBefore, d.CapturedRollback })
                        .Where(name => name != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    expected.Add("journal.json"); expected.Add("journal.json.bak");
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        SafePath.Resolve(directory, Path.GetFileName(file));
                        if (!expected.Contains(Path.GetFileName(file)))
                            return Fail(OperationStatus.Conflict, "recovery.unexpected-content", journal.OperationId);
                    }
                    foreach (var delta in journal.Files.Where(d => !d.Directory))
                    {
                        VerifyRemainingPayload(journal, delta.BeforePayload, delta.BeforeHash);
                        VerifyRemainingPayload(journal, delta.AfterPayload, delta.AfterHash);
                        VerifyOptionalCaptured(journal, delta.CapturedBefore, delta.BeforeHash);
                        VerifyOptionalCaptured(journal, delta.CapturedRollback, delta.AfterHash);
                    }
                }
                foreach (var journal in journals)
                {
                    string previousPrimaryHash = HashFile(JournalPath(journal.OperationId));
                    journal.Purged = true;
                    journal.Before = TombstoneState(journal.Before, journal.OperationId, false);
                    journal.After = TombstoneState(journal.After, journal.OperationId, journal.Phase == OperationPhase.Completed);
                    journal.ChangedIds = Array.Empty<Guid>();
                    WriteJournal(journal);
                    _fault?.Invoke(OperationPhase.PurgePrepared);
                    foreach (var delta in journal.Files.Where(d => !d.Directory))
                    {
                        DeletePayload(journal, delta.BeforePayload, delta.BeforeHash);
                        DeletePayload(journal, delta.AfterPayload, delta.AfterHash);
                        DeletePayload(journal, delta.CapturedBefore, delta.BeforeHash);
                        DeletePayload(journal, delta.CapturedRollback, delta.AfterHash);
                    }
                    string backup = SafePath.Resolve(OperationDirectory(journal.OperationId), "journal.json.bak");
                    if (File.Exists(backup))
                    {
                        if (HashFile(backup) != previousPrimaryHash || ParseJournal(File.ReadAllBytes(backup)).OperationId != journal.OperationId)
                            return Fail(OperationStatus.Conflict, "recovery.backup-changed", journal.OperationId);
                        File.Delete(backup);
                    }
                }
                return new OperationResult();
            }
            catch (UnauthorizedAccessException) { return Fail(OperationStatus.AccessDenied, "recovery.cleanup-access-denied", Guid.Empty); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
            { return Fail(OperationStatus.RecoveryRequired, "recovery.cleanup-incomplete", Guid.Empty); }
            finally { _busy = false; }
        }

        private void VerifyRemainingPayload(Journal journal, string name, string hash)
        {
            if (name == null) return;
            string path = SafePath.Resolve(OperationDirectory(journal.OperationId), name);
            if (File.Exists(path)) ReadPayload(journal.OperationId, name, hash);
            else if (!journal.Purged) throw new InvalidDataException("Recovery payload is missing.");
        }

        private void DeletePayload(Journal journal, string name, string hash)
        {
            if (name == null) return;
            string path = SafePath.Resolve(OperationDirectory(journal.OperationId), name);
            if (!File.Exists(path)) return;
            ReadPayload(journal.OperationId, name, hash);
            File.Delete(path);
        }

        private void VerifyOptionalCaptured(Journal journal, string name, string hash)
        {
            if (name != null && File.Exists(SafePath.Resolve(OperationDirectory(journal.OperationId), name)))
                ReadPayload(journal.OperationId, name, hash);
        }

        private static LauncherState TombstoneState(LauncherState original, Guid operationId, bool committed) => new LauncherState
        {
            Revision = original.Revision,
            Profiles = original.Profiles.Select(Clone).ToList(),
            CommittedOperationIds = committed ? new List<Guid> { operationId } : new List<Guid>()
        };

        private void VerifyRecoverable(FileDelta delta)
        {
            string path = Resolve(delta.Path);
            if (delta.Directory)
            {
                if (File.Exists(path)) throw new OperationConflict("recovery.directory-replaced");
                return;
            }
            bool exists = File.Exists(path);
            string hash = exists ? HashFile(path) : null;
            if (Directory.Exists(path) || !(exists == delta.BeforeExists && hash == delta.BeforeHash ||
                exists == delta.AfterExists && hash == delta.AfterHash)) throw new OperationConflict("recovery.file-changed");
        }

        private OperationResult CancelPrepared(Journal journal)
        {
            foreach (var delta in journal.Files) VerifyRecoverable(delta);
            foreach (var delta in journal.Files.AsEnumerable().Reverse()) Rollback(delta, journal.OperationId);
            journal.Phase = OperationPhase.RolledBack;
            WriteJournal(journal);
            return Fail(OperationStatus.Cancelled, "operation.cancelled-before-commit", journal.OperationId);
        }

        private IEnumerable<Guid> JournalIds()
        {
            string recovery = Resolve("Recovery");
            if (!Directory.Exists(recovery)) return Array.Empty<Guid>();
            return Directory.GetDirectories(recovery).Select(directory =>
            {
                Resolve(Path.GetRelativePath(_root, directory));
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id))
                    throw new InvalidDataException("Unknown recovery directory.");
                return id;
            }).ToArray();
        }

        private Journal ReadJournal(Guid id)
        {
            string path = JournalPath(id);
            var journal = ParseJournal(File.ReadAllBytes(path));
            if (journal == null || journal.SchemaVersion != 1 || journal.OperationId != id || journal.OperationId == Guid.Empty ||
                !Enum.IsDefined(journal.Phase) || !Enum.IsDefined(journal.Kind) || journal.Files == null || journal.Before == null || journal.After == null ||
                journal.AffectedPaths == null || journal.ChangedIds == null ||
                string.IsNullOrEmpty(journal.RequestFingerprint) || string.IsNullOrEmpty(journal.BeforeFingerprint))
                throw new InvalidDataException("Invalid journal.");
            _store.Validate(journal.Before); _store.Validate(journal.After);
            if (journal.Before.CommittedOperationIds.Contains(id) ||
                !journal.After.CommittedOperationIds.Where(marker => marker != id).SequenceEqual(journal.Before.CommittedOperationIds) ||
                ((journal.Phase == OperationPhase.Completed || journal.Phase == OperationPhase.StateCommitted) &&
                    (!journal.After.CommittedOperationIds.Contains(id) || journal.After.Revision != journal.Before.Revision + 1)))
                throw new InvalidDataException("Journal state/marker contract is inconsistent.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var delta in journal.Files)
            {
                if (delta == null || !seen.Add(Resolve(delta.Path)) || !ManagedDeltaPath(journal, delta)) throw new InvalidDataException("Invalid journal path.");
                if (!delta.Directory)
                {
                    if (delta.BeforeExists)
                    {
                        ValidatePayloadReference(delta.BeforePayload, delta.BeforeHash);
                        if (!journal.Purged) ReadPayload(id, delta.BeforePayload, delta.BeforeHash);
                    }
                    else if (delta.BeforePayload != null || delta.BeforeHash != null) throw new InvalidDataException("Invalid before payload.");
                    if (delta.AfterExists)
                    {
                        ValidatePayloadReference(delta.AfterPayload, delta.AfterHash);
                        if (!journal.Purged) ReadPayload(id, delta.AfterPayload, delta.AfterHash);
                    }
                    else if (delta.AfterPayload != null || delta.AfterHash != null) throw new InvalidDataException("Invalid after payload.");
                    if (delta.CapturedBefore != null) ValidatePayloadReference(delta.CapturedBefore, delta.BeforeHash);
                    if (delta.CapturedRollback != null) ValidatePayloadReference(delta.CapturedRollback, delta.AfterHash);
                    if (!journal.Purged)
                    {
                        VerifyOptionalCaptured(journal, delta.CapturedBefore, delta.BeforeHash);
                        VerifyOptionalCaptured(journal, delta.CapturedRollback, delta.AfterHash);
                    }
                    if (!IsIconAsset(delta.Path) && !new[] { ".lnk", ".url" }.Contains(Path.GetExtension(delta.Path).ToLowerInvariant()))
                        throw new InvalidDataException("Journal cannot mutate raw payloads.");
                }
            }
            return journal;
        }

        private static Journal ParseJournal(byte[] bytes)
        {
            var journal = JsonSerializer.Deserialize<Journal>(bytes, LauncherStateStore.JsonOptions);
            if (journal == null || string.IsNullOrEmpty(journal.IntegrityHash)) throw new InvalidDataException("Journal integrity hash is missing.");
            string integrity = journal.IntegrityHash;
            journal.IntegrityHash = null;
            bool intact = integrity == Hash(JsonSerializer.SerializeToUtf8Bytes(journal, LauncherStateStore.JsonOptions));
            journal.IntegrityHash = integrity;
            if (!intact) throw new InvalidDataException("Journal integrity check failed.");
            return journal;
        }

        private bool ManagedDeltaPath(Journal journal, FileDelta delta)
        {
            if (IsIconAsset(delta.Path) || delta.Directory && (SamePath(delta.Path, "Assets") || SamePath(delta.Path, Path.Combine("Assets", "Icons")))) return true;
            if (delta.Directory && SamePath(delta.Path, "Profiles")) return true;
            return journal.Before.Profiles.Concat(journal.After.Profiles).Any(profile =>
                delta.Directory && SamePath(delta.Path, profile.RelativeRoot) || Under(delta.Path, profile.RelativeRoot));
        }

        private byte[] ReadPayload(Guid id, string name, string expected)
        {
            ValidatePayloadReference(name, expected);
            byte[] bytes = File.ReadAllBytes(SafePath.Resolve(OperationDirectory(id), name));
            if (Hash(bytes) != expected) throw new InvalidDataException("Payload checksum failed.");
            return bytes;
        }

        private static void ValidatePayloadReference(string name, string hash)
        {
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || string.IsNullOrEmpty(hash) || hash.Length != 64 ||
                hash.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("Payload reference is invalid.");
        }

        private long PayloadLength(Guid id, string name)
        {
            if (name == null) return 0;
            string path = SafePath.Resolve(OperationDirectory(id), name);
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
        private string OperationDirectory(Guid id) => Resolve(Path.Combine("Recovery", id.ToString("N")));
        private string JournalPath(Guid id) => SafePath.Resolve(OperationDirectory(id), "journal.json");
        private string Resolve(string relative) => SafePath.Resolve(_root, relative);
        private byte[] ReadManaged(string relative) => File.ReadAllBytes(Resolve(relative));
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        private static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
        private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value));
        private static string Join(string parent, string leaf) => string.IsNullOrEmpty(parent) ? leaf : Path.Combine(parent, leaf);
        private static bool SamePath(string left, string right) => string.Equals((left ?? "").Replace('/', '\\'), (right ?? "").Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        private static bool Under(string child, string parent) => (child ?? "").Replace('/', '\\').StartsWith((parent ?? "").Replace('/', '\\').TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        private static string ReplacePrefix(string value, string from, string to) => SamePath(value, from) ? to : Join(to, value[(from.Length + 1)..]);
        private static string DataRelative(ProfileState profile, string relative) => Join(profile.RelativeRoot, relative);
        private string EntryPath(LauncherState state, ShortcutEntry entry) => Resolve(DataRelative(state.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath));
        private static ProfileState ActiveProfile(LauncherState state, Guid id) => state.Profiles.SingleOrDefault(p => p.Id == id && !p.Suppressed)
            ?? throw new InvalidDataException("Unknown or archived profile.");

        private static string Leaf(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != Path.GetFileName(value)) throw new InvalidDataException("A leaf name is required.");
            return value;
        }

        private static (Guid Id, string Relative, bool Folder) SelectionRoot(LauncherState state, Guid id, Guid profileId)
        {
            var entry = state.Shortcuts.SingleOrDefault(s => s.Id == id && s.ProfileId == profileId && !s.Suppressed);
            if (entry != null) return (id, entry.RelativePath, false);
            var folder = state.Folders.SingleOrDefault(f => f.Id == id && f.ProfileId == profileId && !f.Suppressed);
            if (folder != null) return (id, folder.RelativePath, true);
            throw new InvalidDataException("Unknown selected identity.");
        }

        private static (ProfileState Profile, string Relative) FindProfileBinding(LauncherState state, string dataRelative)
        {
            var profile = state.Profiles.Where(p => !p.Suppressed && Under(dataRelative, p.RelativeRoot))
                .OrderByDescending(p => p.RelativeRoot.Length).FirstOrDefault() ?? throw new InvalidDataException("Binding is outside active profiles.");
            return (profile, dataRelative[(profile.RelativeRoot.Length + 1)..]);
        }

        private static Guid[] ChangedIds(LauncherState before, LauncherState after) => after.Shortcuts.Where(s =>
            !before.Shortcuts.Any(b => b.Id == s.Id && Same(b, s))).Select(s => s.Id)
            .Concat(after.Folders.Where(f => !before.Folders.Any(b => b.Id == f.Id && Same(b, f))).Select(f => f.Id)).ToArray();

        private static bool CanRestore(Journal journal) => !journal.Purged && journal.Phase == OperationPhase.Completed &&
            journal.Kind != ShortcutOperationKind.SaveSnapshot && journal.Kind != ShortcutOperationKind.Reconcile &&
            journal.Kind != ShortcutOperationKind.Restore;
        private static OperationResult Fail(OperationStatus status, string code, Guid id) =>
            new OperationResult { Status = status, Code = code, OperationId = id };
        private static OperationResult WithId(OperationResult result, Guid id) { result.OperationId = id; return result; }
        private static OperationResult Success(Journal journal) => new OperationResult { OperationId = journal.OperationId,
            NewRevision = journal.After.Revision, UndoToken = CanRestore(journal) ? journal.OperationId.ToString("N") : null };
        private static Dictionary<Guid, string> RestoreFileBindings(Journal journal) => journal.Before.Shortcuts.Where(entry =>
            journal.Files.Any(delta => !delta.Directory && delta.BeforeExists && !delta.AfterExists &&
                SamePath(delta.Path, DataRelative(journal.Before.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath))))
            .ToDictionary(entry => entry.Id, entry => DataRelative(journal.Before.Profiles.Single(p => p.Id == entry.ProfileId), entry.RelativePath));
        private void Publish(Journal journal) => Publish(new CatalogChangedEventArgs { Revision = journal.After.Revision,
            ProfileId = journal.ProfileId, ChangedIds = journal.ChangedIds, ChangeKind = journal.Kind });
        private void Publish(CatalogChangedEventArgs args)
        {
            if (CatalogChanged == null) return;
            foreach (EventHandler<CatalogChangedEventArgs> subscriber in CatalogChanged.GetInvocationList())
            {
                try { subscriber(this, args); }
                catch (Exception ex)
                { System.Diagnostics.Debug.WriteLine("Catalog observer failed after durable commit: " + ex.GetType().Name); }
            }
        }
    }
}
