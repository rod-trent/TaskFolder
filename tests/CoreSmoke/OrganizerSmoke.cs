using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class OrganizerSmoke
{
    private static string _root;
    private static Action<bool, string> _check;
    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        _root = Path.Combine(scratchRoot, "organizer"); Directory.CreateDirectory(_root); _check = check;
        Group("organizer previews are read-only and batch moves preserve identity", BatchMove);
        Group("cross-profile copies reset usage/pin and retain empty folders", CopyFolders);
        Group("invalid batches, collisions and cancellation are atomic", Conflicts);
        Group("preview confirmation rejects changed files, directories and revisions", StalePreviews);
        Group("alias drafts survive refresh and merge only unchanged aliases", AliasDrafts);
        Group("reorder preserves stable blocks and rejects filtered views", Reorder);
        Group("search, status filters and drag tokens use stable identity", Presentation);
        Group("external link drops preserve bytes and reject unsupported batches", ExternalDrop);
        Group("safe removal keeps raw executables and unknown content", SafeRemove);
        Group("previewed operations recover after an injected file-phase failure", FailureRecovery);
        Group("v2 requirement and localized manager labels are explicit", Contracts);
    }
    private static void Group(string title, Action test) { test(); Console.WriteLine("PASS " + title); }
    private static string Fixture(string name)
    {
        string root = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(root, "Shortcuts", "Folder", "Empty"));
        Directory.CreateDirectory(Path.Combine(root, "Profiles", "Work"));
        foreach (string namePart in new[] { "One", "Two", "Three" }) Write(root, "Shortcuts\\" + namePart + ".url", "[InternetShortcut]\nURL=https://example.invalid/" + namePart + "\n");
        Write(root, "Shortcuts\\Missing.lnk", "opaque link definition; injected reader only");
        Write(root, "Shortcuts\\Folder\\Nested.url", "[InternetShortcut]\nURL=https://example.invalid/nested\n");
        Write(root, "Shortcuts\\Folder\\Unknown.txt", "unmanaged payload");
        Write(root, "Shortcuts\\Raw.exe", "protected executable placeholder; never launched");
        var migration = new StateMigrationService(root);
        _check(migration.Commit(migration.Preview()).Success, "Could not seed organizer fixture.");
        var store = new LauncherStateStore(root); var loaded = store.Load();
        for (int i = 0; i < loaded.State.Shortcuts.Count; i++) loaded.State.Shortcuts[i].SortOrder = i * 10;
        var one = loaded.State.Shortcuts.Single(entry => entry.RelativePath == "One.url");
        one.IsPinned = true; one.PinnedOrder = 0; one.LaunchCount = 17; one.Aliases = new[] { "редактор 편집기" };
        one.LastUsedUtc = DateTimeOffset.Parse("2026-10-20T07:30:00Z");
        one.CustomIcon = new CustomIconReference { Source = IconSource.ExternalReference, Path = Path.Combine(root, "fixture.ico"), Index = 3 };
        _check(store.Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Could not seed organizer metadata.");
        return root;
    }
    private static void Write(string root, string relative, string text)
    { string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
    private static StateLoadResult Load(string root) => new LauncherStateStore(root).Load();
    private static ShortcutManager Manager(SettingsService settings) => new ShortcutManager(settings, path => new ShortcutItem
    { Name = Path.GetFileNameWithoutExtension(path), ShortcutFilePath = path, TargetPath = Path.Combine(settings.DataRoot, "absent-target.exe") }, (_, _) => null, false);
    private static Guid Id(OrganizerSession session, string path) => session.Snapshot.Shortcuts.Single(entry => entry.ProfileId == ProfileState.DefaultId && entry.RelativePath == path).Id;
    private static Guid Work(OrganizerSession session) => session.Snapshot.Profiles.Single(profile => profile.Name == "Work").Id;
    private static string Metadata(ShortcutEntry entry) => JsonSerializer.Serialize(new { entry.Id, entry.Aliases, entry.LaunchCount, entry.LastUsedUtc, entry.CustomIcon, entry.IsPinned, entry.PinnedOrder, entry.SortOrder });
    private static string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path).Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static void BatchMove()
    {
        string root = Fixture("move"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        var one = Id(session, "One.url"); var two = Id(session, "Two.url"); var work = Work(session);
        var metadata = new[] { one, two }.ToDictionary(id => id, id => Metadata(session.Snapshot.Shortcuts.Single(entry => entry.Id == id)));
        var before = Files(root); var preview = session.Prepare(ShortcutOperationKind.Move, new[] { one, two }, work);
        _check(preview.Result.Success && Files(root).SequenceEqual(before), "Preview created a journal, changed state or file bytes.");
        _check(session.Commit(preview).Success, "Batch move failed.");
        _check(session.Snapshot.Shortcuts.Where(entry => metadata.ContainsKey(entry.Id)).All(entry => entry.ProfileId == work && Metadata(entry) == metadata[entry.Id]), "Move changed identities or metadata.");
        _check(session.Snapshot.Settings.ActiveProfileName == "Default" && manager.CaptureMenuSnapshot().ProfileId == ProfileState.DefaultId, "Move switched the launch profile.");
        session.Navigate(work, ""); _check(session.Rows().Count == 2 && settings.Settings.ActiveProfileName == "Default", "Browsing another profile activated it.");
        _check(!File.Exists(Path.Combine(root, "Shortcuts", "One.url")) && File.Exists(Path.Combine(root, "Profiles", "Work", "One.url")), "Move did not relocate exact files.");
        session.Navigate(ProfileState.DefaultId, "Folder\\Empty");
        var empty = session.Snapshot.Folders.Single(f => f.RelativePath == "Folder\\Empty").Id;
        var folder = session.Snapshot.Folders.Single(f => f.RelativePath == "Folder").Id;
        var nested = Id(session, "Folder\\Nested.url");
        _check(session.Commit(session.Prepare(ShortcutOperationKind.Move, new[] { folder }, work)).Success, "Folder move failed from a nested browsing location.");
        _check(session.ProfileId == work && session.FolderPath == "Folder\\Empty" && session.Snapshot.Folders.Single(f => f.Id == empty).ProfileId == work && session.Snapshot.Shortcuts.Single(e => e.Id == nested).ProfileId == work, "Folder move lost nested/empty identities or browsing location.");
        _check(settings.Settings.ActiveProfileName == "Default" && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Unknown.txt")), "Folder move changed launch profile or unmanaged data.");
    }
    private static void CopyFolders()
    {
        string root = Fixture("copy"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        Guid one = Id(session, "One.url"), work = Work(session), folder = session.Snapshot.Folders.Single(f => f.RelativePath == "Folder").Id;
        _check(session.Commit(session.Prepare(ShortcutOperationKind.Copy, new[] { one, folder }, work)).Success, "Copy batch failed.");
        var copied = session.Snapshot.Shortcuts.Single(entry => entry.ProfileId == work && entry.RelativePath == "One.url");
        _check(copied.Id != one && copied.LaunchCount == 0 && copied.LastUsedUtc == null && !copied.IsPinned && copied.Aliases.Single() == "редактор 편집기" && copied.CustomIcon.Index == 3, "Copy did not reset stats/pin or retain aliases/icon.");
        _check(Directory.Exists(Path.Combine(root, "Profiles", "Work", "Folder", "Empty")) && File.Exists(Path.Combine(root, "Shortcuts", "One.url")), "Copy lost empty folders or moved the source.");
        _check(!File.Exists(Path.Combine(root, "Profiles", "Work", "Folder", "Unknown.txt")) && File.ReadAllText(Path.Combine(root, "Shortcuts", "Folder", "Unknown.txt")) == "unmanaged payload", "Copy touched unmanaged content.");
        _check(session.Snapshot.Settings.ActiveProfileName == "Default", "Copy activated destination profile.");
        session.Navigate(work, "Folder\\Empty");
        _check(session.Commit(session.Prepare(ShortcutOperationKind.CreateFolder, Array.Empty<Guid>(), destinationFolder: session.FolderPath, name: "작업 музыка")).Success, "Unicode child folder creation failed.");
        _check(Directory.Exists(Path.Combine(root, "Profiles", "Work", "Folder", "Empty", "작업 музыка")), "Folder created at wrong location.");
    }
    private static void Conflicts()
    {
        string root = Fixture("conflicts"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        Guid one = Id(session, "One.url"), two = Id(session, "Two.url"), work = Work(session);
        var copy = session.Prepare(ShortcutOperationKind.Copy, new[] { one }, work); _check(session.Commit(copy).Success, "Could not seed collision.");
        var before = Files(root);
        _check(!session.Prepare(ShortcutOperationKind.Copy, new[] { one, two }, work).Result.Success && Files(root).SequenceEqual(before), "Collision preview changed part of a batch.");
        Guid folder = session.Snapshot.Folders.Single(f => f.RelativePath == "Folder").Id;
        _check(!session.Prepare(ShortcutOperationKind.Move, new[] { folder }, ProfileState.DefaultId, "Folder\\Empty").Result.Success, "Descendant move accepted.");
        var cancelled = session.Prepare(ShortcutOperationKind.Move, new[] { two }, work); cancelled.Request.Cancelled = true;
        _check(session.Commit(cancelled).Status == OperationStatus.Cancelled && Files(root).SequenceEqual(before), "Cancellation changed the filesystem.");
    }
    private static void StalePreviews()
    {
        foreach (string kind in new[] { "file", "directory", "revision" })
        {
            string root = Fixture("stale-" + kind); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
            Guid one = Id(session, "One.url"); var preview = session.Prepare(ShortcutOperationKind.Move, new[] { one }, Work(session));
            _check(preview.Result.Success, "Initial stale preview failed.");
            if (kind == "file") File.AppendAllText(Path.Combine(root, "Shortcuts", "One.url"), "\nexternal change");
            else if (kind == "directory") Write(root, "Profiles\\Work\\External.txt", "external file");
            else { var loaded = Load(root); loaded.State.Settings.WidgetDisplayName = "external revision"; _check(new LauncherStateStore(root).Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Could not seed concurrent revision."); }
            var before = Files(root); _check(session.Commit(preview).Status == OperationStatus.Conflict && before.SequenceEqual(Files(root)), "Stale " + kind + " preview mutated data.");
        }
    }
    private static void AliasDrafts()
    {
        string root = Fixture("aliases"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        Guid one = Id(session, "One.url"), two = Id(session, "Two.url");
        session.EditAliases(one); session.SetAliasText("Музыка\n편집기\nмузыка"); session.Navigate(Work(session), ""); session.Refresh(); session.EditAliases(two);
        _check(session.Draft.Id == one && session.Draft.Dirty && session.Draft.Text == "Музыка\n편집기\nмузыка", "Refresh/navigation/selection discarded or rebound the draft.");
        var loaded = Load(root); loaded.State.Shortcuts.Single(entry => entry.Id == one).LaunchCount = 99;
        _check(new LauncherStateStore(root).Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Concurrent usage update failed.");
        _check(session.SaveAliases().Success && session.Draft == null, "Unrelated revision prevented alias save.");
        var saved = Load(root).State.Shortcuts.Single(entry => entry.Id == one);
        _check(saved.Aliases.SequenceEqual(new[] { "Музыка", "편집기" }) && saved.LaunchCount == 99, "Alias save overwrote usage or retained duplicates.");
        session.EditAliases(one); session.SetAliasText("draft remains"); loaded = Load(root); loaded.State.Shortcuts.Single(entry => entry.Id == one).Aliases = new[] { "other editor" };
        _check(new LauncherStateStore(root).Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Concurrent alias update failed.");
        _check(session.SaveAliases().Status == OperationStatus.Conflict && session.Draft.Text == "draft remains", "Alias conflict lost the draft.");
        session.DiscardAliases(); session.Refresh(); session.Navigate(ProfileState.DefaultId, ""); session.EditAliases(two); session.SetAliasText("removed item draft");
        _check(session.Commit(session.Prepare(ShortcutOperationKind.Remove, new[] { two })).Success, "Could not remove drafted item.");
        _check(session.SaveAliases().Status == OperationStatus.Conflict && session.Draft.Dirty, "Removed draft target was silently recreated.");
        session.DiscardAliases(); _check(session.Draft == null, "Discard did not clear the draft.");
    }
    private static void Reorder()
    {
        string root = Fixture("reorder"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        var entries = session.Rows().Where(row => !row.IsFolder).Select(row => row.Id).ToArray();
        var selected = entries.Take(2).ToArray();
        _check(session.Commit(session.Reorder(selected)).Success, "Block reorder failed.");
        _check(session.Rows().Where(row => !row.IsFolder).Select(row => row.Id).SequenceEqual(entries.Skip(2).Concat(selected)), "Block reorder changed internal selection order.");
        _check(session.Commit(session.Reorder(selected, direction: -1)).Success, "Keyboard nudge failed.");
        var expected = entries.Skip(2).ToList(); expected.InsertRange(expected.Count - 1, selected);
        _check(session.Rows().Where(row => !row.IsFolder).Select(row => row.Id).SequenceEqual(expected), "Nudge split the selected block.");
        session.Query = "One"; _check(!session.Reorder(selected).Result.Success, "Filtered reorder accepted."); session.Query = "";
        session.AttentionOnly = true; _check(!session.Reorder(selected).Result.Success, "Attention filter reorder accepted.");
    }
    private static void Presentation()
    {
        string root = Fixture("presentation"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        Guid one = Id(session, "One.url"); session.Query = "편집기";
        _check(session.Rows().Single().Id == one, "Unicode alias search failed.");
        session.Query = ""; session.AttentionOnly = true;
        _check(session.Rows().Single().Id == Id(session, "Missing.lnk") && session.Rows().Single().Status == ShortcutStatus.MissingTarget, "Attention filter hid missing target or included healthy links.");
        File.Delete(Path.Combine(root, "Shortcuts", "Two.url")); session.Refresh();
        _check(session.Rows().Any(row => row.Id == Id(session, "Two.url") && row.Status == ShortcutStatus.NeedsReview), "Missing definition vanished from review view.");
        session.AttentionOnly = false; var drag = session.StartDrag(new[] { one });
        _check(session.FindDrag(drag.Token).Ids.Single() == one && session.FindDrag(Guid.NewGuid().ToString("N")) == null, "Drag accepted a forged token or lost identity.");
        session.EndDrag(drag.Token); _check(session.FindDrag(drag.Token) == null, "Ended drag remains usable.");
        drag = session.StartDrag(new[] { one }); _check(session.Commit(session.Prepare(ShortcutOperationKind.Unpin, new[] { one })).Success && session.FindDrag(drag.Token) == null, "Revision change did not invalidate drag.");
        session.EndDrag(drag.Token);
        session.EditAliases(one); session.SetAliasText("watcher draft");
        Write(root, "Profiles\\Work\\Outside.url", "[InternetShortcut]\nURL=https://example.invalid/outside\n");
        _check(session.Reconcile().Success, "Watcher-style reconcile failed.");
        session.Navigate(Work(session), "");
        _check(session.Rows().Single().RelativePath == "Outside.url" && session.Draft.Text == "watcher draft" && settings.Settings.ActiveProfileName == "Default", "Catalog refresh changed draft or launch profile.");
        var outside = session.Rows().Single().Id;
        File.Move(Path.Combine(root, "Profiles", "Work", "Outside.url"), Path.Combine(root, "Profiles", "Work", "Renamed.url"));
        _check(session.Reconcile(new System.Collections.Generic.Dictionary<string, string> { ["Profiles\\Work\\Outside.url"] = "Profiles\\Work\\Renamed.url" }).Success && session.Rows().Single().Id == outside && session.Rows().Single().RelativePath == "Renamed.url", "Nonactive-profile watcher rename lost stable identity.");
        session.Navigate(ProfileState.DefaultId, "Folder\\Empty");
        Directory.Move(Path.Combine(root, "Shortcuts", "Folder"), Path.Combine(root, "Shortcuts", "Renamed Folder"));
        _check(session.Reconcile(new System.Collections.Generic.Dictionary<string, string> { ["Shortcuts\\Folder"] = "Shortcuts\\Renamed Folder" }).Success && session.FolderPath == "Renamed Folder\\Empty" && session.Draft.Text == "watcher draft", "Watcher folder rename reset the location or draft.");
    }
    private static void ExternalDrop()
    {
        string root = Fixture("drop"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        string external = Path.Combine(_root, "external"); Directory.CreateDirectory(external);
        Write(external, "새 링크.url", "[InternetShortcut]\r\nURL=https://example.invalid/새\r\n"); Write(external, "Opaque.lnk", "opaque exact bytes"); Write(external, "Unsupported.exe", "not supported here");
        string[] sources = { Path.Combine(external, "새 링크.url"), Path.Combine(external, "Opaque.lnk") }; var before = Files(root);
        var preview = session.ExternalLinks(sources, Work(session), "");
        _check(preview.Result.Success && Files(root).SequenceEqual(before), "External drop preview wrote files.");
        _check(session.Commit(preview).Success && sources.All(path => File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "Profiles", "Work", Path.GetFileName(path))))), "External drop changed bytes or source files.");
        before = Files(root);
        _check(!session.ExternalLinks(sources, Work(session), "").Result.Success && !session.ExternalLinks(new[] { sources[0], Path.Combine(external, "Unsupported.exe") }, ProfileState.DefaultId, "").Result.Success && before.SequenceEqual(Files(root)), "Collision/unsupported drop partially wrote a batch.");
    }
    private static void SafeRemove()
    {
        string root = Fixture("remove"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        Guid raw = Id(session, "Raw.exe"), folder = session.Snapshot.Folders.Single(f => f.RelativePath == "Folder").Id;
        var preview = session.Prepare(ShortcutOperationKind.Remove, new[] { raw, folder });
        _check(session.Commit(preview).Success && File.Exists(Path.Combine(root, "Shortcuts", "Raw.exe")) && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Unknown.txt")), "Removal deleted raw EXE or unknown data.");
        var restore = settings.NewOperation(ShortcutOperationKind.Restore); restore.RestoreOperationId = preview.Request.OperationId;
        _check(settings.ExecuteOperation(restore).Success && File.Exists(Path.Combine(root, "Shortcuts", "Folder", "Nested.url")), "Manager removal was not recoverable.");
    }
    private static void FailureRecovery()
    {
        string root = Fixture("failure"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        Guid one = Id(session, "One.url"); var preview = session.Prepare(ShortcutOperationKind.Move, new[] { one }, Work(session));
        var engine = new ShortcutOperationService(root, phase => { if (phase == OperationPhase.FileApplied) throw new IOException("injected organizer file fault"); });
        _check(!engine.Execute(preview.Request).Success, "Injected failure reported success.");
        _check(new ShortcutOperationService(root).RecoverPending().Success, "Pending operation recovery failed.");
        _check(Load(root).State.Shortcuts.Single(entry => entry.Id == one).ProfileId == ProfileState.DefaultId && File.Exists(Path.Combine(root, "Shortcuts", "One.url")), "Failure recovery lost source identity or file.");
    }
    private static void Contracts()
    {
        string root = Path.Combine(_root, "legacy"); var settings = new SettingsService(root); using var manager = Manager(settings); var session = new OrganizerSession(settings, manager);
        _check(session.ReadOnly && !session.Refresh().Success && !session.Prepare(ShortcutOperationKind.CreateFolder, Array.Empty<Guid>(), name: "blocked").Result.Success, "Manager implicitly migrated or wrote legacy data.");
        _check(!JsonSerializer.Serialize(new OperationRequest()).Contains("ExpectedDirectoryFingerprints"), "New optional preview guard changes old serialized request hashes.");
        foreach (string key in new[] { "Title", "Attention", "Move", "Copy", "SaveAliases", "SafeRemove", "UseProfile", "StaleDrag" })
            foreach (string language in new[] { "English", "Русский" })
                _check(LocalizationService.Get("Manager_" + key, language) != "Manager_" + key, "Missing manager resource: " + key + " / " + language);
    }
}
