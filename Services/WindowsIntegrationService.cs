using System;
using System.IO;
using Microsoft.Win32;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    /// <summary>Per-user Windows registration; portable registrations are explicit opt-in.</summary>
    public sealed class WindowsIntegrationService
    {
        public const string AppUserModelId = "TaskFolder.TaskFolder";
        public const string InstanceMutexName = "TaskFolder_SingleInstance";
        public const string ActivationEventName = "TaskFolder_ActivateSettings";
        public const string MenuActivationEventName = "TaskFolder_ActivateMenu";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string _runKey;
        private readonly string _programsFolder;
        private readonly string _executablePath;
        private readonly bool _isPortable;

        public WindowsIntegrationService(bool isPortable = false)
            : this(ResolveExecutablePath(), isPortable, RunKey,
                Environment.GetFolderPath(Environment.SpecialFolder.Programs)) { }

        // Alternate locations let smoke tests exercise real registry/COM APIs in isolation.
        internal WindowsIntegrationService(string executablePath, bool isPortable,
            string runKey, string programsFolder)
        {
            _executablePath = Path.GetFullPath(executablePath);
            _isPortable = isPortable;
            _runKey = runKey;
            _programsFolder = programsFolder;
        }

        public string StartupCommand => $"\"{_executablePath}\"" +
            (_isPortable ? " --portable" : string.Empty) + " --startup";
        public string StartMenuShortcutPath => Path.Combine(_programsFolder, "TaskFolder", "TaskFolder.lnk");

        public string ReadStartupCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKey);
            return key?.GetValue("TaskFolder", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        public bool IsStartupRegistered => !string.IsNullOrWhiteSpace(ReadStartupCommand());
        public bool StartupTargetsCurrentCopy => string.Equals(ReadStartupCommand(), StartupCommand,
            StringComparison.OrdinalIgnoreCase);

        public void SetStartupEnabled(bool enabled)
        {
            if (enabled)
            {
                if (!File.Exists(_executablePath))
                    throw new FileNotFoundException("Keep TaskFolder in a permanent folder before enabling startup.", _executablePath);
                if (StartupCommand.Length > 260)
                    throw new InvalidOperationException("The startup path is too long. Move TaskFolder to a shorter permanent path.");
                // OpenSubKey can return null on a clean user profile. Never silently report success.
                using var key = Registry.CurrentUser.CreateSubKey(_runKey, writable: true)
                    ?? throw new InvalidOperationException("Windows did not allow access to the startup registry key.");
                key.SetValue("TaskFolder", StartupCommand, RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);
                key?.DeleteValue("TaskFolder", throwOnMissingValue: false);
            }

            if (enabled ? !StartupTargetsCurrentCopy : IsStartupRegistered)
                throw new IOException("Windows could not verify the startup registration. Check your account permissions.");
            // Do not write Explorer's private StartupApproved data. Windows owns the enable/disable toggle.
        }

        public void RegisterStartMenuShortcut()
        {
            if (!File.Exists(_executablePath))
                throw new FileNotFoundException("TaskFolder.exe was not found.", _executablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(StartMenuShortcutPath));
            WindowsShellLink.Save(StartMenuShortcutPath, _executablePath,
                (_isPortable ? "--portable " : string.Empty) + "--settings", AppUserModelId);
        }

        public void EnsureStartMenuShortcut()
        {
            // An existing registration may belong to an installed copy. Repair is a user action.
            if (!_isPortable && !File.Exists(StartMenuShortcutPath))
                RegisterStartMenuShortcut();
        }

        internal static string ResolveExecutablePath()
        {
            // Environment.ProcessPath points to dotnet.exe under `dotnet run`.
            string appHost = Path.Combine(AppContext.BaseDirectory, "TaskFolder.exe");
            if (File.Exists(appHost)) return appHost;
            string processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) &&
                Path.GetFileName(processPath).Equals("TaskFolder.exe", StringComparison.OrdinalIgnoreCase))
                return processPath;
            throw new FileNotFoundException("Build or publish TaskFolder.exe before registering it with Windows.");
        }
    }
}
