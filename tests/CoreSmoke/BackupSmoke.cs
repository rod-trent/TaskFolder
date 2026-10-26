using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class BackupSmoke
{
    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        string Seed(string name) { string root = Path.Combine(scratchRoot, "backup", name); Directory.CreateDirectory(Path.Combine(root, "Shortcuts", "작업", "Empty"));
            var migration = new StateMigrationService(root); check(migration.Commit(migration.Preview()).Success, "Archive fixture seed failed."); return root; }
        string source = Seed("source"); File.WriteAllText(Path.Combine(source, "Shortcuts", "작업", "One.url"), "[InternetShortcut]\nURL=https://example.invalid/one\n");
        File.WriteAllText(Path.Combine(source, "Shortcuts", "Raw.exe"), "raw executable bytes MUST NOT be exported");
        var settings = new SettingsService(source); check(settings.ExecuteOperation(settings.NewOperation(ShortcutOperationKind.Reconcile)).Success, "Archive reconcile failed.");
        var loaded = new LauncherStateStore(source).Load(); var item = loaded.State.Shortcuts.Single(e => e.RelativePath.EndsWith("One.url")); item.IsPinned = true; item.PinnedOrder = 5; item.SortOrder = 40; item.LaunchCount = 42; item.Aliases = new[] { "편집기", "музыка" }; item.LastUsedUtc = DateTimeOffset.Parse("2026-10-20T07:30:00Z");
        string icon = Path.Combine(source, "fixture.ico"); File.WriteAllBytes(icon, new byte[] { 0, 0, 1, 0, 0, 0 }); item.CustomIcon = new CustomIconReference { Source = IconSource.ExternalReference, Path = icon };
        loaded.State.Settings.Language = "Русский"; loaded.State.Settings.Theme = "Dark"; loaded.State.Settings.WidgetPositionX = 123; loaded.State.Settings.StartWithWindows = true;
        loaded.State.Settings.WidgetSizePercent = 180; loaded.State.Settings.WidgetMenuHideDelayMs = 1200;
        check(new LauncherStateStore(source).Commit(loaded.State, loaded.State.Revision, loaded.Fingerprint).Success, "Archive metadata seed failed.");
        string archive = Path.Combine(scratchRoot, "configuration.zip");
        var exporter = new BackupService(settings, target => Encoding.UTF8.GetBytes("opaque external-link definition: " + Path.GetFileName(target)), _ => ("fake target", "--app-id=one", "fake cwd"));
        var exported = exporter.Export(archive);
        using (var zip = ZipFile.OpenRead(archive)) check(!zip.Entries.Any(e => e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) && zip.Entries.Any(e => e.FullName.StartsWith("icons/")), "Export included raw executable or omitted icon asset.");
        string destination = Seed("destination"); var targetSettings = new SettingsService(destination); var service = new BackupService(targetSettings);
        var preview = service.Preview(archive); check(preview.Result.Success && preview.Imported == 2 && preview.Warnings.Any(w => w.Contains("Raw executable")) && !Directory.Exists(Path.Combine(destination, "Profiles")), "Archive preview wrote files or lost warnings.");
        check(service.Apply(preview).Success, "Archive import failed: " + preview.Result.Code);
        var after = new LauncherStateStore(destination).Load().State; var imported = after.Shortcuts.Single(e => e.RelativePath.EndsWith("One.url"));
        check(imported.Id != item.Id && imported.LaunchCount == 42 && imported.PinnedOrder == 5 && imported.SortOrder == 40 && imported.LastUsedUtc == item.LastUsedUtc && imported.Aliases.SequenceEqual(item.Aliases), "Round-trip changed metadata beyond independent identity.");
        check(after.Folders.Any(f => f.RelativePath == "작업\\Empty") && File.Exists(Path.Combine(destination, imported.CustomIcon.Path)) && imported.CustomIcon.Source == IconSource.ManagedAsset, "Empty folder or managed asset round-trip failed.");
        check(!after.Settings.StartWithWindows && after.Settings.WidgetPositionX == null && after.Settings.Language == "English" && after.Settings.ActiveProfileName == "Default", "Import applied local OS/profile preferences implicitly.");
        check(after.Settings.WidgetSizePercent == 100 && after.Settings.WidgetMenuHideDelayMs == 600,
            "Import changed local widget size or hover-menu lifetime.");
        int count = after.Shortcuts.Count; var repeat = service.Preview(archive); check(repeat.Result.Success && repeat.Request == null && service.Apply(repeat).Success && new LauncherStateStore(destination).Load().State.Shortcuts.Count == count, "Repeated import duplicated configuration.");
        var restore = targetSettings.NewOperation(ShortcutOperationKind.Restore); restore.RestoreOperationId = preview.Request.OperationId;
        check(targetSettings.ExecuteOperation(restore).Success && new LauncherStateStore(destination).Load().State.Shortcuts.All(e => e.Suppressed) && !File.Exists(Path.Combine(destination, imported.CustomIcon.Path)), "Import undo did not restore state/files/assets.");
        // A separately named import can be previewed/committed independently after undo.
        var options = new ImportOptions { ProfilePrefix = "Second ", ApplyPresentationSettings = true };
        preview = service.Preview(archive, options); check(service.Apply(preview).Success, "Second import failed."); targetSettings.Load();
        check(targetSettings.Settings.Language == "Русский" && targetSettings.Settings.Theme == "Dark" && !targetSettings.Settings.StartWithWindows, "Explicit presentation import changed OS settings or ignored selected settings.");
        var mergeOptions = new ImportOptions { ProfilePrefix = "Second ", MergeExisting = true };
        var merge = service.Preview(archive, mergeOptions); check(merge.Result.Success && merge.Imported == 0 && merge.Skipped == 2, "Merge default did not skip collisions.");
        mergeOptions.Conflicts[item.Id] = ImportConflict.Replace; var replaced = service.Preview(archive, mergeOptions); check(replaced.Result.Success && replaced.Imported == 1 && service.Apply(replaced).Success, "Explicit replace could not use journal.");
        mergeOptions.Conflicts[item.Id] = ImportConflict.Rename; mergeOptions.RenamedPaths[item.Id] = "작업\\Another.url";
        check(service.Preview(archive, mergeOptions).Result.Success, "Explicit rename conflict preview failed.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); check(service.Preview(archive, cancellation: cancellation.Token).Result.Status == OperationStatus.Cancelled, "Import cancellation ignored.");
        string Corrupt(string title, Action<ZipArchive> mutate) { string path = Path.Combine(scratchRoot, title + ".zip"); File.Copy(archive, path); using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) mutate(zip); return path; }
        foreach (string path in new[] { "../escape.lnk", "/absolute.lnk", "C:/drive.lnk", "links/evil:stream.lnk", "links/.. /evil.lnk", "links\\mixed.lnk" })
        { string bad = Corrupt("path-" + Guid.NewGuid().ToString("N"), zip => { using var output = zip.CreateEntry(path).Open(); output.WriteByte(1); }); check(!service.Preview(bad).Result.Success, "Unsafe archive path accepted: " + path); }
        string duplicate = Corrupt("duplicate", zip => { var first = zip.Entries.First(e => e.FullName.StartsWith("links/")); var entry = zip.CreateEntry(first.FullName.ToUpperInvariant()); using var output = entry.Open(); output.WriteByte(1); }); check(!service.Preview(duplicate).Result.Success, "Case collision accepted.");
        string checksum = Corrupt("checksum", zip => { string path = zip.Entries.First(e => e.FullName.StartsWith("links/")).FullName; zip.GetEntry(path).Delete(); using var output = zip.CreateEntry(path).Open(); output.WriteByte(99); }); check(!service.Preview(checksum).Result.Success, "Checksum mismatch accepted.");
        string future = Corrupt("future", zip => { var entry = zip.GetEntry("manifest.json"); BackupManifest manifest; using (var input = entry.Open()) manifest = JsonSerializer.Deserialize<BackupManifest>(input, LauncherStateStore.JsonOptions); manifest.SchemaVersion = 999; entry.Delete(); using var output = zip.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(output, manifest, LauncherStateStore.JsonOptions); }); check(!service.Preview(future).Result.Success, "Future archive schema accepted.");
        string missingSchema = Corrupt("missing-schema", zip => { var entry=zip.GetEntry("manifest.json"); string text; using(var input=new StreamReader(entry.Open())) text=input.ReadToEnd(); text=System.Text.RegularExpressions.Regex.Replace(text,"\"SchemaVersion\"\\s*:\\s*1\\s*,",""); entry.Delete(); using var output=new StreamWriter(zip.CreateEntry("manifest.json").Open()); output.Write(text); });
        check(!service.Preview(missingSchema).Result.Success, "Archive without an explicit schema version was accepted.");
        string symlink = Corrupt("symlink", zip => { var entry = zip.CreateEntry("links/symlink.lnk"); entry.ExternalAttributes = unchecked((int)0xa1ff0000); using var output = entry.Open(); output.WriteByte(1); }); check(!service.Preview(symlink).Result.Success, "Symlink payload accepted.");
        string truncated = Path.Combine(scratchRoot, "truncated.zip"); var bytes = File.ReadAllBytes(archive); File.WriteAllBytes(truncated, bytes.Take(bytes.Length / 2).ToArray()); check(!service.Preview(truncated).Result.Success, "Truncated archive accepted.");
        string quota = Path.Combine(scratchRoot, "quota.zip"); using (var zip = ZipFile.Open(quota, ZipArchiveMode.Create)) { using var output = zip.CreateEntry("icons/oversized.ico", CompressionLevel.Optimal).Open(); var block = new byte[1024 * 1024]; for (int i = 0; i < 17; i++) output.Write(block); }
        check(!service.Preview(quota).Result.Success, "Compressed oversized icon accepted.");
        string faultRoot = Seed("fault"); var faultSettings = new SettingsService(faultRoot); var faultPreview = new BackupService(faultSettings).Preview(archive);
        var fault = new ShortcutOperationService(faultRoot, phase => { if (phase == OperationPhase.FileApplied) throw new IOException("injected import failure"); }); check(!fault.Execute(faultPreview.Request).Success && new ShortcutOperationService(faultRoot).RecoverPending().Success, "Import phase failure did not recover.");
        check(new LauncherStateStore(faultRoot).Load().State.Shortcuts.Count == 0 && !File.Exists(Path.Combine(faultRoot, imported.CustomIcon.Path)), "Failed import left committed entries/assets.");
        Console.WriteLine("PASS backup: metadata/empty folders/assets/raw EXE references, idempotency, merge/replace/rename, undo, malformed/future/checksum/traversal/ADS/case/symlink/quota/cancel/fault fixtures; no payload launch");
    }
}
