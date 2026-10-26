using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class OperationsSmoke
{
    private static string _root;
    private static Action<bool, string> _check;

    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        _root = Path.Combine(scratchRoot, "operations");
        Directory.CreateDirectory(_root);
        _check = check;
        Group("remove/restore preserves bytes, metadata and raw payload", TestRemoveRestore);
        Group("occupied restore paths require a replacement name", TestOccupiedRestore);
        Group("move/copy and complete reorder preserve identity contracts", TestMoveCopyOrder);
        Group("validation, stale revisions and replay never double-apply", TestValidationAndReplay);
        Group("journal phase faults recover correctly after restart", TestCrashPhases);
        Group("external edits block destructive recovery", TestExternalRecoveryConflict);
        Group("folder and profile removal retains unknown contents", TestFolderProfileRemoval);
        Group("reconcile preserves known renames and suppressed identities", TestReconcile);
        Group("repair replaces link bytes without changing metadata", TestRepair);
        Group("corrupt journal blocks all new mutations", TestCorruptJournal);
        Group("cancellation before commit compensates files", TestCancellation);
        Group("explicit recovery cleanup retains replay protection", TestPurge);
        Group("raw executable restore cannot rename a metadata-only binding", TestProtectedRestoreName);
        Group("observer failures cannot change committed outcomes", TestObserverFailures);
        Group("concurrent file replacement is captured without losing external bytes", TestCaptureRace);
        Group("folder moves and copies exclude historical suppressed tombstones", TestFolderMoveWithTombstones);
    }

    private static void Group(string title, Action test) { test(); Console.WriteLine("PASS " + title); }
    private static string Fixture(string title, bool includeFolderTombstones = false)
    {
        string root = Path.Combine(_root, title);
        Directory.CreateDirectory(Path.Combine(root, "Shortcuts", "Folder", "Empty"));
        Directory.CreateDirectory(Path.Combine(root, "Profiles", "Work"));
        Write(root, "Shortcuts\\One.lnk", "opaque link bytes one");
        Write(root, "Shortcuts\\Two.url", "[InternetShortcut]\nURL=https://example.invalid/two\n");
        Write(root, "Shortcuts\\Folder\\Nested.lnk", "opaque nested link bytes");
        Write(root, "Shortcuts\\Folder\\Raw.exe", "protected executable placeholder; never launched");
        Write(root, "Shortcuts\\Folder\\Unknown.txt", "unmanaged payload");
        if (includeFolderTombstones)
        {
            Write(root, "Shortcuts\\Folder\\Gone.url", "removed URL definition");
            Write(root, "Shortcuts\\Folder\\Archived\\Only.url", "removed nested definition");
            Directory.CreateDirectory(Path.Combine(root, "Shortcuts", "Folder", "Archived", "Empty"));
        }
        var migration = new StateMigrationService(root);
        _check(migration.Commit(migration.Preview()).Success, "Could not seed operations fixture.");
        var store = new LauncherStateStore(root);
        var loaded = store.Load();
        var one = loaded.State.Shortcuts.Single(s => s.RelativePath == "One.lnk");
        one.LaunchCount = 17; one.IsPinned = true; one.PinnedOrder = 4; one.SortOrder = 23;
        one.Aliases = new[] { "editor", "редактор" }; one.LastUsedUtc = DateTimeOffset.Parse("2026-10-20T07:30:00Z");
        one.CustomIcon = new CustomIconReference { Source = IconSource.ExternalReference,
            Path = Path.Combine(root, "fixture.ico"), Index = 2 };
        _check(store.Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Could not seed metadata.");
        return root;
    }

    private static void Write(string root, string relative, string text)
    { string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
    private static StateLoadResult Load(string root) => new LauncherStateStore(root).Load();
    private static Guid Id(string root, string path) => Load(root).State.Shortcuts.Single(s => s.ProfileId == ProfileState.DefaultId && s.RelativePath == path).Id;
    private static OperationRequest Request(string root, ShortcutOperationKind kind, params Guid[] ids)
    {
        var loaded = Load(root);
        return new OperationRequest { Kind = kind, ProfileId = ProfileState.DefaultId, ExpectedRevision = loaded.State.Revision,
            ExpectedStateFingerprint = loaded.Fingerprint, SelectedIds = ids };
    }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static void TestRemoveRestore()
    {
        string root = Fixture("remove-restore");
        var engine = new ShortcutOperationService(root);
        var before = Load(root).State;
        Guid one = Id(root, "One.lnk"), raw = Id(root, "Folder\\Raw.exe");
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "Shortcuts", "One.lnk"));
        byte[] rawBytes = File.ReadAllBytes(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe"));
        int events = 0; engine.CatalogChanged += (_, _) => events++;
        var removal = Request(root, ShortcutOperationKind.Remove, one, raw);
        var result = engine.Execute(removal);
        _check(result.Success && result.UndoToken != null && events == 1, "Removal did not commit one notification/undo token.");
        var removed = Load(root).State;
        _check(removed.Shortcuts.Single(s => s.Id == one).Suppressed && removed.Shortcuts.Single(s => s.Id == raw).Suppressed &&
            !File.Exists(Path.Combine(root, "Shortcuts", "One.lnk")) &&
            rawBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe"))),
            "Removal lost raw payload or failed to suppress retained identities.");
        _check(new ShortcutOperationService(root).GetRecoveryOperations().Single(i => i.OperationId == removal.OperationId).CanRestore,
            "Recovery was not available after restart.");
        var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = removal.OperationId;
        _check(new ShortcutOperationService(root).Execute(restore).Success, "Recovery failed after restart.");
        var after = Load(root).State;
        _check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "Shortcuts", "One.lnk"))) &&
            Serialize(before.Shortcuts.Single(s => s.Id == one)) == Serialize(after.Shortcuts.Single(s => s.Id == one)) &&
            !after.Shortcuts.Single(s => s.Id == raw).Suppressed, "Restore lost bytes, ID, pin, alias, icon, order or statistics.");
        var repeat = Request(root, ShortcutOperationKind.Restore); repeat.RestoreOperationId = removal.OperationId;
        _check(!engine.Execute(repeat).Success, "A second restore duplicated an operation.");
    }

    private static void TestOccupiedRestore()
    {
        string root = Fixture("occupied"); Guid one = Id(root, "One.lnk");
        var engine = new ShortcutOperationService(root);
        var remove = Request(root, ShortcutOperationKind.Remove, one);
        _check(engine.Execute(remove).Success, "Could not remove occupied-path fixture.");
        Write(root, "Shortcuts\\One.lnk", "new unrelated file at original path");
        var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = remove.OperationId;
        _check(engine.Execute(restore).Status == OperationStatus.Conflict && File.ReadAllText(Path.Combine(root, "Shortcuts", "One.lnk")) == "new unrelated file at original path",
            "Restore overwrote a newly occupied original path.");
        restore.OperationId = Guid.NewGuid(); restore.ReplacementNames[one] = "Recovered.lnk";
        _check(engine.Execute(restore).Success && File.ReadAllText(Path.Combine(root, "Shortcuts", "Recovered.lnk")) == "opaque link bytes one" &&
            Load(root).State.Shortcuts.Single(s => s.Id == one).RelativePath == "Recovered.lnk",
            "Explicit replacement name failed to preserve recovered identity/bytes.");
    }

    private static void TestMoveCopyOrder()
    {
        string root = Fixture("move-copy-order"); Guid one = Id(root, "One.lnk");
        var engine = new ShortcutOperationService(root);
        Guid work = Load(root).State.Profiles.Single(p => p.Name == "Work").Id;
        var copy = Request(root, ShortcutOperationKind.Copy, one); copy.DestinationProfileId = work;
        _check(engine.Execute(copy).Success, "Cross-profile copy failed.");
        var copied = Load(root).State.Shortcuts.Single(s => s.ProfileId == work && s.RelativePath == "One.lnk");
        _check(copied.Id != one && copied.LaunchCount == 0 && copied.LastUsedUtc == null && !copied.IsPinned &&
            copied.Aliases.SequenceEqual(new[] { "editor", "редактор" }) && copied.CustomIcon.Index == 2,
            "Copy reused identity/stats/pin or lost aliases/icon.");
        var move = Request(root, ShortcutOperationKind.Move, one); move.DestinationFolder = "Folder";
        _check(engine.Execute(move).Success, "Move into folder failed.");
        var moved = Load(root).State.Shortcuts.Single(s => s.Id == one);
        _check(moved.RelativePath == "Folder\\One.lnk" && moved.LaunchCount == 17 && moved.IsPinned &&
            !File.Exists(Path.Combine(root, "Shortcuts", "One.lnk")) && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "One.lnk")),
            "Move lost identity/metadata or file binding.");
        var group = Load(root).State.Shortcuts.Where(s => s.ProfileId == ProfileState.DefaultId && Path.GetDirectoryName(s.RelativePath) == "Folder").ToArray();
        var order = Request(root, ShortcutOperationKind.Reorder, group.Select(s => s.Id).ToArray());
        order.Orders = group.Select((entry, index) => (entry.Id, index)).ToDictionary(p => p.Id, p => p.index);
        long revision = Load(root).State.Revision;
        _check(engine.Execute(order).Success && Load(root).State.Revision == revision + 1 && group.All(s =>
            Load(root).State.Shortcuts.Single(entry => entry.Id == s.Id).SortOrder == order.Orders[s.Id]), "Full-group reorder was partial or used several revisions.");
        var incomplete = Request(root, ShortcutOperationKind.Reorder, one); incomplete.Orders[one] = 0;
        _check(!engine.Execute(incomplete).Success, "Partial sibling reorder was accepted.");
        var unsafeMove = Request(root, ShortcutOperationKind.Move, Id(root, "Folder\\Raw.exe")); unsafeMove.DestinationProfileId = work;
        _check(!engine.Execute(unsafeMove).Success && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe")), "Raw executable was relocated as a link.");
    }

    private static void TestValidationAndReplay()
    {
        string root = Fixture("validation-replay"); var engine = new ShortcutOperationService(root); Guid one = Id(root, "One.lnk");
        int events = 0; engine.CatalogChanged += (_, _) => events++;
        var rename = Request(root, ShortcutOperationKind.Rename, one); rename.Name = "Renamed";
        _check(engine.Execute(rename).Success, "Rename failed."); long revision = Load(root).State.Revision;
        _check(engine.Execute(rename).Success && Load(root).State.Revision == revision && events == 1, "Replay duplicated a completed operation/event.");
        rename.Name = "Different";
        _check(engine.Execute(rename).Status == OperationStatus.Conflict, "Reused operation ID accepted another payload.");
        var stale = Request(root, ShortcutOperationKind.Remove, one); stale.ExpectedRevision--;
        _check(engine.Execute(stale).Status == OperationStatus.Conflict && File.Exists(Path.Combine(root, "Shortcuts", "Renamed.lnk")), "Stale operation changed files.");
        foreach (string invalid in new[] { "..\\escape", "CON", "name:stream" })
        {
            var bad = Request(root, ShortcutOperationKind.Rename, one); bad.Name = invalid;
            _check(!engine.Execute(bad).Success && Load(root).State.Revision == revision &&
                File.Exists(Path.Combine(root, "Shortcuts", "Renamed.lnk")), "Invalid rename escaped managed paths.");
        }
        var snapshot = Request(root, ShortcutOperationKind.SaveSnapshot); snapshot.Snapshot = Load(root).State;
        snapshot.Snapshot.Shortcuts.Single(s => s.Id == one).RelativePath = "Injected.lnk";
        _check(!engine.Execute(snapshot).Success && Load(root).State.Revision == revision, "SaveSnapshot changed a structural binding.");
        var same = Request(root, ShortcutOperationKind.SaveSnapshot); same.Snapshot = Load(root).State;
        same.Snapshot.CommittedOperationIds.Clear();
        _check(!engine.Execute(same).Success, "SaveSnapshot tampered with replay markers.");
    }

    private static void TestCrashPhases()
    {
        foreach (var phase in new[] { OperationPhase.Prepared, OperationPhase.FileApplied, OperationPhase.FilesApplied,
            OperationPhase.StatePersisted, OperationPhase.StateCommitted, OperationPhase.Completed })
        {
            string root = Fixture("fault-" + phase); Guid one = Id(root, "One.lnk"); var prior = Load(root);
            var request = Request(root, ShortcutOperationKind.Rename, one); request.Name = "After";
            var engine = new ShortcutOperationService(root, at => { if (at == phase) throw new IOException("Synthetic crash fault"); });
            _check(!engine.Execute(request).Success, "Crash fault was reported as success: " + phase);
            var restart = new ShortcutOperationService(root);
            bool committed = phase == OperationPhase.StatePersisted || phase == OperationPhase.StateCommitted || phase == OperationPhase.Completed;
            if (phase != OperationPhase.Completed)
                _check(!restart.InspectPending().Success && !restart.Execute(Request(root, ShortcutOperationKind.Remove, one)).Success,
                    "Pending journal did not block new mutation: " + phase);
            _check(restart.RecoverPending().Success && restart.InspectPending().Success, "Explicit restart recovery failed: " + phase);
            var current = Load(root).State;
            _check(current.Revision == prior.State.Revision + (committed ? 1 : 0) &&
                current.Shortcuts.Single(s => s.Id == one).RelativePath == (committed ? "After.lnk" : "One.lnk") &&
                File.ReadAllText(Path.Combine(root, "Shortcuts", committed ? "After.lnk" : "One.lnk")) == "opaque link bytes one" &&
                !File.Exists(Path.Combine(root, "Shortcuts", committed ? "One.lnk" : "After.lnk")),
                "Crash recovery failed to retain one coherent file/state result: " + phase);
            _check(restart.RecoverPending().Success, "Repeated recovery was not idempotent.");
            if (committed) _check(restart.Execute(request).Success && restart.Execute(request).NewRevision == prior.State.Revision + 1,
                "Recovered committed operation replay returned a stale revision.");
        }
    }

    private static void TestExternalRecoveryConflict()
    {
        string root = Fixture("external-recovery"); Guid one = Id(root, "One.lnk");
        var request = Request(root, ShortcutOperationKind.Rename, one); request.Name = "After";
        var engine = new ShortcutOperationService(root, phase => { if (phase == OperationPhase.FilesApplied) throw new IOException("Synthetic crash"); });
        _check(!engine.Execute(request).Success, "Could not interrupt external-edit fixture.");
        Write(root, "Shortcuts\\After.lnk", "new external content");
        var restart = new ShortcutOperationService(root);
        _check(!restart.RecoverPending().Success && File.ReadAllText(Path.Combine(root, "Shortcuts", "After.lnk")) == "new external content" &&
            !File.Exists(Path.Combine(root, "Shortcuts", "One.lnk")), "Recovery overwrote external edits or partially compensated before validation.");
    }

    private static void TestFolderProfileRemoval()
    {
        string root = Fixture("folder-removal"); var engine = new ShortcutOperationService(root); var state = Load(root).State;
        Guid folder = state.Folders.Single(f => f.ProfileId == ProfileState.DefaultId && f.RelativePath == "Folder").Id;
        var remove = Request(root, ShortcutOperationKind.Remove, folder);
        _check(engine.Execute(remove).Success && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Unknown.txt")) &&
            File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe")) &&
            !File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Nested.lnk")), "Folder removal deleted raw/unknown contents.");
        var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = remove.OperationId;
        _check(engine.Execute(restore).Success && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Nested.lnk")), "Nested folder recovery failed.");
        var create = Request(root, ShortcutOperationKind.CreateProfile); create.Name = "NewProfile";
        _check(engine.Execute(create).Success, "Journaled profile creation failed.");
        Guid profile = Load(root).State.Profiles.Single(p => p.Name == "NewProfile").Id;
        Write(root, "Profiles\\NewProfile\\Unknown.txt", "must stay");
        var delete = Request(root, ShortcutOperationKind.DeleteProfile); delete.ProfileId = profile;
        _check(engine.Execute(delete).Success && Load(root).State.Profiles.Single(p => p.Id == profile).Suppressed &&
            File.ReadAllText(Path.Combine(root, "Profiles", "NewProfile", "Unknown.txt")) == "must stay", "Profile deletion bypassed payload protection.");
        var bad = Request(root, ShortcutOperationKind.DeleteProfile); bad.ProfileId = ProfileState.DefaultId;
        _check(!engine.Execute(bad).Success, "Default profile was deleted.");
        var nested = Request(root, ShortcutOperationKind.Move, folder); nested.DestinationFolder = "Folder\\Empty";
        _check(!engine.Execute(nested).Success, "A folder moved inside its descendant.");
    }

    private static void TestReconcile()
    {
        string root = Fixture("reconcile"); Guid one = Id(root, "One.lnk"); var engine = new ShortcutOperationService(root);
        File.Move(Path.Combine(root, "Shortcuts", "One.lnk"), Path.Combine(root, "Shortcuts", "External.lnk"));
        var reconcile = Request(root, ShortcutOperationKind.Reconcile); reconcile.RenamedBindings["Shortcuts\\One.lnk"] = "Shortcuts\\External.lnk";
        _check(engine.Execute(reconcile).Success && Load(root).State.Shortcuts.Single(s => s.Id == one).RelativePath == "External.lnk" &&
            Load(root).State.Shortcuts.Single(s => s.Id == one).LaunchCount == 17, "Known external rename lost identity/metadata.");
        long revision = Load(root).State.Revision;
        _check(engine.Execute(Request(root, ShortcutOperationKind.Reconcile)).Success && Load(root).State.Revision == revision,
            "Own/no-op watcher refresh committed another revision.");
        var remove = Request(root, ShortcutOperationKind.Remove, Id(root, "Folder\\Raw.exe"));
        _check(engine.Execute(remove).Success, "Could not suppress raw entry.");
        Write(root, "Shortcuts\\New.url", "new external definition");
        _check(engine.Execute(Request(root, ShortcutOperationKind.Reconcile)).Success && Load(root).State.Shortcuts.Any(s => s.RelativePath == "New.url") &&
            Load(root).State.Shortcuts.Single(s => s.RelativePath == "Folder\\Raw.exe").Suppressed, "Reconcile lost additions or reintroduced suppressed raw entry.");
        File.Move(Path.Combine(root, "Shortcuts", "External.lnk"), Path.Combine(root, "Shortcuts", "Offline.lnk"));
        _check(engine.Execute(Request(root, ShortcutOperationKind.Reconcile)).Success &&
            Load(root).State.Shortcuts.Single(s => s.Id == one).RelativePath == "External.lnk" &&
            Load(root).State.Shortcuts.Single(s => s.RelativePath == "Offline.lnk").Id != one,
            "Offline rename guessed identity from a target or lost original metadata.");
    }

    private static void TestRepair()
    {
        string root = Fixture("repair"); Guid one = Id(root, "One.lnk"); var engine = new ShortcutOperationService(root);
        string old = Serialize(Load(root).State.Shortcuts.Single(s => s.Id == one));
        var repair = Request(root, ShortcutOperationKind.Repair, one); repair.ReplacementBytes = System.Text.Encoding.UTF8.GetBytes("repaired opaque link");
        _check(engine.Execute(repair).Success && File.ReadAllText(Path.Combine(root, "Shortcuts", "One.lnk")) == "repaired opaque link" &&
            Serialize(Load(root).State.Shortcuts.Single(s => s.Id == one)) == old, "Repair changed identity/metadata or failed to stage replacement.");
        var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = repair.OperationId;
        _check(engine.Execute(restore).Success && File.ReadAllText(Path.Combine(root, "Shortcuts", "One.lnk")) == "opaque link bytes one", "Repair undo failed.");
    }

    private static void TestCorruptJournal()
    {
        string root = Fixture("corrupt-journal"); var engine = new ShortcutOperationService(root); Guid one = Id(root, "One.lnk");
        var rename = Request(root, ShortcutOperationKind.Rename, one); rename.Name = "Done";
        _check(engine.Execute(rename).Success, "Could not seed corrupt journal fixture.");
        Write(root, "Recovery\\" + rename.OperationId.ToString("N") + "\\journal.json", "{ corrupt journal");
        long revision = Load(root).State.Revision;
        _check(!engine.InspectPending().Success && !engine.Execute(Request(root, ShortcutOperationKind.Remove, one)).Success &&
            !engine.RecoverPending().Success && Load(root).State.Revision == revision &&
            engine.GetRecoveryOperations().Any(info => info.Pending && info.Error != null), "Corrupt journal allowed mutation or destructive repair.");
        string parseableRoot = Fixture("parseable-corrupt-journal"); var other = new ShortcutOperationService(parseableRoot);
        Guid first = Id(parseableRoot, "One.lnk"); var change = Request(parseableRoot, ShortcutOperationKind.Rename, first); change.Name = "Done";
        _check(other.Execute(change).Success, "Could not seed parseable-corruption fixture.");
        string journalPath = Path.Combine(parseableRoot, "Recovery", change.OperationId.ToString("N"), "journal.json");
        var document = JsonNode.Parse(File.ReadAllText(journalPath));
        document["After"]["Shortcuts"][0]["LaunchCount"] = 999999;
        File.WriteAllText(journalPath, document.ToJsonString());
        _check(!other.InspectPending().Success && !other.Execute(Request(parseableRoot, ShortcutOperationKind.Remove, first)).Success &&
            !other.RecoverPending().Success && File.Exists(Path.Combine(parseableRoot, "Shortcuts", "Done.lnk")),
            "Parseable journal metadata corruption bypassed integrity checks.");
    }

    private static void TestCancellation()
    {
        foreach (var cancelAt in new[] { OperationPhase.Prepared, OperationPhase.FilesApplied })
        {
            string root = Fixture("cancel-" + cancelAt); Guid one = Id(root, "One.lnk");
            var request = Request(root, ShortcutOperationKind.Rename, one); request.Name = "Cancelled";
            var engine = new ShortcutOperationService(root, phase => { if (phase == cancelAt) request.Cancelled = true; });
            long revision = Load(root).State.Revision;
            _check(engine.Execute(request).Status == OperationStatus.Cancelled && Load(root).State.Revision == revision &&
                File.Exists(Path.Combine(root, "Shortcuts", "One.lnk")) && !File.Exists(Path.Combine(root, "Shortcuts", "Cancelled.lnk")) &&
                engine.InspectPending().Success, "Precommit cancellation did not restore the original consistent state: " + cancelAt);
        }
    }

    private static void TestPurge()
    {
        string root = Fixture("purge"); Guid one = Id(root, "One.lnk"); var engine = new ShortcutOperationService(root);
        var remove = Request(root, ShortcutOperationKind.Remove, one);
        _check(engine.Execute(remove).Success, "Could not seed recovery cleanup fixture.");
        var information = engine.GetRecoveryOperations().Single(info => info.OperationId == remove.OperationId);
        _check(information.CanRestore && information.PayloadBytes > 0 && information.OriginalItemPaths[one] == "Shortcuts\\One.lnk",
            "Recovery preview omitted bytes/original binding.");
        _check(engine.PurgeRecovery(new[] { remove.OperationId }).Success, "Explicit completed-operation cleanup failed.");
        var after = new ShortcutOperationService(root).GetRecoveryOperations().Single(info => info.OperationId == remove.OperationId);
        _check(!after.CanRestore && after.PayloadBytes == 0 && !after.Pending && engine.InspectPending().Success &&
            after.MetadataBytes > 0 && after.MetadataBytes < information.MetadataBytes &&
            !File.Exists(Path.Combine(root, "Recovery", remove.OperationId.ToString("N"), "journal.json.bak")),
            "Cleanup left broken journals or claimed purged recovery was restorable.");
        var tombstone = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "Recovery", remove.OperationId.ToString("N"), "journal.json")));
        _check(tombstone["Before"]["Shortcuts"].AsArray().Count == 0 && tombstone["After"]["Shortcuts"].AsArray().Count == 0 &&
            tombstone["Before"]["LegacySettings"] == null && tombstone["After"]["LegacyAmbiguities"].AsArray().Count == 0,
            "Explicit cleanup retained full metadata snapshots in its tombstone.");
        long revision = Load(root).State.Revision;
        _check(engine.Execute(remove).Success && engine.Execute(remove).NewRevision == revision && Load(root).State.Revision == revision &&
            engine.PurgeRecovery(new[] { remove.OperationId }).Success, "Cleanup lost replay protection or was not idempotent.");
        var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = remove.OperationId;
        _check(!engine.Execute(restore).Success, "Purged bytes were advertised as recoverable.");
        var another = Request(root, ShortcutOperationKind.Remove, Id(root, "Two.url"));
        _check(engine.Execute(another).Success, "Could not seed unknown recovery-content fixture.");
        Write(root, "Recovery\\" + another.OperationId.ToString("N") + "\\personal.txt", "retain unknown evidence");
        _check(!engine.PurgeRecovery(new[] { another.OperationId }).Success &&
            File.ReadAllText(Path.Combine(root, "Recovery", another.OperationId.ToString("N"), "personal.txt")) == "retain unknown evidence" &&
            engine.GetRecoveryOperations().Single(info => info.OperationId == another.OperationId).CanRestore,
            "Cleanup deleted unknown data or revoked undo before validating every selected record.");
        string interruptedRoot = Fixture("purge-interrupted"); Guid original = Id(interruptedRoot, "One.lnk");
        var interruptedRemove = Request(interruptedRoot, ShortcutOperationKind.Remove, original);
        var normal = new ShortcutOperationService(interruptedRoot);
        _check(normal.Execute(interruptedRemove).Success, "Could not seed interrupted cleanup fixture.");
        var interrupted = new ShortcutOperationService(interruptedRoot, phase => { if (phase == OperationPhase.PurgePrepared) throw new IOException("Synthetic cleanup interruption"); });
        _check(!interrupted.PurgeRecovery(new[] { interruptedRemove.OperationId }).Success &&
            !new ShortcutOperationService(interruptedRoot).GetRecoveryOperations().Single(info => info.OperationId == interruptedRemove.OperationId).CanRestore,
            "Interrupted cleanup did not persist an explicit non-restorable tombstone.");
        var retry = new ShortcutOperationService(interruptedRoot);
        long savedRevision = Load(interruptedRoot).State.Revision;
        _check(retry.PurgeRecovery(new[] { interruptedRemove.OperationId }).Success && retry.Execute(interruptedRemove).Success &&
            Load(interruptedRoot).State.Revision == savedRevision && retry.GetRecoveryOperations().Single(info => info.OperationId == interruptedRemove.OperationId).PayloadBytes == 0,
            "Interrupted cleanup could not retry safely or lost replay protection.");
    }

    private static void TestProtectedRestoreName()
    {
        string root = Fixture("protected-restore-name"); Guid raw = Id(root, "Folder\\Raw.exe"); var engine = new ShortcutOperationService(root);
        var remove = Request(root, ShortcutOperationKind.Remove, raw);
        _check(engine.Execute(remove).Success, "Could not seed protected restore fixture.");
        var information = engine.GetRecoveryOperations().Single(info => info.OperationId == remove.OperationId);
        _check(information.OriginalItemPaths.Count == 0 && information.ItemPaths.Contains("Shortcuts\\Folder\\Raw.exe"),
            "Metadata-only raw suppression was advertised as a physical file restore target.");
        var wrong = Request(root, ShortcutOperationKind.Restore); wrong.RestoreOperationId = remove.OperationId;
        wrong.ReplacementNames[raw] = "NotHere.exe";
        long revision = Load(root).State.Revision;
        _check(!engine.Execute(wrong).Success && Load(root).State.Revision == revision &&
            Load(root).State.Shortcuts.Single(s => s.Id == raw).RelativePath == "Folder\\Raw.exe" &&
            File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe")), "Replacement name drifted a metadata-only raw executable binding.");
        var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = remove.OperationId;
        _check(engine.Execute(restore).Success && !Load(root).State.Shortcuts.Single(s => s.Id == raw).Suppressed,
            "Raw executable did not restore by unsuppressing its original binding.");
    }

    private static void TestObserverFailures()
    {
        string root = Fixture("observer-failure"); Guid one = Id(root, "One.lnk"); var engine = new ShortcutOperationService(root);
        int observed = 0;
        engine.CatalogChanged += (_, _) => throw new IOException("Synthetic observer failure after commit");
        engine.CatalogChanged += (_, _) => observed++;
        var remove = Request(root, ShortcutOperationKind.Remove, one);
        var result = engine.Execute(remove);
        _check(result.Success && observed == 1 && Load(root).State.CommittedOperationIds.Contains(remove.OperationId) &&
            engine.InspectPending().Success, "Throwing observer masked a durable successful mutation or blocked later subscribers.");
        string recoveryRoot = Fixture("recovery-observer-failure"); Guid source = Id(recoveryRoot, "One.lnk");
        var interrupted = new ShortcutOperationService(recoveryRoot, phase => { if (phase == OperationPhase.Prepared) throw new IOException("Synthetic interrupted operation"); });
        var rename = Request(recoveryRoot, ShortcutOperationKind.Rename, source); rename.Name = "Never";
        _check(!interrupted.Execute(rename).Success, "Could not interrupt observer recovery fixture.");
        var recovery = new ShortcutOperationService(recoveryRoot); int recoveredEvents = 0;
        recovery.CatalogChanged += (_, _) => throw new InvalidOperationException("Synthetic recovery observer failure");
        recovery.CatalogChanged += (_, _) => recoveredEvents++;
        _check(recovery.RecoverPending().Success && recoveredEvents == 1 && recovery.InspectPending().Success,
            "Throwing observer changed an explicit recovery outcome or suppressed a later subscriber.");
    }

    private static void TestCaptureRace()
    {
        string root = Fixture("capture-race"); Guid one = Id(root, "One.lnk");
        var request = Request(root, ShortcutOperationKind.Rename, one); request.Name = "After";
        int apply = 0;
        var engine = new ShortcutOperationService(root, phase =>
        {
            if (phase == OperationPhase.BeforeFileApply && ++apply == 2)
                Write(root, "Shortcuts\\One.lnk", "concurrent external bytes after precondition check");
        });
        long revision = Load(root).State.Revision;
        _check(!engine.Execute(request).Success && Load(root).State.Revision == revision &&
            !engine.InspectPending().Success && !engine.RecoverPending().Success,
            "Concurrent source replacement was committed or destructively repaired.");
        string directory = Path.Combine(root, "Recovery", request.OperationId.ToString("N"));
        _check(Directory.GetFiles(directory, "*.captured-before").Any(path =>
            File.ReadAllText(path) == "concurrent external bytes after precondition check") &&
            Directory.GetFiles(directory, "*.before").Any(path => File.ReadAllText(path) == "opaque link bytes one"),
            "Concurrent replacement bytes or the original preimage were lost in the check/capture race.");
    }

    private static void TestFolderMoveWithTombstones()
    {
        foreach (var kind in new[] { ShortcutOperationKind.Rename, ShortcutOperationKind.Move, ShortcutOperationKind.Copy })
        {
            string root = Fixture("folder-tombstones-" + kind, includeFolderTombstones: true);
            var engine = new ShortcutOperationService(root);
            var store = new LauncherStateStore(root);
            var seed = store.Load();
            var active = seed.State.Shortcuts.Single(entry => entry.RelativePath == "Folder\\Nested.lnk");
            active.LaunchCount = 29; active.IsPinned = true; active.Aliases = new[] { "active nested alias" };
            _check(store.Commit(seed.State, seed.State.Revision, seed.Fingerprint).Success, "Could not seed nested folder metadata.");
            var original = Load(root).State;
            Guid folder = original.Folders.Single(item => item.ProfileId == ProfileState.DefaultId && item.RelativePath == "Folder").Id;
            Guid empty = original.Folders.Single(item => item.ProfileId == ProfileState.DefaultId && item.RelativePath == "Folder\\Empty").Id;
            Guid archived = original.Folders.Single(item => item.ProfileId == ProfileState.DefaultId && item.RelativePath == "Folder\\Archived").Id;
            Guid raw = original.Shortcuts.Single(item => item.RelativePath == "Folder\\Raw.exe").Id;
            Guid gone = original.Shortcuts.Single(item => item.RelativePath == "Folder\\Gone.url").Id;
            Guid work = original.Profiles.Single(profile => profile.Name == "Work").Id;
            OperationRequest FolderRequest()
            {
                var request = Request(root, kind, folder);
                if (kind == ShortcutOperationKind.Rename) request.Name = "RenamedFolder";
                if (kind == ShortcutOperationKind.Move) request.DestinationFolder = "Destination";
                if (kind == ShortcutOperationKind.Copy) request.DestinationProfileId = work;
                return request;
            }
            long protectedRevision = Load(root).State.Revision;
            _check(!engine.Execute(FolderRequest()).Success && Load(root).State.Revision == protectedRevision &&
                File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe")),
                "Folder operation relocated an active protected executable: " + kind);
            var removal = Request(root, ShortcutOperationKind.Remove, gone, raw, archived);
            _check(engine.Execute(removal).Success && !File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Gone.url")) &&
                !Directory.Exists(Path.Combine(root, "Shortcuts", "Folder", "Archived")),
                "Could not create absent link/folder tombstones for folder-operation fixture.");
            var before = Load(root).State;
            var removedEntries = before.Shortcuts.Where(entry => entry.Suppressed).ToDictionary(entry => entry.Id, Serialize);
            var removedFolders = before.Folders.Where(item => item.Suppressed).ToDictionary(item => item.Id, Serialize);
            byte[] rawBytes = File.ReadAllBytes(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe"));
            var result = engine.Execute(FolderRequest());
            _check(result.Success, "A folder operation tried reading an absent suppressed payload: " + kind);
            var after = Load(root).State;
            _check(removedEntries.All(pair => Serialize(after.Shortcuts.Single(entry => entry.Id == pair.Key)) == pair.Value) &&
                removedFolders.All(pair => Serialize(after.Folders.Single(item => item.Id == pair.Key)) == pair.Value),
                "Folder operation rebound or copied historical suppressed identities: " + kind);
            Guid destinationProfile = kind == ShortcutOperationKind.Copy ? work : ProfileState.DefaultId;
            string destinationFolder = kind == ShortcutOperationKind.Rename ? "RenamedFolder" :
                kind == ShortcutOperationKind.Move ? "Destination\\Folder" : "Folder";
            var destination = after.Shortcuts.Single(entry => entry.ProfileId == destinationProfile &&
                entry.RelativePath == destinationFolder + "\\Nested.lnk");
            string destinationRoot = Path.Combine(root, after.Profiles.Single(profile => profile.Id == destinationProfile).RelativeRoot);
            _check(destination.Aliases.SequenceEqual(new[] { "active nested alias" }) &&
                File.ReadAllText(Path.Combine(destinationRoot, destination.RelativePath)) == "opaque nested link bytes" &&
                Directory.Exists(Path.Combine(destinationRoot, destinationFolder, "Empty")) &&
                !File.Exists(Path.Combine(destinationRoot, destinationFolder, "Gone.url")) &&
                !Directory.Exists(Path.Combine(destinationRoot, destinationFolder, "Archived")),
                "Folder operation lost active/empty-folder content or copied suppressed content: " + kind);
            if (kind == ShortcutOperationKind.Copy)
                _check(destination.Id != active.Id && destination.LaunchCount == 0 && !destination.IsPinned &&
                    after.Shortcuts.Single(entry => entry.Id == active.Id).LaunchCount == 29 &&
                    !after.Shortcuts.Any(entry => entry.ProfileId == work && entry.Suppressed) &&
                    !after.Folders.Any(item => item.ProfileId == work && item.Suppressed),
                    "Folder copy reused identity/statistics or duplicated suppressed records.");
            else
                _check(destination.Id == active.Id && destination.LaunchCount == 29 && destination.IsPinned &&
                    after.Folders.Single(item => item.Id == folder).RelativePath == destinationFolder &&
                    after.Folders.Single(item => item.Id == empty).RelativePath == destinationFolder + "\\Empty",
                    "Folder move/rename lost active identities, pin or statistics.");
            _check(rawBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "Shortcuts", "Folder", "Raw.exe"))) &&
                File.ReadAllText(Path.Combine(root, "Shortcuts", "Folder", "Unknown.txt")) == "unmanaged payload",
                "Folder operation moved/deleted physically retained protected or unknown payload: " + kind);
            var restore = Request(root, ShortcutOperationKind.Restore); restore.RestoreOperationId = removal.OperationId;
            _check(engine.Execute(restore).Success && File.ReadAllText(Path.Combine(root, "Shortcuts", "Folder", "Gone.url")) == "removed URL definition" &&
                File.ReadAllText(Path.Combine(root, "Shortcuts", "Folder", "Archived", "Only.url")) == "removed nested definition",
                "Historical tombstones could not be restored separately after a folder operation: " + kind);
        }
    }
}
