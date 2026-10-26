using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "catalog") return 2;
        string root = Path.Combine(Path.GetTempPath(), "TaskFolder-usability-qa", "completion-20261026", "shell-spike"); Directory.CreateDirectory(root);
        try
        {
            var apps = new List<CatalogApp>(); ShellAppCatalog.Enumerate(apps.Add, CancellationToken.None);
            foreach (string provider in new[] { "Desktop", "PWA", "Packaged" })
            {
                var app = apps.FirstOrDefault(a => a.Provider == provider);
                if (app == null) { Console.WriteLine("UNVERIFIED provider absent: " + provider); continue; }
                string path = Path.Combine(root, provider + "-" + Guid.NewGuid().ToString("N") + ".lnk");
                if (app.LinkPath != null) File.Copy(app.LinkPath, path); else WindowsShellLink.SaveDefinition(path, app.ShellName, null, null, true);
                var parsed = WindowsShellLink.ReadDefinition(path);
                if (app.LinkPath != null) { var original = WindowsShellLink.ReadDefinition(app.LinkPath);
                    if (parsed != original || !File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(app.LinkPath))) throw new Exception("Link identity/bytes changed: " + provider); }
                else if (!WindowsShellLink.IsApplicationIdentity(parsed.Target)) throw new Exception("Packaged identity was not persisted.");
                bool dispatched = false; var launch = new ShortcutLaunchService(_ => { }, start => { if (start.FileName != path) throw new Exception("Dispatch changed link identity."); dispatched = true; }, File.Exists);
                if (!launch.Launch(new ShortcutItem { ShortcutFilePath = path, TargetPath = parsed.Target, Arguments = parsed.Arguments }).Dispatched || !dispatched) throw new Exception("Fake dispatch failed.");
                Console.WriteLine("PASS native enumeration → persisted link → parse → fake dispatch: " + provider);
            }
            Console.WriteLine("Catalog providers: " + string.Join(", ", apps.GroupBy(a => a.Provider).Select(g => g.Key + "=" + g.Count())));
            Console.WriteLine("No application launched; no registration or installed links modified."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": native catalog spike failed; no raw identities logged."); return 1; }
    }
}
