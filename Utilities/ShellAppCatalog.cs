using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TaskFolder.Models;

namespace TaskFolder.Utilities
{
    internal static class ShellAppCatalog
    {
        internal static int Enumerate(Action<CatalogApp> found, CancellationToken cancellation)
        {
            int count = 0; var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string root)
            {
                if (!Directory.Exists(root)) return;
                foreach (string path in Directory.GetFiles(root, "*.lnk"))
                {
                    cancellation.ThrowIfCancellationRequested(); if (!known.Add(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    try
                    {
                        var definition = WindowsShellLink.ReadDefinition(path);
                        var app = new CatalogApp { Name = Path.GetFileNameWithoutExtension(path), Identity = "link:" + path, LinkPath = path,
                            Target = definition.Target, Arguments = definition.Arguments, WorkingDirectory = definition.WorkingDirectory,
                            Provider = definition.Arguments.Contains("--app", StringComparison.OrdinalIgnoreCase) ? "PWA" : "Desktop" };
                        found(app); count++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is COMException || ex is UnauthorizedAccessException) { }
                }
                foreach (string child in Directory.GetDirectories(root))
                { cancellation.ThrowIfCancellationRequested(); if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) { try { Visit(child); } catch (UnauthorizedAccessException) { } } }
            }
            Visit(Environment.GetFolderPath(Environment.SpecialFolder.Programs)); Visit(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
            object shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); dynamic automation = shell;
                folder = automation.NameSpace("shell:AppsFolder"); if (folder == null) return count;
                dynamic apps = folder; items = apps.Items(); dynamic collection = items;
                for (int index = 0; index < collection.Count; index++)
                {
                    cancellation.ThrowIfCancellationRequested(); object item = collection.Item(index);
                    try { dynamic app = item; string path = app.Path;
                        if (!string.IsNullOrEmpty(path) && !Path.IsPathFullyQualified(path) && path.Contains('!') && known.Add("shell:" + path))
                        { found(new CatalogApp { Name = app.Name, Identity = "shell:" + path, ShellName = "shell:AppsFolder\\" + path, Provider = "Packaged" }); count++; } }
                    finally { if (item != null) Marshal.FinalReleaseComObject(item); }
                }
            }
            finally { if (items != null) Marshal.FinalReleaseComObject(items); if (folder != null) Marshal.FinalReleaseComObject(folder); if (shell != null) Marshal.FinalReleaseComObject(shell); }
            return count;
        }
    }
}
