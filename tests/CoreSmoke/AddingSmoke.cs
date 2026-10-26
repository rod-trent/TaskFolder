using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class AddingSmoke
{
    internal static void Run(string scratchRoot, Action<bool, string> check)
    {
        string root = Path.Combine(scratchRoot, "adding"); Directory.CreateDirectory(Path.Combine(root, "Shortcuts")); Directory.CreateDirectory(Path.Combine(root, "Profiles", "Work"));
        string inputRoot = Path.Combine(scratchRoot, "add-inputs"); Directory.CreateDirectory(inputRoot);
        string exe = Path.Combine(inputRoot, "browser.exe"), file = Path.Combine(inputRoot, "Документ 작업.txt"); File.WriteAllText(exe, "never executed"); File.WriteAllText(file, "external document");
        var migration = new StateMigrationService(root); check(migration.Commit(migration.Preview()).Success, "Add seed migration failed.");
        var settings = new SettingsService(root);
        byte[] Link(string target, string args, string cwd, bool shell) => JsonSerializer.SerializeToUtf8Bytes(new[] { target, args ?? "", cwd ?? "", shell.ToString() });
        (string, string, string) Read(string path) { if (Path.GetFileName(path) == "Broken.lnk") throw new InvalidDataException("Injected unreadable link definition."); var values = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(path)); return (values[0], values[1], values[2]); }
        var service = new AddItemService(settings, Link, Read); Guid work = settings.StateSnapshot.Profiles.Single(p => p.Name == "Work").Id;
        var inputs = new[] { new AddInput { Kind = AddInputKind.Path, Source = file, Name = "Документ" }, new AddInput { Kind = AddInputKind.Path, Source = inputRoot, Name = "Folder" },
            new AddInput { Kind = AddInputKind.Url, Source = "https://example.invalid/작업", Name = "Web" }, new AddInput { Kind = AddInputKind.Command, Source = exe, Name = "PWA one", Arguments = "--app-id=one", WorkingDirectory = inputRoot },
            new AddInput { Kind = AddInputKind.Command, Source = exe, Name = "PWA two", Arguments = "--app-id=two", WorkingDirectory = inputRoot } };
        string before = File.ReadAllText(Path.Combine(root, "state.v2.json")); var preview = service.Prepare(work, "", inputs);
        check(preview.Result.Success && preview.Duplicates.Count == 0 && File.ReadAllText(Path.Combine(root, "state.v2.json")) == before, "Add preview changed state or collapsed distinct PWA arguments.");
        check(service.Commit(preview).Success, "Universal add failed."); settings.Load();
        check(settings.StateSnapshot.Shortcuts.Count == 5 && settings.Settings.ActiveProfileName == "Default", "Add switched launch profile or omitted inputs.");
        var command = Read(Path.Combine(root, "Profiles", "Work", "PWA one.lnk")); check(command.Item1 == exe && command.Item2 == "--app-id=one" && command.Item3 == inputRoot, "Command fields were recombined or lost.");
        check(File.ReadAllText(file) == "external document" && File.ReadAllText(exe) == "never executed", "Add touched input payloads.");
        var duplicate = service.Prepare(work, "", new[] { new AddInput { Kind = AddInputKind.Command, Source = exe, Name = "PWA duplicate", Arguments = "--app-id=one", WorkingDirectory = inputRoot } });
        check(duplicate.Result.Success && duplicate.Duplicates.Count == 1, "Duplicate launch definition not previewed.");
        foreach (string url in new[] { "javascript:alert(1)", "https://example.invalid/\nURL=x", "file:///C:/Windows", "not a url" })
            check(!service.Prepare(work, "", new[] { new AddInput { Kind = AddInputKind.Url, Source = url, Name = "bad" } }).Result.Success, "Unsafe/invalid URL accepted.");
        string link = Path.Combine(inputRoot, "Original.lnk"); File.WriteAllBytes(link, Link(exe, "--app-id=preserved", inputRoot, false));
        var existing = service.Prepare(work, "", new[] { new AddInput { Kind = AddInputKind.ExistingShortcut, Source = link } });
        check(service.Commit(existing).Success && File.ReadAllBytes(link).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "Profiles", "Work", "Original.lnk"))), "Existing link bytes were rewritten.");
        check(!service.Prepare(work, "", new[] { new AddInput { Kind = AddInputKind.ExistingShortcut, Source = link }, new AddInput { Kind = AddInputKind.Url, Source = "invalid", Name = "Bad" } }).Result.Success, "Invalid batch partially accepted.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        check(service.Prepare(work, "", inputs, cancellation.Token).Result.Status == OperationStatus.Cancelled, "Add cancellation ignored.");
        var stale = service.Prepare(work, "", new[] { new AddInput { Kind = AddInputKind.Url, Source = "https://example.invalid/stale", Name = "Stale" } });
        File.WriteAllText(Path.Combine(root, "Profiles", "Work", "outside.txt"), "new external content");
        check(service.Commit(stale).Status == OperationStatus.Conflict && !File.Exists(Path.Combine(root, "Profiles", "Work", "Stale.url")), "Changed destination did not invalidate Add preview.");
        File.WriteAllText(Path.Combine(root,"Profiles","Work","Broken.lnk"),"opaque invalid definition; never launched"); settings.Load();
        check(settings.ExecuteOperation(settings.NewOperation(ShortcutOperationKind.Reconcile)).Success,"Could not seed unrelated broken definition.");
        var besideBroken=service.Prepare(work,"",new[]{new AddInput{Kind=AddInputKind.Url,Source="https://example.invalid/beside-broken",Name="Healthy beside broken"}});
        check(besideBroken.Result.Success && besideBroken.Summary.Any(line=>line.Contains("Broken.lnk")) && service.Commit(besideBroken).Success,"An unrelated invalid existing link blocked a healthy Add or its duplicate-comparison warning was hidden.");
        check(!service.Prepare(work,"",new[]{new AddInput{Kind=AddInputKind.ExistingShortcut,Source=Path.Combine(root,"Profiles","Work","Broken.lnk"),Name="Invalid requested definition"}}).Result.Success,"An invalid explicitly requested definition bypassed validation.");
        Console.WriteLine("PASS adding: file/folder/URL/explicit command/batch, PWA arguments, duplicates, exact link bytes, cancel and stale destination (fake Shell writer; no launch)");
    }
}
