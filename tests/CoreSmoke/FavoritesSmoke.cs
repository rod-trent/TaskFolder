using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class FavoritesSmoke
{
    private static string _root;
    private static Action<bool, string> _check;

    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        _root = Path.Combine(scratchRoot, "favorites");
        Directory.CreateDirectory(_root);
        _check = check;
        Group("favorites persist by identity without duplicating metadata", TestPinLifecycle);
        Group("favorite order is independent of folder order", TestPinOrder);
        Group("active profile snapshot keeps favorites and catalog together", TestProfiles);
        Group("eight direct favorites and complete overflow activate stable IDs", TestOverflow);
        Group("missing pinned targets remain visible", TestMissingPin);
        Group("rename/move/remove/restore preserve favorite metadata", TestOperations);
        Group("interleaved undo restores visible pin positions and keeps survivor order", TestInterleavedUndo);
        Group("cross-profile favorite moves append safely and undo the source position", TestCrossProfilePinMove);
        Group("stale, incomplete and cross-profile favorite requests are refused", TestValidation);
        Group("Recent and Most Used visibility settings are independent", TestVisibility);
        Group("S3 resources and keyboard context-key contract are complete", TestPresentationContract);
    }

    private static void Group(string title, Action test) { test(); Console.WriteLine("PASS " + title); }

    private static string Fixture(string name)
    {
        string root = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(root, "Shortcuts", "Nested", "Empty"));
        Directory.CreateDirectory(Path.Combine(root, "Profiles", "Work"));
        for (int index = 0; index < 10; index++) Write(root, "Shortcuts\\Item" + index.ToString("D2") + ".url", "[InternetShortcut]\nURL=https://example.invalid/" + index + "\n");
        Write(root, "Shortcuts\\Nested\\Rock & Roll.url", "[InternetShortcut]\nURL=https://example.invalid/nested\n");
        Write(root, "Shortcuts\\Missing.lnk", "opaque missing-target definition; never parsed natively");
        Write(root, "Profiles\\Work\\Item00.url", "[InternetShortcut]\nURL=https://example.invalid/work\n");
        Write(root, "Profiles\\Work\\Work only.url", "[InternetShortcut]\nURL=https://example.invalid/work-only\n");
        Write(root, "fixture.ico", "fake icon; injected reader only");
        var migration = new StateMigrationService(root);
        _check(migration.Commit(migration.Preview()).Success, "Could not seed favorite fixture.");
        var store = new LauncherStateStore(root);
        var loaded = store.Load();
        for (int index = 0; index < loaded.State.Shortcuts.Count; index++) loaded.State.Shortcuts[index].SortOrder = index * 10;
        foreach (var entry in loaded.State.Shortcuts.Where(s => s.ProfileId == ProfileState.DefaultId && s.RelativePath.StartsWith("Item")))
        {
            int index = int.Parse(entry.RelativePath.Substring(4, 2), CultureInfo.InvariantCulture);
            if (index < 3) { entry.LaunchCount = 30 - index; entry.LastUsedUtc = DateTimeOffset.Parse("2026-10-20T07:30:00Z").AddMinutes(index); }
        }
        var first = loaded.State.Shortcuts.Single(s => s.ProfileId == ProfileState.DefaultId && s.RelativePath == "Item00.url");
        first.Aliases = new[] { "editor", "редактор 편집기" };
        first.CustomIcon = new CustomIconReference { Source = IconSource.ExternalReference, Path = Path.Combine(root, "fixture.ico"), Index = 3 };
        _check(store.Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Could not seed favorite metadata.");
        return root;
    }

    private static void Write(string root, string relative, string content)
    { string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content); }
    private static StateLoadResult Load(string root) => new LauncherStateStore(root).Load();
    private static ShortcutManager Manager(SettingsService settings) => new ShortcutManager(settings, path => new ShortcutItem
    {
        Name = Path.GetFileNameWithoutExtension(path), ShortcutFilePath = path,
        TargetPath = Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(settings.DataRoot, "absent-target.exe") : "https://example.invalid/fixture"
    }, (_, _) => null, false);
    private static ShortcutItem Item(ShortcutManager manager, string relative) => manager.CaptureMenuSnapshot().AllShortcuts.Single(item =>
        item.ShortcutFilePath.EndsWith(Path.DirectorySeparatorChar + relative, StringComparison.OrdinalIgnoreCase));
    private static OperationRequest Request(string root, ShortcutOperationKind kind, Guid profileId, params Guid[] ids)
    {
        var loaded = Load(root);
        return new OperationRequest { Kind = kind, ProfileId = profileId, ExpectedRevision = loaded.State.Revision,
            ExpectedStateFingerprint = loaded.Fingerprint, SelectedIds = ids };
    }
    private static string ExceptPin(ShortcutEntry entry) => JsonSerializer.Serialize(new
    { entry.Id, entry.ProfileId, entry.RelativePath, entry.Kind, entry.Aliases, entry.SortOrder, entry.LaunchCount,
        entry.LastUsedUtc, entry.CustomIcon, entry.Suppressed, entry.LegacyRecord });

    private static void TestPinLifecycle()
    {
        string root = Fixture("pin-lifecycle");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var first = Item(manager, "Item00.url");
        var before = Load(root).State;
        string metadata = ExceptPin(before.Shortcuts.Single(entry => entry.Id == first.Id));
        byte[] bytes = File.ReadAllBytes(first.ShortcutFilePath);
        manager.SetPinned(first, true);
        var pinned = manager.CaptureMenuSnapshot();
        _check(pinned.Pins.Count == 1 && pinned.Pins[0].Id == first.Id && pinned.ProfileId == ProfileState.DefaultId,
            "Pin did not appear in the active profile snapshot.");
        var after = Load(root).State;
        _check(after.Shortcuts.Count == before.Shortcuts.Count && after.Shortcuts.Select(entry => entry.Id).Distinct().Count() == after.Shortcuts.Count &&
            ExceptPin(after.Shortcuts.Single(entry => entry.Id == first.Id)) == metadata && bytes.SequenceEqual(File.ReadAllBytes(first.ShortcutFilePath)),
            "Pin created another identity or changed launch bytes/statistics/folder metadata.");
        manager.SetPinned(first, true);
        _check(manager.CaptureMenuSnapshot().Pins.Count == 1, "Pinning an already pinned identity created a duplicate.");
        var reopened = new SettingsService(root);
        using var reopenedManager = Manager(reopened);
        _check(reopenedManager.CaptureMenuSnapshot().Pins.Single().Id == first.Id, "Favorites did not survive restart.");
        reopenedManager.SetPinned(reopenedManager.CaptureMenuSnapshot().Pins.Single(), false);
        _check(reopenedManager.CaptureMenuSnapshot().Pins.Count == 0 &&
            ExceptPin(Load(root).State.Shortcuts.Single(entry => entry.Id == first.Id)) == metadata, "Unpin changed metadata beyond pin fields.");
    }

    private static void TestPinOrder()
    {
        string root = Fixture("pin-order");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var items = manager.CaptureMenuSnapshot().AllShortcuts.Take(3).ToArray();
        foreach (var item in items) manager.SetPinned(item, true);
        var folderOrder = Load(root).State.Shortcuts.ToDictionary(entry => entry.Id, entry => entry.SortOrder);
        var desired = new[] { items[2].Id, items[0].Id, items[1].Id };
        manager.ReorderPins(desired);
        _check(manager.CaptureMenuSnapshot().Pins.Select(item => item.Id).SequenceEqual(desired), "Favorite reorder did not persist the full requested order.");
        _check(Load(root).State.Shortcuts.All(entry => entry.SortOrder == folderOrder[entry.Id]), "Favorite reorder changed folder ordering.");
        manager.SetPinned(items[0], false);
        manager.SetPinned(items[0], true);
        var appended = new[] { items[2].Id, items[1].Id, items[0].Id };
        _check(manager.CaptureMenuSnapshot().Pins.Select(item => item.Id).SequenceEqual(appended), "Re-pinning did not append after existing favorites.");
        using var restarted = Manager(new SettingsService(root));
        _check(restarted.CaptureMenuSnapshot().Pins.Select(item => item.Id).SequenceEqual(appended), "Favorite order was lost after restart.");
    }

    private static void TestProfiles()
    {
        string root = Fixture("profiles");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var profiles = new ProfileService(settings, manager);
        var defaultRow = Item(manager, "Item00.url");
        Guid defaultPin = defaultRow.Id;
        manager.SetPinned(defaultRow, true);
        profiles.SwitchProfile("Work");
        var work = manager.CaptureMenuSnapshot();
        _check(work.ProfileName == "Work" && work.ProfileId != ProfileState.DefaultId && work.Pins.Count == 0 &&
            work.AllShortcuts.Count == 2 && work.AllShortcuts.All(item => item.ProfileId == work.ProfileId) && work.MostUsed.Count == 0 && work.Recent.Count == 0,
            "Profile label/catalog/history/favorites came from different profiles.");
        bool staleCallbackRefused = false;
        try { manager.SetPinned(defaultRow, false); }
        catch (StatePersistenceException ex) { staleCallbackRefused = ex.Result.Status == OperationStatus.Conflict; }
        _check(staleCallbackRefused && Load(root).State.Shortcuts.Single(entry => entry.Id == defaultPin).IsPinned,
            "A stale favorite callback edited a shortcut from the previously active profile.");
        _check(LauncherMenuBuilder.ResolveActivation(work, defaultRow) == null,
            "A favorite row captured from the old profile could still activate after switching.");
        var workPin = Item(manager, "Item00.url");
        manager.SetPinned(workPin, true);
        _check(workPin.Id != defaultPin && manager.CaptureMenuSnapshot().Pins.Single().Id == workPin.Id,
            "Same-basename favorites were shared across profiles.");
        profiles.SwitchProfile("Default");
        var restored = manager.CaptureMenuSnapshot();
        _check(restored.ProfileName == "Default" && restored.ProfileId == ProfileState.DefaultId && restored.Pins.Single().Id == defaultPin &&
            restored.AllShortcuts.All(item => item.ProfileId == ProfileState.DefaultId) && restored.Revision == settings.Revision,
            "Switch-back did not restore a coherent active-profile snapshot.");
        _check(restored.FolderPaths.Contains("Nested") && restored.FolderPaths.Contains(Path.Combine("Nested", "Empty")),
            "Adding favorites hid existing empty/nested folder hierarchy.");
    }

    private static void TestOverflow()
    {
        string root = Fixture("overflow");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var ids = manager.CaptureMenuSnapshot().AllShortcuts.Select(item => item.Id).ToArray();
        var pin = Request(root, ShortcutOperationKind.Pin, ProfileState.DefaultId, ids);
        _check(settings.ExecuteOperation(pin).Success, "Batch pin failed.");
        manager.ReloadShortcuts();
        var snapshot = manager.CaptureMenuSnapshot();
        _check(snapshot.Pins.Count == 12 && snapshot.DirectPins.Count == 8 &&
            snapshot.DirectPins.Select(item => item.Id).SequenceEqual(snapshot.Pins.Take(8).Select(item => item.Id)), "Direct favorite limit changed saved pin count/order.");
        var activated = new List<Guid>();
        ToolStripMenuItem Build(ShortcutItem shortcut)
        {
            var row = new ToolStripMenuItem(shortcut.GetDisplayName()) { Tag = shortcut };
            row.Click += (_, _) => activated.Add(shortcut.Id);
            return row;
        }
        var rows = LauncherMenuBuilder.BuildFavorites(snapshot, Build, text => new ToolStripMenuItem(text), "English");
        try
        {
            var direct = rows.OfType<ToolStripMenuItem>().Where(row => row.Tag is ShortcutItem).ToArray();
            var overflow = rows.OfType<ToolStripMenuItem>().Single(row => row.DropDownItems.Count != 0);
            _check(direct.Length == 8 && overflow.DropDownItems.Count == 12 &&
                overflow.DropDownItems.Cast<ToolStripItem>().Select(row => ((ShortcutItem)row.Tag).Id).SequenceEqual(snapshot.Pins.Select(item => item.Id)),
                "Favorites overflow lost items, used wrong IDs, or exceeded direct-menu limit.");
            direct[0].PerformClick();
            ((ToolStripMenuItem)overflow.DropDownItems[11]).PerformClick();
            _check(activated.SequenceEqual(new[] { snapshot.Pins[0].Id, snapshot.Pins[11].Id }),
                "Favorite activation callbacks did not dispatch the selected stable IDs.");
            _check(snapshot.Pins.Any(item => item.Name == "Rock & Roll"), "Favorite projection changed a literal ampersand in a filename.");
        }
        finally { foreach (var row in rows) row.Dispose(); }
        _check(LauncherMenuBuilder.BuildFavorites(new LauncherMenuSnapshot(), Build, text => new ToolStripMenuItem(text), "English").Count == 0,
            "An empty favorites list created a decorative or inactive menu section.");
    }

    private static void TestMissingPin()
    {
        string root = Fixture("missing");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var missing = Item(manager, "Missing.lnk");
        manager.SetPinned(missing, true);
        var snapshot = manager.CaptureMenuSnapshot();
        _check(snapshot.Pins.Single().Id == missing.Id && snapshot.Pins.Single().Status == ShortcutStatus.MissingTarget,
            "A missing pinned target disappeared rather than remaining available for repair.");
        _check(File.Exists(missing.ShortcutFilePath) && !File.Exists(missing.TargetPath), "Missing-target fixture did not remain isolated.");
    }

    private static void TestOperations()
    {
        string root = Fixture("operations");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var first = Item(manager, "Item00.url");
        manager.SetPinned(first, true);
        manager.SetPinned(Item(manager, "Item01.url"), true);
        int rank = Load(root).State.Shortcuts.Single(entry => entry.Id == first.Id).PinnedOrder;
        byte[] bytes = File.ReadAllBytes(first.ShortcutFilePath);
        _check(manager.RenameShortcut(first, "Renamed"), "Pinned shortcut rename failed.");
        var moved = Request(root, ShortcutOperationKind.Move, ProfileState.DefaultId, first.Id); moved.DestinationFolder = "Nested";
        _check(settings.ExecuteOperation(moved).Success, "Pinned shortcut move failed.");
        manager.ReloadShortcuts();
        var entry = Load(root).State.Shortcuts.Single(item => item.Id == first.Id);
        _check(entry.IsPinned && entry.PinnedOrder == rank && entry.RelativePath == Path.Combine("Nested", "Renamed.url") &&
            entry.LaunchCount == 30 && entry.Aliases.SequenceEqual(new[] { "editor", "редактор 편집기" }), "Rename/move changed pin rank, identity or metadata.");
        var currentActivation = LauncherMenuBuilder.ResolveActivation(manager.CaptureMenuSnapshot(), first);
        _check(currentActivation != null && currentActivation.Id == first.Id &&
            currentActivation.ShortcutFilePath == Path.Combine(root, "Shortcuts", "Nested", "Renamed.url"),
            "Stable-ID activation did not resolve the newest renamed/moved file binding.");
        var remove = Request(root, ShortcutOperationKind.Remove, ProfileState.DefaultId, first.Id);
        _check(settings.ExecuteOperation(remove).Success, "Pinned shortcut removal failed.");
        manager.ReloadShortcuts();
        _check(!manager.CaptureMenuSnapshot().Pins.Any(item => item.Id == first.Id) && manager.CaptureMenuSnapshot().Pins.Count == 1,
            "Removed favorite remained launchable or removed another pin.");
        _check(LauncherMenuBuilder.ResolveActivation(manager.CaptureMenuSnapshot(), first) == null,
            "A removed favorite remained available through its old activation callback.");
        var restore = Request(root, ShortcutOperationKind.Restore, ProfileState.DefaultId); restore.RestoreOperationId = remove.OperationId;
        _check(settings.ExecuteOperation(restore).Success, "Pinned shortcut restore failed.");
        manager.ReloadShortcuts();
        entry = Load(root).State.Shortcuts.Single(item => item.Id == first.Id);
        _check(manager.CaptureMenuSnapshot().Pins.First().Id == first.Id && entry.PinnedOrder == rank &&
            bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "Shortcuts", entry.RelativePath))), "Undo did not restore original pin order/identity/bytes.");
        var copy = Request(root, ShortcutOperationKind.Copy, ProfileState.DefaultId, first.Id);
        copy.DestinationProfileId = Load(root).State.Profiles.Single(profile => profile.Name == "Work").Id;
        _check(settings.ExecuteOperation(copy).Success, "Pinned shortcut copy failed.");
        var copied = Load(root).State.Shortcuts.Single(item => item.ProfileId == copy.DestinationProfileId && item.RelativePath == "Renamed.url");
        _check(copied.Id != first.Id && !copied.IsPinned && copied.LaunchCount == 0 &&
            Load(root).State.Shortcuts.Single(item => item.Id == first.Id).IsPinned, "Copy inherited a pin/statistics or changed source favorite identity.");
    }

    private static void TestInterleavedUndo()
    {
        string root = Fixture("undo-pin-append");
        var settings = new SettingsService(root);
        using (var manager = Manager(settings))
        {
            _check(manager.RenameShortcut(Item(manager, "Item09.url"), "Zulu") &&
                manager.RenameShortcut(Item(manager, "Item00.url"), "Bravo") &&
                manager.RenameShortcut(Item(manager, "Item01.url"), "Charlie"), "Could not seed nonalphabetic favorite names.");
            var first = Item(manager, "Zulu.url");
            var second = Item(manager, "Bravo.url");
            var third = Item(manager, "Charlie.url");
            manager.SetPinned(first, true); manager.SetPinned(second, true);
            var remove = Request(root, ShortcutOperationKind.Remove, ProfileState.DefaultId, first.Id);
            _check(settings.ExecuteOperation(remove).Success, "Could not remove the first favorite.");
            manager.ReloadShortcuts();
            manager.SetPinned(third, true);
            var restore = Request(root, ShortcutOperationKind.Restore, ProfileState.DefaultId); restore.RestoreOperationId = remove.OperationId;
            _check(settings.ExecuteOperation(restore).Success, "Could not restore a favorite after appending another pin.");
            manager.ReloadShortcuts();
            var pins = manager.CaptureMenuSnapshot().Pins;
            _check(pins.Select(item => item.Id).SequenceEqual(new[] { first.Id, second.Id, third.Id }) &&
                pins.Select(item => item.PinnedOrder).Distinct().Count() == pins.Count,
                "Undo produced colliding pin ranks or sorted the restored first favorite alphabetically behind another item.");
        }

        string reorderedRoot = Fixture("undo-after-survivor-reorder");
        var reorderedSettings = new SettingsService(reorderedRoot);
        using (var manager = Manager(reorderedSettings))
        {
            var initial = new[] { Item(manager, "Item03.url"), Item(manager, "Item09.url"),
                Item(manager, "Item00.url"), Item(manager, "Item01.url") };
            foreach (var item in initial) manager.SetPinned(item, true);
            var remove = Request(reorderedRoot, ShortcutOperationKind.Remove, ProfileState.DefaultId, initial[1].Id);
            _check(reorderedSettings.ExecuteOperation(remove).Success, "Could not remove the second favorite.");
            manager.ReloadShortcuts();
            var survivors = new[] { initial[3].Id, initial[2].Id, initial[0].Id };
            manager.ReorderPins(survivors);
            var metadata = Load(reorderedRoot).State.Shortcuts.ToDictionary(entry => entry.Id, ExceptPin);
            var restore = Request(reorderedRoot, ShortcutOperationKind.Restore, ProfileState.DefaultId); restore.RestoreOperationId = remove.OperationId;
            _check(reorderedSettings.ExecuteOperation(restore).Success, "Could not restore after independently reordering surviving favorites.");
            manager.ReloadShortcuts();
            var pins = manager.CaptureMenuSnapshot().Pins;
            _check(pins.Select(item => item.Id).SequenceEqual(new[] { survivors[0], initial[1].Id, survivors[1], survivors[2] }) &&
                pins.Where(item => item.Id != initial[1].Id).Select(item => item.Id).SequenceEqual(survivors) &&
                pins.Select(item => item.PinnedOrder).Distinct().Count() == pins.Count,
                "Undo failed to insert at the saved visible ordinal or replaced the user's newer survivor ordering.");
            _check(Load(reorderedRoot).State.Shortcuts.Where(entry => entry.Id != initial[1].Id).All(entry => ExceptPin(entry) == metadata[entry.Id]),
                "Restoring a pin changed unrelated survivor metadata.");
        }

        string sparseRoot = Fixture("undo-unpin-sparse-ranks");
        var store = new LauncherStateStore(sparseRoot);
        var loaded = store.Load();
        var chosen = new[] { "Item09.url", "Item00.url", "Item01.url" }.Select(path =>
            loaded.State.Shortcuts.Single(entry => entry.ProfileId == ProfileState.DefaultId && entry.RelativePath == path)).ToArray();
        int[] ranks = { 100, 1000, 3000 };
        for (int index = 0; index < chosen.Length; index++) { chosen[index].IsPinned = true; chosen[index].PinnedOrder = ranks[index]; }
        _check(store.Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Could not seed sparse historical pin ranks.");
        var sparseSettings = new SettingsService(sparseRoot);
        using (var manager = Manager(sparseSettings))
        {
            var unpin = Request(sparseRoot, ShortcutOperationKind.Unpin, ProfileState.DefaultId, chosen[1].Id);
            _check(sparseSettings.ExecuteOperation(unpin).Success, "Could not unpin sparse-rank favorite.");
            manager.ReloadShortcuts();
            var added = Item(manager, "Item03.url"); manager.SetPinned(added, true);
            var survivors = new[] { added.Id, chosen[2].Id, chosen[0].Id };
            manager.ReorderPins(survivors);
            var restore = Request(sparseRoot, ShortcutOperationKind.Restore, ProfileState.DefaultId); restore.RestoreOperationId = unpin.OperationId;
            _check(sparseSettings.ExecuteOperation(restore).Success, "Recovery could not undo Unpin after later pin ordering changes.");
            manager.ReloadShortcuts();
            var pins = manager.CaptureMenuSnapshot().Pins;
            _check(pins.Select(item => item.Id).SequenceEqual(new[] { survivors[0], chosen[1].Id, survivors[1], survivors[2] }) &&
                pins.Select(item => item.PinnedOrder).Distinct().Count() == pins.Count,
                "Sparse saved rank was used as a list index instead of restoring the old visible position.");
        }
    }

    private static void TestValidation()
    {
        string root = Fixture("validation");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var first = Item(manager, "Item00.url");
        var second = Item(manager, "Item01.url");
        manager.SetPinned(first, true);
        var stale = Request(root, ShortcutOperationKind.Unpin, ProfileState.DefaultId, first.Id);
        manager.SetPinned(second, true);
        _check(new ShortcutOperationService(root).Execute(stale).Status == OperationStatus.Conflict &&
            Load(root).State.Shortcuts.Single(item => item.Id == first.Id).IsPinned, "Stale favorite action overwrote a newer snapshot.");
        long revision = settings.Revision;
        var partial = Request(root, ShortcutOperationKind.ReorderPins, ProfileState.DefaultId, first.Id); partial.Orders[first.Id] = 0;
        _check(!settings.ExecuteOperation(partial).Success && settings.Revision == revision, "Partial favorite reorder committed.");
        Guid workId = Load(root).State.Shortcuts.First(item => item.ProfileId != ProfileState.DefaultId).Id;
        var crossProfile = Request(root, ShortcutOperationKind.Pin, ProfileState.DefaultId, first.Id, workId);
        _check(!settings.ExecuteOperation(crossProfile).Success && !Load(root).State.Shortcuts.Single(item => item.Id == workId).IsPinned &&
            settings.Revision == revision, "Mixed-profile selection partially pinned another profile.");
        var remove = Request(root, ShortcutOperationKind.Remove, ProfileState.DefaultId, first.Id);
        _check(settings.ExecuteOperation(remove).Success, "Could not seed suppressed-pin validation.");
        var pinSuppressed = Request(root, ShortcutOperationKind.Pin, ProfileState.DefaultId, first.Id);
        _check(!settings.ExecuteOperation(pinSuppressed).Success && Load(root).State.Shortcuts.Single(item => item.Id == first.Id).Suppressed,
            "Pinning resurrected a suppressed item.");
    }

    private static void TestCrossProfilePinMove()
    {
        string root = Fixture("cross-profile-pins");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        var profiles = new ProfileService(settings, manager);
        _check(manager.RenameShortcut(Item(manager, "Item09.url"), "Zulu"), "Could not seed a uniquely named source favorite.");
        var source = Item(manager, "Zulu.url");
        manager.SetPinned(source, true);
        profiles.SwitchProfile("Work");
        var destinationPin = Item(manager, "Item00.url");
        manager.SetPinned(destinationPin, true);
        Guid work = manager.CaptureMenuSnapshot().ProfileId;
        profiles.SwitchProfile("Default");
        var move = Request(root, ShortcutOperationKind.Move, ProfileState.DefaultId, source.Id);
        move.DestinationProfileId = work;
        _check(settings.ExecuteOperation(move).Success && settings.Settings.ActiveProfileName == "Default",
            "Moving a favorite changed the active launch profile.");
        manager.ReloadShortcuts();
        var moved = Load(root).State;
        var workPins = moved.Shortcuts.Where(entry => entry.ProfileId == work && entry.IsPinned && !entry.Suppressed).OrderBy(entry => entry.PinnedOrder).ToArray();
        _check(workPins.Select(entry => entry.Id).SequenceEqual(new[] { destinationPin.Id, source.Id }) &&
            workPins.Select(entry => entry.PinnedOrder).Distinct().Count() == workPins.Length &&
            moved.Shortcuts.Single(entry => entry.Id == source.Id).IsPinned,
            "Cross-profile move collided with destination pin ranks or changed the existing destination ordering.");
        _check(LauncherMenuBuilder.ResolveActivation(manager.CaptureMenuSnapshot(), source) == null,
            "A moved-away favorite remained launchable from its source-profile menu callback.");
        var newSourcePin = Item(manager, "Item02.url");
        manager.SetPinned(newSourcePin, true);
        var destinationMetadata = ExceptPin(Load(root).State.Shortcuts.Single(entry => entry.Id == destinationPin.Id));
        var restore = Request(root, ShortcutOperationKind.Restore, ProfileState.DefaultId); restore.RestoreOperationId = move.OperationId;
        _check(settings.ExecuteOperation(restore).Success, "Could not undo cross-profile move after adding a new source pin.");
        manager.ReloadShortcuts();
        var sourcePins = manager.CaptureMenuSnapshot().Pins;
        var final = Load(root).State;
        _check(sourcePins.Select(item => item.Id).SequenceEqual(new[] { source.Id, newSourcePin.Id }) &&
            sourcePins.Select(item => item.PinnedOrder).Distinct().Count() == sourcePins.Count &&
            final.Shortcuts.Where(entry => entry.ProfileId == work && entry.IsPinned && !entry.Suppressed).Single().Id == destinationPin.Id &&
            ExceptPin(final.Shortcuts.Single(entry => entry.Id == destinationPin.Id)) == destinationMetadata &&
            settings.Settings.ActiveProfileName == "Default", "Undo did not restore the source slot or changed destination metadata/profile selection.");
    }

    private static void TestVisibility()
    {
        string root = Fixture("visibility");
        var settings = new SettingsService(root);
        using var manager = Manager(settings);
        manager.SetPinned(Item(manager, "Item00.url"), true);
        int catalogCount = manager.CaptureMenuSnapshot().AllShortcuts.Count;
        foreach (var flags in new[] { (Recent: false, Most: true), (Recent: true, Most: false), (Recent: false, Most: false), (Recent: true, Most: true) })
        {
            settings.Settings.ShowRecent = flags.Recent;
            settings.Settings.ShowMostUsed = flags.Most;
            settings.Save();
            var snapshot = manager.CaptureMenuSnapshot();
            _check(snapshot.ShowRecent == flags.Recent && snapshot.ShowMostUsed == flags.Most &&
                (snapshot.Recent.Count > 0) == flags.Recent && (snapshot.MostUsed.Count > 0) == flags.Most &&
                snapshot.Pins.Count == 1 && snapshot.AllShortcuts.Count == catalogCount,
                "History visibility toggles were coupled or affected favorites/catalog.");
            var reopened = new SettingsService(root);
            _check(reopened.Settings.ShowRecent == flags.Recent && reopened.Settings.ShowMostUsed == flags.Most,
                "History visibility settings did not persist independently.");
        }
    }

    private static void TestPresentationContract()
    {
        string[] keys = { "Menu_Favorites", "Menu_AllFavorites", "Menu_Profile", "Menu_Pin", "Menu_Unpin", "Menu_PinEarlier", "Menu_PinLater",
            "Settings_ShowRecent", "Settings_ShowMostUsed", "Settings_History" };
        foreach (string key in keys)
            _check(!string.IsNullOrWhiteSpace(LocalizationService.Get(key, "English")) && !string.IsNullOrWhiteSpace(LocalizationService.Get(key, "Русский")),
                "S3 resource missing in an explicit language: " + key);
        _check(LocalizationService.Get("Menu_Favorites", "Русский").Contains("избран", StringComparison.OrdinalIgnoreCase), "Russian favorites resource fell back to English.");
        foreach (string language in new[] { "English", "Русский" })
            _check(string.Format(CultureInfo.InvariantCulture, LocalizationService.Get("Menu_Profile", language), "Rock & Roll 작업").Contains("Rock & Roll 작업"),
                "Profile resource lost its formatting placeholder or Unicode name.");
        var profile = new LauncherMenuSnapshot { ProfileName = "Rock & Roll 작업" };
        _check(LauncherMenuBuilder.ProfileText(profile, "English") ==
            string.Format(CultureInfo.InvariantCulture, LocalizationService.Get("Menu_Profile", "English"), "Rock && Roll 작업") &&
            profile.ProfileName == "Rock & Roll 작업", "Profile menu escaping changed the stored display name or WinForms mnemonic text.");
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ko-KR");
            _check(LocalizationService.Get("Menu_Favorites", "System") == LocalizationService.Get("Menu_Favorites", "English"),
                "Unsupported system language did not fall back to English resources.");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        _check(LauncherMenuBuilder.IsContextKey(Keys.Apps) && LauncherMenuBuilder.IsContextKey(Keys.Shift | Keys.F10) &&
            !LauncherMenuBuilder.IsContextKey(Keys.F10) && !LauncherMenuBuilder.IsContextKey(Keys.Control | Keys.F10),
            "Keyboard context-menu shortcut recognition changed unrelated key combinations.");
        _check((int)ShortcutOperationKind.Reconcile == 12 && (int)ShortcutOperationKind.Pin == 13 &&
            (int)ShortcutOperationKind.Unpin == 14 && (int)ShortcutOperationKind.ReorderPins == 15,
            "Appending favorite operations changed existing numeric journal operation kinds.");
    }
}
