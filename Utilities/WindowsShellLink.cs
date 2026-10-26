using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TaskFolder.Utilities
{
    /// <summary>Creates native shell shortcuts with the same identity as the running process.</summary>
    internal static class WindowsShellLink
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        internal static void ConfigureTaskbarWindow(IntPtr window, bool portable)
        {
            Guid iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
            try
            {
                string executable = Services.WindowsIntegrationService.ResolveExecutablePath();
                SetWindowString(store, 2, $"\"{executable}\"" + (portable ? " --portable" : "") + " --menu");
                SetWindowString(store, 3, executable + ",0");
                SetWindowString(store, 4, "TaskFolder");
                // Set identity after relaunch metadata, before the button is shown.
                SetWindowString(store, 5, Services.WindowsIntegrationService.AppUserModelId);
            }
            finally { Marshal.ReleaseComObject(store); }
        }

        private static void SetWindowString(IPropertyStore store, uint id, string text)
        {
            var key = new PropertyKey
            {
                FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = id
            };
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) };
            try { store.SetValue(ref key, ref value); }
            finally { Marshal.FreeCoTaskMem(value.Pointer); }
            // Window property stores apply immediately; Commit is not required by the Shell API.
        }

        internal static void Save(string path, string target, string arguments, string appId)
        {
            bool existed = System.IO.File.Exists(path);
            object shellLink = new ShellLink();
            try
            {
                var link = (IShellLinkW)shellLink;
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
                link.SetDescription("TaskFolder — Windows application launcher");
                link.SetIconLocation(target, 0);
                link.SetShowCmd(1);
                var store = (IPropertyStore)shellLink;
                var key = new PropertyKey
                {
                    FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5
                };
                var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(appId) };
                try { store.SetValue(ref key, ref value); store.Commit(); }
                finally { Marshal.FreeCoTaskMem(value.Pointer); }
                ((IPersistFile)shellLink).Save(path, true);
                // Notify the Shell without restarting Explorer or resetting other apps' icon caches.
                SHChangeNotify(existed ? 0x00002000u : 0x00000002u, 0x0005, path, IntPtr.Zero); // UPDATEITEM/CREATE, PATHW
            }
            finally { Marshal.FinalReleaseComObject(shellLink); }
        }

        internal static void AllowExistingInstanceActivation()
        {
            // A Start/search launch owns foreground permission and can pass it to the tray process.
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("TaskFolder"))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id != current.Id && process.SessionId == current.SessionId)
                            AllowSetForegroundWindow(process.Id);
                    }
                    catch (InvalidOperationException) { /* Process exited while being enumerated. */ }
                    catch (System.ComponentModel.Win32Exception) { /* Keep activation usable if access is denied. */ }
                }
            }
        }

        /// <summary>Edit only the path on a loaded link; preserve arguments, cwd and Shell property stores.</summary>
        internal static void RetargetCopy(string sourcePath, string destinationPath, string targetPath)
        {
            if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
                throw new InvalidOperationException("Shell link editing requires an STA thread.");
            object shellLink = new ShellLink();
            try
            {
                var persist = (IPersistFile)shellLink;
                persist.Load(sourcePath, 0);
                ((IShellLinkW)shellLink).SetPath(targetPath);
                persist.Save(destinationPath, true);
            }
            finally { Marshal.FinalReleaseComObject(shellLink); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint flags, out uint attributes);
        [DllImport("shell32.dll")]
        private static extern int SHGetNameFromIDList(IntPtr pidl, uint kind, out IntPtr name);

        internal static void SaveDefinition(string path, string target, string arguments, string workingDirectory, bool shellItem = false)
        {
            object instance = new ShellLink(); IntPtr pidl = IntPtr.Zero;
            try
            {
                var link = (IShellLinkW)instance;
                if (shellItem)
                {
                    if (!IsApplicationIdentity(target)) throw new ArgumentException("Only AppsFolder identities are supported.");
                    Marshal.ThrowExceptionForHR(SHParseDisplayName(target, IntPtr.Zero, out pidl, 0, out _));
                    link.SetIDList(pidl);
                }
                else { link.SetPath(target); link.SetArguments(arguments ?? string.Empty); link.SetWorkingDirectory(workingDirectory ?? string.Empty); }
                link.SetShowCmd(1); ((IPersistFile)instance).Save(path, true);
            }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); Marshal.FinalReleaseComObject(instance); }
        }

        internal static bool IsApplicationIdentity(string value) => value?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) == true ||
            value?.StartsWith("::{4234d49b-0245-4df3-b780-3893943456e1}\\", StringComparison.OrdinalIgnoreCase) == true;

        internal static (string Target, string Arguments, string WorkingDirectory) ReadDefinition(string path)
        {
            object instance = new ShellLink(); IntPtr pidl = IntPtr.Zero, name = IntPtr.Zero;
            try
            {
                ((IPersistFile)instance).Load(path, 0); var link = (IShellLinkW)instance;
                var target = new StringBuilder(32768); var args = new StringBuilder(32768); var cwd = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetArguments(args, args.Capacity); link.GetWorkingDirectory(cwd, cwd.Capacity);
                if (target.Length == 0)
                {
                    link.GetIDList(out pidl);
                    if (pidl != IntPtr.Zero && SHGetNameFromIDList(pidl, 0x80028000, out name) >= 0) target.Append(Marshal.PtrToStringUni(name));
                }
                return (target.ToString(), args.ToString(), cwd.ToString());
            }
            finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); Marshal.FinalReleaseComObject(instance); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int length, IntPtr data, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int length);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int length);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int length);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int length, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

        // PROPVARIANT's union contains a counted array, hence 24 bytes on x64 (16 on x86).
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort Type;
            private ushort _reserved1, _reserved2, _reserved3;
            public IntPtr Pointer;
            private IntPtr _padding;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }
    }
}
