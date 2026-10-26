using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class Program
{
    private static int _checks;

    [DllImport("shell32.dll")]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr id);

    [STAThread]
    private static void Main()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), "TaskFolder.WindowsIntegrationSmoke." + id);
        string registryRoot = @"Software\TaskFolder.Tests\" + id;
        string runKey = registryRoot + @"\Run";
        Directory.CreateDirectory(root);
        try
        {
            // Spaces and Unicode catch quoting/path regressions; never register a real startup entry.
            string exeFolder = Path.Combine(root, "Андрей App Folder");
            Directory.CreateDirectory(exeFolder);
            string exe = Path.Combine(exeFolder, "TaskFolder.exe");
            File.WriteAllBytes(exe, new byte[] { 0 });
            string programs = Path.Combine(root, "Programs");
            var normal = new WindowsIntegrationService(exe, false, runKey, programs);
            var portable = new WindowsIntegrationService(exe, true, runKey, programs);

            Check(!normal.IsStartupRegistered, "clean profile has no registration");
            normal.SetStartupEnabled(false);
            Check(!normal.IsStartupRegistered, "disable is safe when the Run key is absent");
            normal.SetStartupEnabled(true);
            Check(normal.StartupTargetsCurrentCopy, "missing Run key is created and verified");
            Check(normal.ReadStartupCommand() == $"\"{exe}\" --startup", "normal startup command quotes Unicode/space path");
            using (var key = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                Check(key.GetValueKind("TaskFolder") == RegistryValueKind.String, "startup uses REG_SZ");
                key.SetValue("OtherTool", "preserve me");
                key.SetValue("TaskFolder", "\"C:\\Old Folder\\TaskFolder.exe\"");
            }
            Check(normal.IsStartupRegistered && !normal.StartupTargetsCurrentCopy, "stale registration is detected");
            portable.SetStartupEnabled(true);
            Check(portable.ReadStartupCommand() == $"\"{exe}\" --portable --startup", "portable startup preserves data mode");
            Check(portable.StartupTargetsCurrentCopy, "portable repair is verified");
            normal.SetStartupEnabled(false);
            using (var key = Registry.CurrentUser.OpenSubKey(runKey))
                Check((string)key.GetValue("OtherTool") == "preserve me", "disable preserves other registry entries");
            Check(!normal.IsStartupRegistered, "disable removes only TaskFolder");

            portable.EnsureStartMenuShortcut();
            Check(!File.Exists(portable.StartMenuShortcutPath), "portable launch does not auto-register a shortcut");
            normal.EnsureStartMenuShortcut();
            Check(File.Exists(normal.StartMenuShortcutPath), "normal launch creates the Start menu shortcut");
            VerifyShortcut(normal.StartMenuShortcutPath, exe, "--settings");
            byte[] original = File.ReadAllBytes(normal.StartMenuShortcutPath);
            portable.RegisterStartMenuShortcut();
            VerifyShortcut(portable.StartMenuShortcutPath, exe, "--portable --settings");
            byte[] repaired = File.ReadAllBytes(normal.StartMenuShortcutPath);
            normal.EnsureStartMenuShortcut();
            Check(Convert.ToBase64String(repaired) == Convert.ToBase64String(File.ReadAllBytes(normal.StartMenuShortcutPath)),
                "automatic registration preserves an existing copy");
            normal.RegisterStartMenuShortcut();
            VerifyShortcut(normal.StartMenuShortcutPath, exe, "--settings");
            Check(original.Length > 0, "shortcut is a real shell link");

            var missing = new WindowsIntegrationService(Path.Combine(root, "missing.exe"), false, runKey, programs);
            Expect<FileNotFoundException>(() => missing.SetStartupEnabled(true), "missing executable cannot be registered");
            Expect<FileNotFoundException>(() => missing.RegisterStartMenuShortcut(), "missing executable cannot get a shortcut");
            string longFolder = Path.Combine(root, new string('x', 180), new string('y', 70));
            Directory.CreateDirectory(longFolder);
            string longExe = Path.Combine(longFolder, "TaskFolder.exe");
            File.WriteAllBytes(longExe, new byte[] { 0 });
            var longPath = new WindowsIntegrationService(longExe, false, runKey, programs);
            Expect<InvalidOperationException>(() => longPath.SetStartupEnabled(true), "Run command over 260 characters is rejected");
            Check(!normal.IsStartupRegistered, "failed registrations leave Run unchanged");

            Marshal.ThrowExceptionForHR(WindowsShellLink.SetCurrentProcessExplicitAppUserModelID(WindowsIntegrationService.AppUserModelId));
            Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out IntPtr appId));
            try { Check(Marshal.PtrToStringUni(appId) == WindowsIntegrationService.AppUserModelId, "native process identity matches shortcuts"); }
            finally { Marshal.FreeCoTaskMem(appId); }

            Console.WriteLine($"WINDOWS_INTEGRATION_SMOKE_OK: {_checks} checks; isolated registry and real COM shortcuts; no app GUI launched.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryRoot, throwOnMissingSubKey: false);
            // root is a freshly generated GUID folder created by this test, never a supplied path.
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyShortcut(string path, string exe, string arguments)
    {
        dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object shortcut = null;
        object shell = null;
        object folder = null;
        object item = null;
        try
        {
            shortcut = wsh.CreateShortcut(path);
            dynamic link = shortcut;
            Check(string.Equals((string)link.TargetPath, exe, StringComparison.OrdinalIgnoreCase), "shell reads correct executable");
            Check((string)link.Arguments == arguments, "shell reads correct launch arguments");
            Check(string.Equals((string)link.WorkingDirectory, Path.GetDirectoryName(exe), StringComparison.OrdinalIgnoreCase), "shell reads working directory");
            Check(((string)link.IconLocation).Contains("TaskFolder.exe"), "shortcut uses app icon");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            folder = ((dynamic)shell).NameSpace(Path.GetDirectoryName(path));
            item = ((dynamic)folder).ParseName(Path.GetFileName(path));
            Check((string)((dynamic)item).ExtendedProperty("System.AppUserModel.ID") == WindowsIntegrationService.AppUserModelId,
                "shell reads persistent AppUserModelID");
        }
        finally
        {
            foreach (object obj in new object[] { item, folder, shell, shortcut, wsh })
                if (obj != null) Marshal.FinalReleaseComObject(obj);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        _checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }
}
