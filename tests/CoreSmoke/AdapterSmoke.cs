using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class AdapterSmoke
{
    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        string Root(string name)
        {
            string root = Path.Combine(scratchRoot, "adapters", name);
            Directory.CreateDirectory(Path.Combine(root, "Shortcuts"));
            foreach (string file in new[] { "A.url", "C.url" })
                File.WriteAllText(Path.Combine(root, "Shortcuts", file), "[InternetShortcut]\nURL=https://example.invalid/" + file);
            var migration = new StateMigrationService(root);
            check(migration.Commit(migration.Preview()).Success, "Adapter fixture migration failed.");
            return root;
        }
        ShortcutManager Manager(SettingsService settings) => new ShortcutManager(settings, path => new ShortcutItem
        { Name = Path.GetFileNameWithoutExtension(path), ShortcutFilePath = path, TargetPath = "https://example.invalid/fixture" }, (_, _) => null, false);

        string cachedRoot = Root("cached-rename");
        var cachedSettings = new SettingsService(cachedRoot);
        Guid cachedId = cachedSettings.GetMetadata(Path.Combine(cachedRoot, "Shortcuts", "A.url")).Id;
        var originalMetadata = cachedSettings.GetMetadata(cachedId);
        originalMetadata.LaunchCount = 9;
        cachedSettings.SaveMetadata(originalMetadata);
        using (var manager = Manager(cachedSettings))
        {
            int refreshes = 0;
            manager.ShortcutsChanged += (_, _) => refreshes++;
            File.Move(Path.Combine(cachedRoot, "Shortcuts", "A.url"), Path.Combine(cachedRoot, "Shortcuts", "B.url"));
            Rename(manager, Path.Combine(cachedRoot, "Shortcuts"), "A.url", "B.url");
            Call(manager, "LoadShortcuts"); // Simulates a concurrent own-operation scan caching the new disk stamp.
            Call(manager, "OnDebounceTimerFired", (object)null);
            check(refreshes == 1, "A cached disk stamp discarded an unprocessed external rename.");
            manager.ReloadShortcuts();
            check(manager.GetAllShortcuts().Single(item => item.Id == cachedId).ShortcutFilePath.EndsWith("B.url") &&
                cachedSettings.GetMetadata(cachedId).LaunchCount == 9 && manager.GetAllShortcuts().Count == 2,
                "Manager lost identity/statistics when rename preceded reconciliation.");
        }

        string interleavedRoot = Root("rename-during-reconcile");
        var interleavedSettings = new SettingsService(interleavedRoot);
        Guid secondId = interleavedSettings.GetMetadata(Path.Combine(interleavedRoot, "Shortcuts", "C.url")).Id;
        using (var manager = Manager(interleavedSettings))
        {
            bool delivered = false;
            interleavedSettings.Operations.CatalogChanged += (_, args) =>
            {
                if (delivered || args.ChangeKind != ShortcutOperationKind.Reconcile) return;
                delivered = true;
                File.Move(Path.Combine(interleavedRoot, "Shortcuts", "C.url"), Path.Combine(interleavedRoot, "Shortcuts", "D.url"));
                Rename(manager, Path.Combine(interleavedRoot, "Shortcuts"), "C.url", "D.url");
            };
            File.Move(Path.Combine(interleavedRoot, "Shortcuts", "A.url"), Path.Combine(interleavedRoot, "Shortcuts", "B.url"));
            Rename(manager, Path.Combine(interleavedRoot, "Shortcuts"), "A.url", "B.url");
            manager.ReloadShortcuts();
            check(delivered, "Interleaved watcher event was not delivered during reconciliation.");
            manager.ReloadShortcuts();
            check(manager.GetAllShortcuts().Single(item => item.Id == secondId).ShortcutFilePath.EndsWith("D.url") &&
                manager.GetAllShortcuts().Count == 2, "Reconciliation cleared a rename mapping that arrived during commit.");
        }

        string reorderRoot = Root("failed-reorder");
        var staleSettings = new SettingsService(reorderRoot);
        using (var manager = Manager(staleSettings))
        {
            var group = manager.GetAllShortcuts();
            var before = group.ToDictionary(item => item.Id, item => item.SortOrder);
            var winner = new SettingsService(reorderRoot);
            winner.Settings.WidgetDisplayName = "new writer";
            winner.Save();
            bool conflict = false;
            try { manager.UpdateSortOrders(group, group.Select((item, index) => (item.Id, Order: index * 10)).ToDictionary(pair => pair.Id, pair => pair.Order)); }
            catch (StatePersistenceException ex) { conflict = ex.Result.Status == OperationStatus.Conflict; }
            check(conflict && group.All(item => item.SortOrder == before[item.Id]) &&
                new SettingsService(reorderRoot).StateSnapshot.Shortcuts.All(item => item.SortOrder == int.MaxValue),
                "A failed reorder changed live objects or persisted ordering.");
        }

        string draftRoot = Root("draft-and-reference");
        var settings = new SettingsService(draftRoot);
        var reference = settings.Settings;
        var draft = new SettingsDraft(settings.Settings);
        var metadata = settings.GetMetadata(Path.Combine(draftRoot, "Shortcuts", "A.url"));
        metadata.LaunchCount++;
        settings.SaveMetadata(metadata);
        check(ReferenceEquals(reference, settings.Settings) && draft.IsCurrent(settings.Settings),
            "Metadata-only refresh invalidated preferences or the widget's settings reference.");
        var external = new SettingsService(draftRoot);
        external.Settings.ShowNotifications = false;
        external.Save();
        settings.Load(); // The Settings UI checks this before changing OS registration or staging controls.
        check(ReferenceEquals(reference, settings.Settings) && !draft.IsCurrent(settings.Settings),
            "A saved preferences change escaped the editor baseline guard.");

        string retryRoot = Root("draft-retry");
        var retry = new SettingsService(retryRoot);
        var retryDraft = new SettingsDraft(retry.Settings);
        bool controlValue = false;
        retry.Settings.ShowNotifications = controlValue;
        using (var lease = new FileStream(Path.Combine(retryRoot, "state.writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            check(!retry.TrySave().Success, "Locked preferences save unexpectedly succeeded.");
        retry.Load();
        check(retryDraft.IsCurrent(retry.Settings), "A temporary save error caused a false preferences conflict on retry.");
        retry.Settings.ShowNotifications = controlValue;
        check(retry.TrySave().Success && !new SettingsService(retryRoot).Settings.ShowNotifications,
            "Retry after releasing the lock lost the retained editor input.");
        string linkRoot = Root("reparse-containment");
        string externalFolder = Path.Combine(scratchRoot, "adapters", "outside-managed-tree");
        Directory.CreateDirectory(externalFolder);
        string sentinel = Path.Combine(externalFolder, "sentinel.url");
        File.WriteAllText(sentinel, "outside managed tree; must stay untouched");
        bool symlinkCreated = false;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(linkRoot, "Shortcuts", "Escape"), externalFolder);
            symlinkCreated = true;
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("SKIP reparse fixture creation: symbolic-link privilege unavailable."); }
        catch (IOException ex) when ((ex.HResult & 0xffff) == 1314 || (ex.HResult & 0xffff) == 50)
        { Console.WriteLine("SKIP reparse fixture creation: filesystem/privilege does not permit symbolic links."); }
        if (symlinkCreated)
        {
            bool refused = false;
            try { SafePath.Resolve(linkRoot, Path.Combine("Shortcuts", "Escape", "sentinel.url")); }
            catch (InvalidDataException) { refused = true; }
            var reparseSettings = new SettingsService(linkRoot);
            var reconcile = reparseSettings.NewOperation(ShortcutOperationKind.Reconcile);
            check(refused && !reparseSettings.ExecuteOperation(reconcile).Success &&
                File.ReadAllText(sentinel) == "outside managed tree; must stay untouched", "Managed mutation traversed a real reparse point.");
            Console.WriteLine("PASS real scratch symbolic-link containment (no privilege changes)");
        }
        Console.WriteLine("PASS manager watcher interleaving, failed reorder, and settings draft guards (injected watcher events)");
        string adoptionRoot = Path.Combine(scratchRoot, "adapters", "explicit-adoption");
        Directory.CreateDirectory(Path.Combine(adoptionRoot, "Shortcuts"));
        File.WriteAllText(Path.Combine(adoptionRoot, "Shortcuts", "Adopt.url"), "[InternetShortcut]\nURL=https://example.invalid/adopt\n");
        var adoption = new SettingsService(adoptionRoot);
        using (var legacyManager = Manager(adoption))
        {
            check(!adoption.UsesStateV2, "Legacy manager migrated implicitly.");
            var migration = new StateMigrationService(adoptionRoot); check(migration.Commit(migration.Preview()).Success, "Explicit adoption fixture failed.");
            adoption.Load(); legacyManager.ReloadShortcuts(); int notifications = 0; legacyManager.ShortcutsChanged += (_, _) => notifications++;
            var pin = adoption.NewOperation(ShortcutOperationKind.Pin); pin.SelectedIds = new[] { adoption.StateSnapshot.Shortcuts.Single().Id };
            check(adoption.ExecuteOperation(pin).Success && notifications == 1 && legacyManager.CaptureMenuSnapshot().Pins.Single().Id == pin.SelectedIds[0], "Manager created before migration did not subscribe to v2 catalog changes.");
        }
    }

    private static void Rename(ShortcutManager manager, string directory, string oldName, string newName)
    {
        Call(manager, "OnFileSystemChanged", manager, new RenamedEventArgs(WatcherChangeTypes.Renamed, directory, newName, oldName));
        ((Timer)typeof(ShortcutManager).GetField("_debounceTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager))
            .Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void Call(ShortcutManager manager, string method, params object[] args) =>
        typeof(ShortcutManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, args);
}
