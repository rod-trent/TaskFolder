using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class MigrationSmoke
{
    private static Action<bool, string> _check;
    private static string _root;

    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        _check = check;
        _root = Path.Combine(scratchRoot, "migration");
        Directory.CreateDirectory(_root);
        Group("migration inventory, collisions and idempotency", TestMigration);
        Group("duplicate legacy records remain unresolved", TestDuplicateLegacyRecords);
        Group("preview conflicts and corrupt legacy input", TestPreviewValidation);
        Group("state revisions, snapshots and refusal to downgrade", TestStateStore);
        Group("write faults retain readable state", TestWriteFaults);
        Group("locked state is not reported as saved", TestLockedState);
        Group("invalid state bindings are refused", TestInvalidState);
        Group("v2 facade isolates identity and legacy files", TestFacade);
        Group("manager consumers retain identity and missing definitions", TestManagerConsumers);
        Group("facade recovery is read-only and future schemas are refused", TestFacadeRecovery);
        Group("external edits and exclusive writers do not lose updates", TestConcurrentWrite);
        Group("existing URL add and profile creation register independent identities", TestExistingAddAndProfile);
    }

    private static void Group(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static string Fixture(string name)
    {
        string result = Path.Combine(_root, name);
        Directory.CreateDirectory(result);
        return result;
    }

    private static string Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return path;
    }

    private static LauncherState Clone(LauncherState state) =>
        JsonSerializer.Deserialize<LauncherState>(JsonSerializer.Serialize(state));

    private static LauncherState FreshState()
    {
        var state = new LauncherState();
        state.Profiles.Add(new ProfileState
        {
            Id = ProfileState.DefaultId, Name = "Default", RelativeRoot = "Shortcuts"
        });
        state.Shortcuts.Add(new ShortcutEntry
        {
            Id = Guid.NewGuid(), ProfileId = ProfileState.DefaultId,
            RelativePath = "Fixture.url", Kind = ShortcutKind.Url
        });
        return state;
    }

    private static void TestMigration()
    {
        string root = Fixture("inventory");
        string[] paths =
        {
            Write(root, Path.Combine("Shortcuts", "Shared.lnk"), "synthetic link one"),
            Write(root, Path.Combine("Shortcuts", "Nested", "Shared.lnk"), "synthetic link two"),
            Write(root, Path.Combine("Profiles", "Работа 작업", "Shared.lnk"), "synthetic link three"),
            Write(root, Path.Combine("Shortcuts", "Один 하나.url"), "[InternetShortcut]\nURL=https://example.invalid\n"),
            Write(root, Path.Combine("Shortcuts", "Malformed.lnk"), "not a Shell link"),
            Write(root, Path.Combine("Shortcuts", "Raw.exe"), "synthetic payload; never launch")
        };
        Directory.CreateDirectory(Path.Combine(root, "Shortcuts", "Пустая 빈", "Nested empty"));
        const string settingsJson = "{\"MenuFontSize\":12.5,\"ActiveProfileName\":\"Default\",\"FutureSetting\":{\"retain\":true}}";
        const string metadataJson = "[" +
            "{\"FileName\":\"Shared.lnk\",\"LaunchCount\":11,\"LastUsed\":\"2026-10-20T16:25:30\",\"Unknown\":{\"retain\":17}}," +
            "{\"FileName\":\"Один 하나.url\",\"LaunchCount\":13,\"LastUsed\":\"2026-10-20T16:25:30+09:00\",\"SortOrder\":4,\"Unknown\":\"preserved\"}," +
            "{\"FileName\":\"Stale.lnk\",\"LaunchCount\":7,\"Unknown\":[1,2,3]}]";
        Write(root, "settings.json", settingsJson);
        Write(root, "metadata.json", metadataJson);
        var before = paths.ToDictionary(path => path, File.ReadAllBytes);
        var migration = new StateMigrationService(root);
        var preview = migration.Preview();
        _check(preview.Result.Success && !preview.AlreadyMigrated, "Valid legacy fixture did not preview.");
        _check(preview.State.Shortcuts.Count == 6, "Migration dropped malformed, missing-target or raw executable definitions.");
        _check(preview.State.Profiles.Count == 2 && preview.State.Profiles.Any(p => p.Id == ProfileState.DefaultId && p.RelativeRoot == "Shortcuts"),
            "Default/named profile inventory moved or lost identity.");
        _check(preview.State.Folders.Any(f => f.RelativePath == Path.Combine("Пустая 빈", "Nested empty")),
            "Migration omitted empty Unicode folders.");
        _check(preview.State.Shortcuts.Select(s => s.Id).Distinct().Count() == 6 && preview.State.Shortcuts.All(s => s.Id != Guid.Empty),
            "Migration generated duplicate or empty shortcut IDs.");
        var shared = preview.State.Shortcuts.Where(s => Path.GetFileName(s.RelativePath) == "Shared.lnk").ToArray();
        _check(shared.Length == 3 && shared.All(s => s.LaunchCount == 0), "Ambiguous legacy count was duplicated or assigned arbitrarily.");
        var ambiguity = preview.State.LegacyAmbiguities.Single(a => a.OriginalRecord.GetProperty("FileName").GetString() == "Shared.lnk");
        _check(ambiguity.CandidateIds.OrderBy(id => id).SequenceEqual(shared.Select(s => s.Id).OrderBy(id => id)) &&
            ambiguity.OriginalRecord.GetProperty("Unknown").GetProperty("retain").GetInt32() == 17 &&
            ambiguity.OriginalRecord.GetProperty("LastUsed").GetString() == "2026-10-20T16:25:30",
            "Ambiguous source record/candidate mapping/timezone representation was lost.");
        var unique = preview.State.Shortcuts.Single(s => Path.GetFileName(s.RelativePath) == "Один 하나.url");
        _check(unique.LaunchCount == 13 && unique.SortOrder == 4 &&
            unique.LegacyRecord.Value.GetProperty("Unknown").GetString() == "preserved", "Unique legacy metadata was not carried intact.");
        _check(unique.LastUsedUtc.HasValue && unique.LastUsedUtc.Value == new DateTimeOffset(2026, 10, 20, 7, 25, 30, TimeSpan.Zero),
            "Explicit legacy timezone was not converted correctly.");
        _check(preview.AssignedLaunchCount == 13 && preview.UnresolvedLaunchCount == 18 &&
            preview.State.Shortcuts.Sum(s => s.LaunchCount) + preview.UnresolvedLaunchCount == 31,
            "Assigned plus unresolved legacy counters do not reconcile.");
        _check(preview.State.Settings.Language == "English" && preview.State.Settings.Theme == "Light" &&
            preview.State.LegacySettings.Value.GetProperty("FutureSetting").GetProperty("retain").GetBoolean(),
            "Legacy presentation defaults or unknown settings were discarded.");
        _check(!File.Exists(Path.Combine(root, "state.v2.json")) && !Directory.Exists(preview.SnapshotPath),
            "Preview wrote state or a migration snapshot.");
        var result = migration.Commit(preview);
        _check(result.Success && Directory.Exists(preview.SnapshotPath), "Explicit fixture cutover/snapshot failed.");
        _check(!Directory.EnumerateFiles(preview.SnapshotPath, "*.exe", SearchOption.AllDirectories).Any(),
            "Migration snapshot copied executable payloads.");
        _check(File.ReadAllText(Path.Combine(root, "settings.json")) == settingsJson &&
            File.ReadAllText(Path.Combine(root, "metadata.json")) == metadataJson &&
            before.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))), "Migration changed legacy JSON or launch bytes.");
        var reopened = new LauncherStateStore(root).Load();
        _check(reopened.Result.Success && reopened.State.Revision == 1 && reopened.State.Shortcuts.Count == 6,
            "Committed migration did not reopen as one coherent state.");
        var repeated = migration.Preview();
        _check(repeated.Result.Success && repeated.AlreadyMigrated &&
            repeated.State.Shortcuts.Select(s => s.Id).OrderBy(id => id).SequenceEqual(preview.State.Shortcuts.Select(s => s.Id).OrderBy(id => id)),
            "Repeat migration lost or regenerated IDs.");
        string committedBytes = File.ReadAllText(Path.Combine(root, "state.v2.json"));
        _check(migration.Commit(repeated).Success && File.ReadAllText(Path.Combine(root, "state.v2.json")) == committedBytes,
            "Repeat migration was not an idempotent no-op.");
    }

    private static void TestDuplicateLegacyRecords()
    {
        string root = Fixture("duplicate-records");
        Write(root, Path.Combine("Shortcuts", "One.lnk"), "fixture");
        Write(root, "metadata.json", "[{\"FileName\":\"One.lnk\",\"LaunchCount\":4,\"Extra\":\"first\"}," +
            "{\"FileName\":\"one.LNK\",\"LaunchCount\":5,\"Extra\":\"second\"}]");
        var preview = new StateMigrationService(root).Preview();
        _check(preview.Result.Success && preview.State.Shortcuts.Single().LaunchCount == 0 &&
            preview.State.LegacyAmbiguities.Count == 2 && preview.UnresolvedLaunchCount == 9 && preview.AssignedLaunchCount == 0,
            "Duplicate legacy records were overwritten or assigned to one shortcut.");
        _check(preview.State.LegacyAmbiguities.Select(a => a.OriginalRecord.GetProperty("Extra").GetString()).OrderBy(s => s)
            .SequenceEqual(new[] { "first", "second" }), "Duplicate raw legacy records lost original fields.");
    }

    private static void TestPreviewValidation()
    {
        string root = Fixture("stale-preview");
        string link = Write(root, Path.Combine("Shortcuts", "One.lnk"), "before preview");
        var migration = new StateMigrationService(root);
        var preview = migration.Preview();
        File.WriteAllText(link, "externally changed after preview");
        var conflict = migration.Commit(preview);
        _check(!conflict.Success && conflict.Status == OperationStatus.Conflict && !File.Exists(Path.Combine(root, "state.v2.json")),
            "Commit accepted a stale preview after external launch-definition changes.");
        string addedRoot = Fixture("added-after-preview");
        Write(addedRoot, Path.Combine("Shortcuts", "One.lnk"), "existing fixture");
        var addedMigration = new StateMigrationService(addedRoot);
        var addedPreview = addedMigration.Preview();
        Write(addedRoot, Path.Combine("Shortcuts", "New.url"), "external addition");
        _check(addedMigration.Commit(addedPreview).Status == OperationStatus.Conflict &&
            !File.Exists(Path.Combine(addedRoot, "state.v2.json")), "Commit missed a launch file added after preview.");
        string corruptRoot = Fixture("corrupt-legacy");
        Write(corruptRoot, "metadata.json", "{ broken");
        var corrupt = new StateMigrationService(corruptRoot);
        var invalid = corrupt.Preview();
        _check(!invalid.Result.Success && !corrupt.Commit(invalid).Success &&
            File.ReadAllText(Path.Combine(corruptRoot, "metadata.json")) == "{ broken" && !File.Exists(Path.Combine(corruptRoot, "state.v2.json")),
            "Corrupt legacy data became writable empty defaults.");
    }

    private static void TestStateStore()
    {
        string root = Fixture("store");
        var store = new LauncherStateStore(root);
        var empty = store.Load();
        _check(empty.Result.Success && empty.State == null, "Absent v2 state did not remain explicitly absent.");
        var original = FreshState();
        _check(store.Commit(original, 0).Success && original.Revision == 0, "First commit failed or mutated the caller snapshot.");
        var first = store.Load();
        _check(first.Result.Success && first.State.Revision == 1 && !first.ReadOnly && !string.IsNullOrEmpty(first.Fingerprint),
            "Initial commit did not return a readable version/fingerprint.");
        var updated = Clone(first.State);
        updated.Settings.WidgetDisplayName = "Saved together";
        updated.Shortcuts[0].LaunchCount = 4_294_967_300L;
        _check(store.Commit(updated, first.State.Revision, first.Fingerprint).Success, "Second atomic snapshot failed.");
        var second = store.Load();
        _check(second.State.Revision == 2 && second.State.Settings.WidgetDisplayName == "Saved together" &&
            second.State.Shortcuts[0].LaunchCount == 4_294_967_300L, "Settings/Int64 metadata did not persist together.");
        var backup = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(store.BackupPath));
        _check(backup.Revision == 1 && backup.Shortcuts[0].LaunchCount == 0, "Last-good backup was not the previous coherent revision.");
        string primary = File.ReadAllText(store.StatePath);
        _check(!store.Commit(first.State, 1, first.Fingerprint).Success && File.ReadAllText(store.StatePath) == primary,
            "Stale revision overwrote a newer snapshot.");
        var external = Clone(second.State);
        external.Settings.WidgetDisplayName = "External edit with same revision";
        File.WriteAllText(store.StatePath, JsonSerializer.Serialize(external));
        _check(store.Commit(second.State, 2, second.Fingerprint).Status == OperationStatus.Conflict,
            "Same-revision external edit bypassed fingerprint conflict detection.");
        external.SchemaVersion = 999;
        File.WriteAllText(store.StatePath, JsonSerializer.Serialize(external));
        var future = store.Load();
        _check(!future.Result.Success && future.Result.Status == OperationStatus.UnsupportedSchema && future.ReadOnly,
            "Future schema silently downgraded to a backup/default state.");
        string futureBytes = File.ReadAllText(store.StatePath);
        _check(!store.Commit(second.State, 2).Success && File.ReadAllText(store.StatePath) == futureBytes,
            "Future primary schema was overwritten.");
        File.WriteAllText(store.StatePath, "{\"SchemaVersion\":\"2\"}");
        var wrongSchemaKind = store.Load();
        _check(wrongSchemaKind.State != null && wrongSchemaKind.RecoveredFromBackup && wrongSchemaKind.ReadOnly,
            "Malformed schema kind bypassed backup recovery.");
        File.WriteAllText(store.StatePath, "{ corrupt primary");
        var recovered = store.Load();
        _check(recovered.State != null && recovered.State.Revision == 1 && recovered.RecoveredFromBackup && recovered.ReadOnly,
            "Corrupt primary did not expose a read-only last-good snapshot.");
        _check(!store.Commit(recovered.State, 1, recovered.Fingerprint).Success && File.ReadAllText(store.StatePath) == "{ corrupt primary",
            "Read-only backup recovery overwrote corrupt primary evidence.");
        File.WriteAllText(store.BackupPath, "{ corrupt backup");
        var broken = store.Load();
        _check(!broken.Result.Success && broken.State == null && broken.ReadOnly, "Two corrupt snapshots yielded writable defaults.");
        string orphanBackupRoot = Fixture("missing-primary-corrupt-backup");
        var orphanBackupStore = new LauncherStateStore(orphanBackupRoot);
        File.WriteAllText(orphanBackupStore.BackupPath, "{ corrupt backup without primary");
        var orphanBackup = orphanBackupStore.Load();
        _check(!orphanBackup.Result.Success && orphanBackup.ReadOnly && !orphanBackupStore.Commit(FreshState(), 0).Success,
            "Missing primary plus corrupt backup yielded writable empty defaults.");
    }

    private static void TestWriteFaults()
    {
        foreach (StateWritePhase phase in Enum.GetValues<StateWritePhase>())
        {
            string root = Fixture("fault-" + phase);
            var seed = new LauncherStateStore(root);
            _check(seed.Commit(FreshState(), 0).Success, "Could not seed write-fault fixture.");
            var prior = seed.Load();
            var candidate = Clone(prior.State);
            candidate.Settings.WidgetDisplayName = "candidate";
            var faulting = new LauncherStateStore(root, observed =>
            {
                if (observed == phase) throw new IOException("Synthetic disk/write fault", unchecked((int)0x80070070));
            });
            var result = faulting.Commit(candidate, prior.State.Revision, prior.Fingerprint);
            _check(!result.Success, "Injected write fault was reported as saved: " + phase);
            var readable = seed.Load();
            _check(readable.Result.Success && readable.State != null && readable.State.Shortcuts.Count == 1,
                "Write fault lost the readable coherent snapshot: " + phase);
            if (phase == StateWritePhase.BeforeWrite || phase == StateWritePhase.BeforeReplace)
                _check(readable.State.Revision == 1 && readable.State.Settings.WidgetDisplayName != "candidate",
                    "Pre-replace failure changed committed state: " + phase);
            else
            {
                _check(readable.State.Revision == 2 && readable.State.Settings.WidgetDisplayName == "candidate",
                    "Post-replace failure did not leave the complete candidate readable: " + phase);
                var backup = JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(seed.BackupPath));
                _check(backup.Revision == 1, "Post-replace fault lost prior last-good state: " + phase);
            }
            _check(candidate.Revision == 1, "Faulting commit mutated the caller snapshot.");
        }
    }

    private static void TestLockedState()
    {
        string root = Fixture("locked-state");
        var store = new LauncherStateStore(root);
        _check(store.Commit(FreshState(), 0).Success, "Could not seed locked-state fixture.");
        var prior = store.Load();
        byte[] before = File.ReadAllBytes(store.StatePath);
        using (var held = new FileStream(store.StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _check(!store.Commit(prior.State, prior.State.Revision, prior.Fingerprint).Success,
                "Exclusively locked primary was reported as saved.");
        }
        _check(before.SequenceEqual(File.ReadAllBytes(store.StatePath)), "Locked-state failure changed primary bytes.");
    }

    private static void TestInvalidState()
    {
        foreach (string path in new[] { @"..\escape.url", @"C:\outside.url", "Folder\\Name.url:stream", "CON.lnk" })
        {
            string root = Fixture("invalid-binding-" + Guid.NewGuid().ToString("N"));
            var state = FreshState();
            state.Shortcuts[0].RelativePath = path;
            _check(!new LauncherStateStore(root).Commit(state, 0).Success && !File.Exists(Path.Combine(root, "state.v2.json")),
                "Unsafe managed binding was persisted: " + path);
        }
        var duplicate = FreshState();
        var sameId = new ShortcutEntry { Id = duplicate.Shortcuts[0].Id, ProfileId = ProfileState.DefaultId, RelativePath = "Other.url", Kind = ShortcutKind.Url };
        duplicate.Shortcuts.Add(sameId);
        _check(!new LauncherStateStore(Fixture("duplicate-id")).Commit(duplicate, 0).Success, "Duplicate shortcut IDs were persisted.");
        var orphan = FreshState();
        orphan.Shortcuts[0].ProfileId = Guid.NewGuid();
        _check(!new LauncherStateStore(Fixture("orphan-profile")).Commit(orphan, 0).Success, "Orphan profile binding was persisted.");
        var pathCollision = FreshState();
        pathCollision.Shortcuts[0].RelativePath = "Nested/One.url";
        pathCollision.Shortcuts.Add(new ShortcutEntry
        {
            Id = Guid.NewGuid(), ProfileId = ProfileState.DefaultId,
            RelativePath = "Nested\\One.url", Kind = ShortcutKind.Url
        });
        _check(!new LauncherStateStore(Fixture("normalized-path-collision")).Commit(pathCollision, 0).Success,
            "Two normalized bindings to the same physical shortcut were persisted.");
    }

    private static void TestFacade()
    {
        string root = Fixture("facade");
        string first = Write(root, Path.Combine("Shortcuts", "Same.lnk"), "first link");
        string second = Write(root, Path.Combine("Shortcuts", "Nested", "Same.lnk"), "second link");
        const string legacyMetadata = "[{\"FileName\":\"Same.lnk\",\"LaunchCount\":19}]";
        Write(root, "settings.json", "{\"ActiveProfileName\":\"Default\"}");
        Write(root, "metadata.json", legacyMetadata);
        var migration = new StateMigrationService(root);
        _check(migration.Commit(migration.Preview()).Success, "Could not prepare v2 facade fixture.");
        var facade = new SettingsService(root);
        _check(facade.UsesStateV2 && facade.Revision == 1, "Facade did not use existing committed v2 state.");
        var one = facade.GetMetadata(first);
        var two = facade.GetMetadata(second);
        _check(one.Id != Guid.Empty && two.Id != Guid.Empty && one.Id != two.Id && one.LaunchCount == 0 && two.LaunchCount == 0,
            "Facade still mixed same-basename metadata.");
        one.LaunchCount = 4_294_967_300L;
        facade.SaveMetadata(one);
        _check(facade.GetMetadata(one.Id).LaunchCount == 4_294_967_300L && facade.GetMetadata(two.Id).LaunchCount == 0,
            "Stable-ID update mixed another entry or truncated launch count.");
        var reopened = new SettingsService(root);
        _check(reopened.GetMetadata(first).Id == one.Id && reopened.GetMetadata(one.Id).LaunchCount == 4_294_967_300L &&
            reopened.GetMetadata(second).LaunchCount == 0 && File.ReadAllText(Path.Combine(root, "metadata.json")) == legacyMetadata,
            "Facade restart lost identity or dual-wrote legacy metadata.");
        var stale = new SettingsService(root);
        reopened.Settings.WidgetDisplayName = "newer writer";
        reopened.Save();
        stale.Settings.WidgetDisplayName = "stale writer";
        _check(!stale.TrySave().Success && new SettingsService(root).Settings.WidgetDisplayName == "newer writer",
            "Facade reported success for a stale revision or overwrote newer settings.");
        bool visibleFailure = false;
        try { stale.Save(); }
        catch (StatePersistenceException) { visibleFailure = true; }
        _check(visibleFailure, "Save hid an explicit persistence conflict.");
    }

    private static void TestManagerConsumers()
    {
        string root = Fixture("manager-consumers");
        string firstPath = Write(root, Path.Combine("Shortcuts", "Shared.lnk"), "first PWA definition");
        string secondPath = Write(root, Path.Combine("Shortcuts", "Nested", "Shared.lnk"), "second PWA definition");
        string profilePath = Write(root, Path.Combine("Profiles", "Work", "Shared.lnk"), "profile PWA definition");
        string missingPath = Write(root, Path.Combine("Shortcuts", "Missing.lnk"), "missing local target definition");
        string malformedPath = Write(root, Path.Combine("Shortcuts", "Malformed.lnk"), "invalid definition");
        string target = Write(root, "target-fixture.exe", "synthetic executable; never launch");
        string icon = Write(root, "fixture.ico", "synthetic icon; injected reader only");
        var migration = new StateMigrationService(root);
        _check(migration.Commit(migration.Preview()).Success, "Could not seed manager-consumer migration.");
        var store = new LauncherStateStore(root);
        var initial = store.Load();
        var pinned = Clone(initial.State);
        var selected = pinned.Shortcuts.Single(s => s.ProfileId == ProfileState.DefaultId && s.RelativePath == "Shared.lnk");
        selected.IsPinned = true;
        selected.Aliases = new[] { "Редактор 편집기" };
        selected.LaunchCount = 4_294_967_300L;
        _check(store.Commit(pinned, initial.State.Revision, initial.Fingerprint).Success, "Could not seed additive pin/alias metadata.");
        var settings = new SettingsService(root);
        int iconReads = 0;
        using var manager = new ShortcutManager(settings,
            path => path == malformedPath ? null : new ShortcutItem
            {
                ShortcutFilePath = path, Name = Path.GetFileNameWithoutExtension(path),
                TargetPath = path == missingPath ? Path.Combine(root, "absent-fixture.exe") : target,
                WorkingDirectory = root, Arguments = path == firstPath ? "--app-id=one" : "--app-id=two"
            },
            (path, index) => { iconReads++; return null; }, false);
        var items = manager.GetAllShortcuts();
        _check(items.Count == 4 && items.Single(s => s.ShortcutFilePath == missingPath).Status == ShortcutStatus.MissingTarget &&
            items.Single(s => s.ShortcutFilePath == malformedPath).Status == ShortcutStatus.Invalid,
            "V2 manager hid malformed or absent-target definitions.");
        var first = items.Single(s => s.ShortcutFilePath == firstPath);
        var second = items.Single(s => s.ShortcutFilePath == secondPath);
        _check(first.Id == selected.Id && first.Id != second.Id && !first.Equals(second),
            "Manager/PWA equality collapsed stable shortcut identities.");
        manager.RecordLaunch(first);
        manager.UpdateSortOrder(first, 12);
        manager.SetCustomIcon(first, icon, 3);
        _check(settings.GetMetadata(first.Id).LaunchCount == 4_294_967_301L && settings.GetMetadata(second.Id).LaunchCount == 0 &&
            settings.GetMetadata(first.Id).SortOrder == 12 && settings.GetMetadata(second.Id).SortOrder == int.MaxValue && iconReads == 1,
            "A metadata consumer still used basename or bypassed injected icon reading.");
        long beforeRejectedRename = settings.Revision;
        foreach (string invalidName in new[] { @"..\escaped", "Folder/New", "CON", "Bad:name" })
            _check(!manager.RenameShortcut(first, invalidName) && File.ReadAllText(firstPath) == "first PWA definition" &&
                settings.Revision == beforeRejectedRename && settings.GetMetadata(first.Id).FileName == firstPath,
                "Invalid rename leaf mutated managed files/state: " + invalidName);
        _check(manager.RenameShortcut(first, "Renamed"), "Fixture rename failed.");
        string renamedPath = Path.Combine(root, "Shortcuts", "Renamed.lnk");
        var renamed = new SettingsService(root);
        var renamedState = renamed.StateSnapshot.Shortcuts.Single(s => s.Id == selected.Id);
        _check(renamed.GetMetadata(renamedPath).Id == selected.Id && renamedState.IsPinned &&
            renamedState.Aliases.SequenceEqual(new[] { "Редактор 편집기" }) && renamedState.LaunchCount == 4_294_967_301L &&
            renamedState.CustomIcon.Path == icon && renamedState.CustomIcon.Index == 3 &&
            File.ReadAllText(renamedPath) == "first PWA definition",
            "Rename changed identity/metadata or source launch bytes.");
        var profiles = new ProfileService(settings, manager);
        profiles.SwitchProfile("Work");
        var other = manager.GetAllShortcuts().Single();
        _check(other.ShortcutFilePath == profilePath && other.Id != selected.Id && other.LaunchCount == 0,
            "Switching profile mixed another same-basename metadata record.");
    }

    private static void TestFacadeRecovery()
    {
        string root = Fixture("facade-recovery");
        var store = new LauncherStateStore(root);
        _check(store.Commit(FreshState(), 0).Success, "Could not seed facade recovery.");
        var first = store.Load();
        _check(store.Commit(first.State, first.State.Revision, first.Fingerprint).Success, "Could not seed facade backup.");
        File.WriteAllText(store.StatePath, "{ damaged primary evidence");
        var readOnly = new SettingsService(root);
        _check(readOnly.UsesStateV2 && readOnly.StateLoad.RecoveredFromBackup && readOnly.StateLoad.ReadOnly && readOnly.Revision == 1,
            "Facade did not expose backup recovery as a read-only v2 state.");
        readOnly.Settings.WidgetDisplayName = "must not save";
        _check(readOnly.TrySave().Status == OperationStatus.RecoveryRequired && File.ReadAllText(store.StatePath) == "{ damaged primary evidence",
            "Read-only facade repaired corrupt state without explicit recovery.");
        bool readOnlyFailure = false;
        try { readOnly.Save(); }
        catch (StatePersistenceException ex) { readOnlyFailure = ex.Result.Status == OperationStatus.RecoveryRequired; }
        _check(readOnlyFailure, "Read-only save hid its recovery requirement.");

        string futureRoot = Fixture("facade-future");
        var futureStore = new LauncherStateStore(futureRoot);
        var futureState = FreshState();
        futureState.SchemaVersion = 999;
        File.WriteAllText(futureStore.StatePath, JsonSerializer.Serialize(futureState));
        bool rejected = false;
        try { _ = new SettingsService(futureRoot); }
        catch (StatePersistenceException ex) { rejected = ex.Result.Status == OperationStatus.UnsupportedSchema; }
        _check(rejected && !File.Exists(Path.Combine(futureRoot, "settings.json")),
            "Future v2 schema fell back to a writable legacy facade.");
    }

    private static void TestConcurrentWrite()
    {
        string root = Fixture("mid-commit-external-change");
        var store = new LauncherStateStore(root);
        _check(store.Commit(FreshState(), 0).Success, "Could not seed concurrent-write fixture.");
        var initial = store.Load();
        var external = Clone(initial.State);
        external.Settings.WidgetDisplayName = "external change before replace";
        string externalBytes = JsonSerializer.Serialize(external);
        var staged = new LauncherStateStore(root, phase =>
        {
            if (phase == StateWritePhase.BeforeReplace) File.WriteAllText(store.StatePath, externalBytes);
        });
        var result = staged.Commit(initial.State, initial.State.Revision, initial.Fingerprint);
        _check(result.Status == OperationStatus.Conflict && File.ReadAllText(store.StatePath) == externalBytes,
            "External change during staging was overwritten by replace.");
        byte[] before = File.ReadAllBytes(store.StatePath);
        using (var heldWriter = new FileStream(Path.Combine(root, "state.writer.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
        {
            var latest = store.Load();
            _check(!store.Commit(latest.State, latest.State.Revision, latest.Fingerprint).Success,
                "A second writer acquired an exclusively held data-root writer lock.");
        }
        _check(before.SequenceEqual(File.ReadAllBytes(store.StatePath)), "Blocked second writer altered primary state.");

        var stale = new SettingsService(root);
        var winner = new SettingsService(root);
        Guid id = winner.StateSnapshot.Shortcuts.Single().Id;
        var winningMetadata = winner.GetMetadata(id);
        winningMetadata.LaunchCount = 23;
        winner.SaveMetadata(winningMetadata);
        var staleMetadata = stale.GetMetadata(id);
        staleMetadata.LaunchCount = 99;
        bool rejected = false;
        try { stale.SaveMetadata(staleMetadata); }
        catch (StatePersistenceException ex) { rejected = ex.Result.Status == OperationStatus.Conflict; }
        _check(rejected && new SettingsService(root).GetMetadata(id).LaunchCount == 23,
            "Stale metadata update overwrote newer statistics or hid the conflict.");
    }

    private static void TestExistingAddAndProfile()
    {
        string root = Fixture("existing-add-and-profile");
        string source = Write(root, "source-fixture.url", "[InternetShortcut]\nURL=https://example.invalid/new\n");
        byte[] bytes = File.ReadAllBytes(source);
        var migration = new StateMigrationService(root);
        _check(migration.Commit(migration.Preview()).Success, "Could not seed empty v2 add/profile fixture.");
        var settings = new SettingsService(root);
        using var manager = new ShortcutManager(settings, path => new ShortcutItem
        {
            ShortcutFilePath = path, Name = Path.GetFileNameWithoutExtension(path),
            TargetPath = "https://example.invalid/new", WorkingDirectory = root
        }, (path, index) => null, false);
        var first = manager.AddShortcut(source, "Created");
        _check(first != null && first.Id != Guid.Empty && first.ProfileId == ProfileState.DefaultId &&
            bytes.SequenceEqual(File.ReadAllBytes(first.ShortcutFilePath)),
            "Existing URL Add did not create a visible bound v2 shortcut with exact source bytes.");
        var profiles = new ProfileService(settings, manager);
        profiles.CreateProfile("Work");
        var workProfile = settings.StateSnapshot.Profiles.Single(p => p.Name == "Work");
        _check(workProfile.Id != Guid.Empty && workProfile.Id != ProfileState.DefaultId &&
            workProfile.RelativeRoot == Path.Combine("Profiles", "Work"), "Profile creation did not register a v2 identity.");
        profiles.SwitchProfile("Work");
        var second = manager.AddShortcut(source, "Created");
        _check(second != null && second.Id != first.Id && second.ProfileId == workProfile.Id &&
            bytes.SequenceEqual(File.ReadAllBytes(second.ShortcutFilePath)), "URL Add in another profile reused identity or altered launch bytes.");
        manager.RecordLaunch(second);
        var reopened = new SettingsService(root);
        _check(reopened.StateSnapshot.Profiles.Single(p => p.Name == "Work").Id == workProfile.Id &&
            reopened.GetMetadata(first.Id).LaunchCount == 0 && reopened.GetMetadata(second.Id).LaunchCount == 1 &&
            reopened.GetMetadata(first.ShortcutFilePath).Id == first.Id && reopened.GetMetadata(second.ShortcutFilePath).Id == second.Id,
            "Added/profile identities or independent statistics did not survive restart.");
        _check(bytes.SequenceEqual(File.ReadAllBytes(source)), "Existing URL Add altered its original source.");
    }
}
